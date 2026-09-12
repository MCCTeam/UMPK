using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Umpk.IntegrationTests;

/// <summary>Runs one isolated Velocity process against two explicitly owned local backends.</summary>
internal sealed class LocalVelocity : IAsyncDisposable
{
    public const int Port = 25598;

    private readonly Process _process;
    private readonly string _workDir;
    private readonly StringBuilder _log = new();
    private readonly object _logGate = new();
    private bool _disposed;

    private LocalVelocity(Process process, string workDir)
    {
        _process = process;
        _workDir = workDir;
    }

    public string WorkDirectory => _workDir;

    public string LogText
    {
        get
        {
            lock (_logGate)
                return _log.ToString();
        }
    }

    public static async Task<LocalVelocity> StartAsync(
        string jar,
        string javaExe,
        IReadOnlyDictionary<string, int> backends,
        CancellationToken ct)
    {
        if (!File.Exists(jar))
            throw new FileNotFoundException("Velocity jar is not provisioned.", jar);
        if (backends.Count < 2 || !backends.ContainsKey("backend"))
            throw new ArgumentException("The proxy test requires a primary backend and at least one switch target.", nameof(backends));

        string workDir = Path.Combine(Path.GetTempPath(), "umpk-velocity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "forwarding.secret"), "isolated-test-only\n");
        File.WriteAllText(Path.Combine(workDir, "velocity.toml"), Configuration(backends));

        var start = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory = workDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(jar);

        var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start Velocity.");
        var proxy = new LocalVelocity(process, workDir);
        proxy.PumpOutput();
        try
        {
            await proxy.WaitForReadyAsync(ct).ConfigureAwait(false);
            return proxy;
        }
        catch
        {
            await proxy.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static string Configuration(IReadOnlyDictionary<string, int> backends)
    {
        var text = new StringBuilder();
        text.AppendLine("config-version = \"2.9\"");
        text.AppendLine("bind = \"127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "\"");
        text.AppendLine("motd = \"UMPK isolated proxy test\"");
        text.AppendLine("show-max-players = 8");
        text.AppendLine("online-mode = false");
        text.AppendLine("force-key-authentication = false");
        text.AppendLine("prevent-client-proxy-connections = false");
        text.AppendLine("player-info-forwarding-mode = \"none\"");
        text.AppendLine("forwarding-secret-file = \"forwarding.secret\"");
        text.AppendLine("announce-forge = false");
        text.AppendLine("kick-existing-players = false");
        text.AppendLine("sample-players-in-ping = false");
        text.AppendLine("enable-player-address-logging = false");
        text.AppendLine();
        text.AppendLine("[servers]");
        foreach ((string name, int port) in backends)
            text.AppendLine(name + " = \"127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "\"");
        text.AppendLine("try = [\"backend\"]");
        text.AppendLine();
        text.AppendLine("[forced-hosts]");
        text.AppendLine();
        text.AppendLine("[advanced]");
        text.AppendLine("compression-threshold = 256");
        text.AppendLine("compression-level = -1");
        text.AppendLine("login-ratelimit = 0");
        text.AppendLine("connection-timeout = 5000");
        text.AppendLine("read-timeout = 30000");
        text.AppendLine("haproxy-protocol = false");
        text.AppendLine("tcp-fast-open = false");
        text.AppendLine("bungee-plugin-message-channel = true");
        text.AppendLine("show-ping-requests = false");
        text.AppendLine("failover-on-unexpected-server-disconnect = true");
        text.AppendLine("announce-proxy-commands = true");
        text.AppendLine("log-command-executions = true");
        text.AppendLine("log-player-connections = true");
        text.AppendLine("accepts-transfers = false");
        text.AppendLine("enable-reuse-port = false");
        text.AppendLine("command-rate-limit = 50");
        text.AppendLine("forward-commands-if-rate-limited = true");
        text.AppendLine("kick-after-rate-limited-commands = 0");
        text.AppendLine("tab-complete-rate-limit = 10");
        text.AppendLine("kick-after-rate-limited-tab-completes = 0");
        return text.ToString();
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"Velocity exited with code {_process.ExitCode} before readiness.\n{LogText}");

            if (LogText.Contains("Done (", StringComparison.Ordinal))
            {
                using var socket = new TcpClient();
                await socket.ConnectAsync("127.0.0.1", Port, timeout.Token).ConfigureAwait(false);
                return;
            }

            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
        }

        throw new TimeoutException($"Velocity did not become ready.\n{LogText}");
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("end").ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _process.Dispose();
            try
            {
                Directory.Delete(_workDir, recursive: true);
            }
            catch (IOException)
            {
                // The path is recorded in test output if cleanup could not finish immediately.
            }
        }
    }
}
