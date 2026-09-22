using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Umpk.IntegrationTests;

/// <summary>Provisions an isolated server in a temporary directory, configures its dedicated port, waits for both the readiness message and an accepting socket, and stops the process during teardown.</summary>
/// <remarks>Server sessions use port 25599 by default and therefore run sequentially. A test that explicitly owns multiple isolated backends may supply distinct ports. Every wait is bounded, including readiness and file-copy operations.</remarks>
public sealed class LocalServer : IAsyncDisposable
{
    /// <summary>The default integration port. Siblings use 25565/25566; this harness owns 25599 by default.</summary>
    public const int Port = 25599;

    private const int RconPort = 25600;

    private readonly Process _process;
    private readonly string _workDir;
    private readonly int _port;
    private readonly StringBuilder _log = new();
    private readonly object _logGate = new();
    private bool _disposed;

    private LocalServer(Process process, string workDir, int port)
    {
        _process = process;
        _workDir = workDir;
        _port = port;
    }

    /// <summary>The TCP port owned by this isolated server instance.</summary>
    public int ServerPort => _port;

    /// <summary>The captured server stdout/stderr so far.</summary>
    public string LogText
    {
        get
        {
            lock (_logGate)
                return _log.ToString();

        }
    }

    /// <summary>Copies the source server dir, rewrites config, boots the server, and waits for readiness up to <paramref name="bootTimeout"/> (default 120s). Throws on timeout after killing the process.</summary>
    /// <param name="sourceServerDir">The provisioned per-version server directory to clone.</param>
    /// <param name="javaExe">The JVM executable to boot with. Vanilla servers bind a JRE major version, so each leg must pass the runtime resolved for its version (see <see cref="JavaRuntimes"/>). When null the resolution falls back to the <c>UMPK_JAVA</c> environment variable and finally the ambient <c>java</c>.</param>
    /// <param name="bootTimeout">The bounded readiness wait (default 120s).</param>
    /// <param name="ct">Cancellation for the boot wait.</param>
    /// <param name="port">The isolated server listener port.</param>
    /// <param name="rconPort">The matching reserved RCON port, even though RCON is disabled here.</param>
    public static async Task<LocalServer> StartAsync(
        string sourceServerDir,
        string? javaExe = null,
        TimeSpan? bootTimeout = null,
        CancellationToken ct = default,
        int port = Port,
        int rconPort = RconPort)
    {
        ArgumentNullException.ThrowIfNull(sourceServerDir);
        if (!Directory.Exists(sourceServerDir))
            throw new DirectoryNotFoundException($"Source server dir not found: {sourceServerDir}");

        TimeSpan timeout = bootTimeout ?? TimeSpan.FromSeconds(120);
        string workDir = Path.Combine(Path.GetTempPath(), "umpk-itest-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(sourceServerDir, workDir);
        RewriteServerProperties(workDir, port, rconPort);
        File.WriteAllText(Path.Combine(workDir, "eula.txt"), "eula=true\n");

        // The feature tests do not need world datapacks. Removing them from the copy avoids loading a pack whose format is incompatible with the selected server version.
        string datapacks = Path.Combine(workDir, "world", "datapacks");
        if (Directory.Exists(datapacks))
        {
            try
            {
                Directory.Delete(datapacks, recursive: true);
            }
            catch (IOException)
            {
                // best effort; a datapack that cannot be removed will surface as the same boot error
            }
        }

        string jar = Path.Combine(workDir, "server.jar");
        if (!File.Exists(jar))
            throw new FileNotFoundException($"server.jar not present in {workDir}.");

        // The server JVM is version-bound: the caller passes the runtime resolved for this version. Fall back to UMPK_JAVA (single-runtime override for manual probing) and finally the ambient `java`.
        string resolvedJava = javaExe
            ?? (Environment.GetEnvironmentVariable("UMPK_JAVA") is { Length: > 0 } j ? j : "java");
        var psi = new ProcessStartInfo(resolvedJava)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-Xmx1G");
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add("server.jar");
        psi.ArgumentList.Add("nogui");

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start java.");
        var server = new LocalServer(process, workDir, port);
        server.PumpOutput();

        try
        {
            await server.WaitForReadyAsync(timeout, ct).ConfigureAwait(false);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return server;
    }

    /// <summary>Sends a line to the server console stdin (for /kill, /summon, etc.).</summary>
    public async Task SendConsoleAsync(string command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _process.StandardInput.WriteLineAsync(command.AsMemory(), ct).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task WaitForReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        while (!timeoutCts.IsCancellationRequested)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"Server process exited early (code {_process.ExitCode}) before readiness.\n{LogText}");

            if (LogText.Contains("Done (", StringComparison.Ordinal) ||
                LogText.Contains(@"""Done""", StringComparison.Ordinal))
            {
                // "Done" is stdout truth, not socket truth: the listener can lag the log line (or, on a wedged boot, never open). Probe the real TCP port before declaring ready so the caller's first login attempt lands on an accepting socket.
                await WaitForPortAsync(timeoutCts.Token).ConfigureAwait(false);
                return;
            }

            try
            {
                await Task.Delay(250, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }

        throw new TimeoutException($"Server did not report Done within {timeout.TotalSeconds:0}s.\n{LogText}");
    }

    /// <summary>Post-"Done" readiness probe: a throwaway socket connect against the server port, retried on a short bounded loop. The connection closes immediately after being accepted (a pre-handshake disconnect the server tolerates silently), so the probe proves listener readiness without starting a login.</summary>
    private async Task WaitForPortAsync(CancellationToken ct)
    {
        const int Attempts = 40;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"Server process exited (code {_process.ExitCode}) after Done but before the port accepted.\n{LogText}");

            try
            {
                using var probe = new System.Net.Sockets.TcpClient();
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(TimeSpan.FromSeconds(2));
                await probe.ConnectAsync("127.0.0.1", _port, attemptCts.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"Server reported Done but port {_port} never accepted a connection.\n{LogText}");
    }

    private void PumpOutput()
    {
        _process.OutputDataReceived += OnData;
        _process.ErrorDataReceived += OnData;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        void OnData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null)
                return;

            lock (_logGate)
                _log.AppendLine(e.Data);

        }
    }

    private static void RewriteServerProperties(string workDir, int port, int rconPort)
    {
        string path = Path.Combine(workDir, "server.properties");
        var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : [];
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["server-port"] = port.ToString(CultureInfo.InvariantCulture),
            ["query.port"] = port.ToString(CultureInfo.InvariantCulture),
            ["online-mode"] = "false",
            // Vanilla 26.3 flips the white-list default to true, so a fresh isolated server would reject every login; force it (and its enforcer) off on every era, where it was already the default.
            ["white-list"] = "false",
            ["enforce-whitelist"] = "false",
            ["enable-rcon"] = "false",
            ["rcon.port"] = rconPort.ToString(CultureInfo.InvariantCulture),
            ["level-name"] = "world",
            ["spawn-protection"] = "0",
            ["max-players"] = "5",
            ["view-distance"] = "6",
            ["sync-chunk-writes"] = "false",
            // Disable packet compression so the login flow never hits the set_compression negotiation window; the harness records/asserts pre-decode frames either way, and this keeps the login handshake deterministic across 1.8 / 1.21.5 / 26.2.
            ["network-compression-threshold"] = "-1",
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || line.StartsWith('#'))
                continue;

            string key = line[..eq];
            if (overrides.TryGetValue(key, out string? value))
            {
                lines[i] = key + "=" + value;
                seen.Add(key);
            }
        }

        foreach ((string key, string value) in overrides)
            if (!seen.Contains(key))
                lines.Add(key + "=" + value);

        File.WriteAllLines(path, lines);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            // Skip logs to keep the copy lean; they are large and irrelevant.
            string rel = Path.GetRelativePath(source, dir);
            if (rel.StartsWith("logs", StringComparison.Ordinal))
                continue;

            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(source, file);
            if (rel.StartsWith("logs", StringComparison.Ordinal))
                continue;

            // Skip world/session.lock: a running (or ungracefully killed) server holds a mandatory lock on it, so File.Copy can block indefinitely trying to open it for read. Minecraft recreates session.lock on boot, so copying it is both unnecessary and hazardous.
            string fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "session.lock", StringComparison.Ordinal))
                continue;

            // Skip links. Bound every other copy so a FIFO, socket, or other special inode cannot stall the integration leg while waiting for a peer.
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            string target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!TryCopyBounded(file, target) && File.Exists(target))
            {
                // Non-copyable within the budget (FIFO/socket/other special inode): skip it. The server recreates any runtime pipe it needs on boot.
                try
                {
                    File.Delete(target);
                }
                catch (IOException)
                {
                    // best effort
                }
            }
        }
    }

    /// <summary>Copies one file, abandoning it if the copy does not finish within a few seconds. Guards against non-regular inodes (FIFOs/sockets) whose <see cref="File.Copy(string, string, bool)"/> blocks indefinitely. Returns false when the copy was abandoned or failed.</summary>
    private static bool TryCopyBounded(string source, string target)
    {
        var copy = Task.Run(() =>
        {
            try
            {
                File.Copy(source, target, overwrite: true);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        });

        return copy.Wait(TimeSpan.FromSeconds(5)) && copy.Result;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                // Graceful stop first, then hard kill.
                try
                {
                    await _process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                    await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // stdin may be closed; fall through to kill.
                }

                using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    await _process.WaitForExitAsync(killCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // did not stop in time
                }
            }
        }
        finally
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(10_000);
                }
            }
            catch (Exception)
            {
                // best effort
            }

            _process.Dispose();
            TryDeleteDirectory(_workDir);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);

        }
        catch (Exception)
        {
            // leave the temp dir if the OS still holds a handle; it is under the temp root.
        }
    }
}
