using System.Diagnostics;
using System.Security.Cryptography;
using Xunit;

namespace Umpk.Bedrock.IntegrationTests;

internal sealed class BedrockServerHarness : IAsyncDisposable
{
    internal const string SourceDistribution = "/opt/minecraft-bedrock-server";
    internal const string SourceDistributionEnvironmentVariable = "UMPK_BDS_SOURCE_DIR";
    internal const string EvidenceRootEnvironmentVariable = "UMPK_BDS_EVIDENCE_ROOT";
    internal const string ExpectedExecutableSha256 = "297ce10bee4b1e7486ac8fb22be51dbfa151e69d76dfd88c84da524cb6ffefce";
    internal const string ExpectedSourceSha256 = "8ba803f23d681816495c7ac83bdba4b9cd7165a3bee5aedd18fa0c8c3d408ec2";
    internal const string DebuggerExecutable = "/usr/bin/gdb";
    internal const string InferiorPidMarkerFileName = "bds-inferior-pid.txt";
    internal const string EvidenceDirectoryEnvironmentVariable = "UMPK_BDS_EVIDENCE_DIR";
    internal const string InferiorPidFileEnvironmentVariable = "UMPK_BDS_INFERIOR_PID_FILE";

    private readonly List<string> _output = [];
    private readonly Lock _outputGate = new();
    private Process? _process;
    private bool _disposed;
    private readonly bool _strictMovement;
    private readonly bool _creativeMode;
    private readonly bool _lanVisibility;
    private readonly string? _debuggerCommandFile;
    private int _inferiorProcessId = -1;

    internal string RunDirectory { get; private set; } = string.Empty;

    internal string EvidenceDirectory { get; private set; } = string.Empty;

    /// <summary>Gets the absolute source path captured when this harness was constructed.</summary>
    internal string SelectedSourceDistribution { get; }

    /// <summary>Gets the evidence parent captured when this harness was constructed; each run retains its own child directory.</summary>
    internal string EvidenceRootDirectory { get; }

    internal int Port { get; private set; }

    internal int IPv6Port { get; private set; }

    internal string ExecutableSha256 { get; private set; } = string.Empty;

    internal string ConfigSha256 { get; private set; } = string.Empty;

    internal bool StrictMovementRequested => _strictMovement;

    internal string EffectiveMovementStrict => _strictMovement ? "true" : "false";

    internal bool CreativeModeRequested => _creativeMode;

    internal bool LanVisibilityRequested => _lanVisibility;

    internal string EffectiveLanVisibility => _lanVisibility ? "true" : "false";

    /// <summary>Initializes the harness and captures optional absolute source/evidence environment paths; an absolute GDB script path opts one isolated run into the debugger child.</summary>
    /// <remarks><para>The script receives the evidence directory and inferior PID marker path through environment variables, writes the inferior PID as decimal text to the marker file, and auto-continues its breakpoints because batch mode has no prompt while piped stdin still reaches the inferior.</para></remarks>
    internal BedrockServerHarness(bool strictMovement = false, bool creativeMode = false, bool lanVisibility = false, string? debuggerCommandFile = null, Func<string, string?>? environmentVariableReader = null)
    {
        _strictMovement = strictMovement;
        _creativeMode = creativeMode;
        _lanVisibility = lanVisibility;
        if (debuggerCommandFile is not null && !Path.IsPathFullyQualified(debuggerCommandFile))
            throw new ArgumentException("The debugger command file must be an absolute path when provided.", nameof(debuggerCommandFile));
        _debuggerCommandFile = debuggerCommandFile;
        Func<string, string?> readEnvironment = environmentVariableReader ?? Environment.GetEnvironmentVariable;
        SelectedSourceDistribution = ResolvePathOverride(readEnvironment(SourceDistributionEnvironmentVariable), SourceDistribution, SourceDistributionEnvironmentVariable);
        EvidenceRootDirectory = ResolvePathOverride(readEnvironment(EvidenceRootEnvironmentVariable), Path.Combine(Path.GetTempPath(), "umpk-bedrock-live-join"), EvidenceRootEnvironmentVariable);
        ValidateEvidenceRoot(EvidenceRootDirectory, SelectedSourceDistribution);
    }

    /// <summary>Gets the owned launcher PID, which is the GDB PID in debugger mode and the BDS PID otherwise.</summary>
    internal int ProcessId => _process?.Id ?? -1;

    /// <summary>Gets whether the harness launches BDS under the opt-in GDB child for an isolated diagnostic.</summary>
    internal bool IsDebuggerLaunch => _debuggerCommandFile is not null;

    /// <summary>Gets the absolute GDB command-file path selected at construction, or null for the default direct BDS launch.</summary>
    internal string? DebuggerCommandFile => _debuggerCommandFile;

    /// <summary>Gets the PID of the owned launcher process, which is the direct BDS PID by default and the GDB PID when the debugger seam is active.</summary>
    internal int LauncherProcessId => _process?.Id ?? -1;

    /// <summary>Gets the BDS inferior PID reported by the debugger script, or the direct BDS PID when no debugger is active, or -1 before it is known.</summary>
    internal int InferiorProcessId => _debuggerCommandFile is not null ? _inferiorProcessId : _process?.Id ?? -1;

    /// <summary>Gets the evidence-relative marker file the debugger script writes with the inferior BDS PID.</summary>
    internal string InferiorPidMarkerPath => Path.Combine(EvidenceDirectory, InferiorPidMarkerFileName);

    internal IReadOnlyList<string> Output
    {
        get
        {
            lock (_outputGate)
                return [.. _output];
        }
    }

    internal static void EnsureLinuxDistribution()
    {
        string source = ResolvePathOverride(Environment.GetEnvironmentVariable(SourceDistributionEnvironmentVariable), SourceDistribution, SourceDistributionEnvironmentVariable);
        EnsureLinuxDistribution(source);
    }

    private static void EnsureLinuxDistribution(string source)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("The Bedrock live leg requires the Linux BDS executable.");
        if (!File.Exists(Path.Combine(source, "bedrock_server")))
            throw new FileNotFoundException($"The BDS distribution was not found at {source}.");
    }

    private static string ResolvePathOverride(string? value, string fallback, string variable)
    {
        if (value is null)
            return fallback;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new ArgumentException($"{variable} must be an absolute directory path when provided.", variable);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static void ValidateEvidenceRoot(string evidenceRoot, string source)
    {
        if (IsWithinDirectory(evidenceRoot, source))
            throw new ArgumentException($"{EvidenceRootEnvironmentVariable} must remain outside the source distribution.", EvidenceRootEnvironmentVariable);
        for (DirectoryInfo? parent = new(AppContext.BaseDirectory); parent is not null; parent = parent.Parent)
        {
            if (HasGitCheckoutMarker(parent.FullName))
            {
                if (IsWithinDirectory(evidenceRoot, parent.FullName))
                    throw new ArgumentException($"{EvidenceRootEnvironmentVariable} must remain outside the harness Git checkout.", EvidenceRootEnvironmentVariable);
                break;
            }
        }
    }

    internal static bool HasGitCheckoutMarker(string directory)
    {
        string marker = Path.Combine(directory, ".git");
        string gitDirectory;
        if (Directory.Exists(marker))
        {
            gitDirectory = marker;
        }
        else
        {
            if (!File.Exists(marker))
                return false;
            string gitFile = File.ReadAllText(marker).Trim();
            if (!gitFile.StartsWith("gitdir: ", StringComparison.Ordinal))
                return false;
            string target = gitFile[8..].Trim();
            if (target.Length == 0)
                return false;
            gitDirectory = Path.GetFullPath(target, directory);
        }

        string headPath = Path.Combine(gitDirectory, "HEAD");
        if (!File.Exists(headPath))
            return false;
        string head = File.ReadAllText(headPath).Trim();
        bool validHead = head.StartsWith("ref: refs/", StringComparison.Ordinal) && head.Length > "ref: refs/".Length && !head[5..].Any(static character => char.IsWhiteSpace(character));
        if (!validHead)
            validHead = (head.Length is 40 or 64) && head.All(static character => char.IsAsciiHexDigit(character));
        if (!validHead)
            return false;

        string commonDirectory = gitDirectory;
        string commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
        if (File.Exists(commonDirectoryFile))
        {
            string commonTarget = File.ReadAllText(commonDirectoryFile).Trim();
            if (commonTarget.Length == 0)
                return false;
            commonDirectory = Path.GetFullPath(commonTarget, gitDirectory);
        }
        return Directory.Exists(Path.Combine(commonDirectory, "objects")) && Directory.Exists(Path.Combine(commonDirectory, "refs"));
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        string relative = Path.GetRelativePath(directory, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    internal async Task StartAsync(CancellationToken cancellationToken = default)
    {
        string executable = Path.Combine(SelectedSourceDistribution, "bedrock_server");
        ExecutableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, cancellationToken).ConfigureAwait(false))).ToLowerInvariant();
        if (!string.Equals(ExecutableSha256, ExpectedExecutableSha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"The BDS executable hash {ExecutableSha256} does not match the pinned 1.26.45.1 identity.");
        EnsureLinuxDistribution(SelectedSourceDistribution);

        string runId = Guid.NewGuid().ToString("N")[..12];
        RunDirectory = Path.Combine(Path.GetTempPath(), $"umpk-bds-{runId}");
        EvidenceDirectory = Path.Combine(EvidenceRootDirectory, runId);
        if (OperatingSystem.IsLinux())
            Directory.CreateDirectory(EvidenceDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else
            Directory.CreateDirectory(EvidenceDirectory);
        await CopyDistributionAsync(SelectedSourceDistribution, RunDirectory, cancellationToken).ConfigureAwait(false);
        string copiedExecutable = Path.Combine(RunDirectory, "bedrock_server");
        string copiedSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(copiedExecutable, cancellationToken).ConfigureAwait(false))).ToLowerInvariant();
        if (!string.Equals(copiedSha256, ExpectedExecutableSha256, StringComparison.Ordinal))
            throw new InvalidOperationException($"The copied BDS executable hash {copiedSha256} does not match the pinned 1.26.45.1 identity.");

        (Port, IPv6Port) = AllocatePorts();
        ConfigSha256 = WriteServerProperties(runId);

        _inferiorProcessId = -1;
        ProcessStartInfo startInfo;
        if (_debuggerCommandFile is null)
        {
            startInfo = new ProcessStartInfo(Path.Combine(RunDirectory, "bedrock_server"))
            {
                WorkingDirectory = RunDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.Environment["LD_LIBRARY_PATH"] = RunDirectory;
        }
        else
        {
            startInfo = BuildDebuggerStartInfo(Path.Combine(RunDirectory, "bedrock_server"), _debuggerCommandFile);
        }

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("The BDS process failed to start.");
        _process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (_outputGate)
                    _output.Add(args.Data);
            }
        };
        _process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (_outputGate)
                    _output.Add($"[stderr] {args.Data}");
            }
        };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool started;
            lock (_outputGate)
                started = _output.Any(static line => line.Contains("Server started.", StringComparison.Ordinal));
            if (started)
            {
                if (_debuggerCommandFile is null)
                    return;
                await ResolveInferiorProcessIdAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (_process.HasExited)
                throw new InvalidOperationException($"The BDS process exited early with code {_process.ExitCode}.");

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        if (_debuggerCommandFile is not null)
            KillOwnedLauncherTree();
        throw new TimeoutException($"The BDS process did not report startup. Captured output:{Environment.NewLine}{string.Join(Environment.NewLine, Output.Take(50))}");
    }

    /// <summary>Builds the opt-in GDB launcher for one isolated diagnostic while keeping run directory, library path, redirection, and target executable identical to the direct launch.</summary>
    /// <remarks>The launcher runs /usr/bin/gdb -q -batch -x script --args runDir/bedrock_server so cleanup targets its owned process tree without matching a server by name.</remarks>
    private ProcessStartInfo BuildDebuggerStartInfo(string serverExecutable, string commandFile)
    {
        if (!File.Exists(commandFile))
            throw new FileNotFoundException($"The debugger command file was not found at {commandFile}.");
        if (!File.Exists(DebuggerExecutable))
            throw new FileNotFoundException($"The debugger executable was not found at {DebuggerExecutable}.");
        var debuggerStartInfo = new ProcessStartInfo(DebuggerExecutable)
        {
            WorkingDirectory = RunDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        debuggerStartInfo.ArgumentList.Add("-q");
        debuggerStartInfo.ArgumentList.Add("-batch");
        debuggerStartInfo.ArgumentList.Add("-x");
        debuggerStartInfo.ArgumentList.Add(commandFile);
        debuggerStartInfo.ArgumentList.Add("--args");
        debuggerStartInfo.ArgumentList.Add(serverExecutable);
        debuggerStartInfo.Environment["LD_LIBRARY_PATH"] = RunDirectory;
        debuggerStartInfo.Environment[EvidenceDirectoryEnvironmentVariable] = EvidenceDirectory;
        debuggerStartInfo.Environment[InferiorPidFileEnvironmentVariable] = InferiorPidMarkerPath;
        return debuggerStartInfo;
    }

    /// <summary>Waits for the debugger script to report the BDS inferior PID marker so launcher and inferior PIDs stay distinct, and fails clearly when the marker is absent after Server started.</summary>
    private async Task ResolveInferiorProcessIdAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset markerDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTimeOffset.UtcNow < markerDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadInferiorPidMarker(out int inferiorPid))
            {
                _inferiorProcessId = inferiorPid;
                return;
            }

            if (_process is null || _process.HasExited)
                throw new InvalidOperationException($"The debugger launcher exited before reporting the inferior BDS PID marker at {InferiorPidMarkerPath}.");
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"The debugger script did not report the inferior BDS PID marker at {InferiorPidMarkerPath} after Server started; launcher PID {LauncherProcessId} cannot be confused with the inferior PID.");
    }

    /// <summary>Reads the inferior PID marker file written by the debugger script without trusting process names or unrelated servers.</summary>
    private bool TryReadInferiorPidMarker(out int inferiorPid)
    {
        inferiorPid = -1;
        string markerPath = InferiorPidMarkerPath;
        string text;
        try
        {
            if (!File.Exists(markerPath))
                return false;
            text = File.ReadAllText(markerPath).Trim();
        }
        catch (IOException)
        {
            return false;
        }

        if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
            return false;
        inferiorPid = parsed;
        return true;
    }

    /// <summary>Kills only the owned launcher process tree for the debugger timeout path and never matches a server by name.</summary>
    private void KillOwnedLauncherTree()
    {
        Process? launcher = _process;
        if (launcher is null)
            return;
        try
        {
            if (!launcher.HasExited)
                launcher.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

    }

    internal IReadOnlyList<string> FindPlayerLines(string playerName) =>
        Output.Where(line => line.Contains(playerName, StringComparison.Ordinal)).ToArray();

    internal async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        Process? process = _process;
        if (process is null || process.HasExited)
            throw new InvalidOperationException("The BDS process is not running.");
        await process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>Stops the owned server, where the graceful stop line uses launcher stdin because GDB batch forwards piped stdin to the inferior.</summary>
    internal async Task StopAsync()
    {
        Process? process = _process;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: _debuggerCommandFile is not null);
            }
            catch (InvalidOperationException)
            {
            }
        }

        try
        {
            await File.WriteAllLinesAsync(Path.Combine(EvidenceDirectory, "bds-stdout.log"), Output).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _process?.Dispose();
        try
        {
            if (Directory.Exists(RunDirectory))
                Directory.Delete(RunDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal static async Task CopyDistributionAsync(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        using var copy = Process.Start(BuildDistributionCopyStartInfo(source, destination)) ?? throw new InvalidOperationException("The BDS distribution copy failed to start.");
        try
        {
            await copy.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!copy.HasExited)
                    copy.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await copy.WaitForExitAsync(cleanupTimeout.Token).ConfigureAwait(false);
            throw;
        }
        if (copy.ExitCode != 0)
            throw new InvalidOperationException($"The BDS distribution copy exited with code {copy.ExitCode}.");
    }

    internal static ProcessStartInfo BuildDistributionCopyStartInfo(string source, string destination)
    {
        var startInfo = new ProcessStartInfo("cp") { UseShellExecute = false };
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(Path.Combine(source, "."));
        startInfo.ArgumentList.Add(destination);
        return startInfo;
    }

    private static (int Port, int IPv6Port) AllocatePorts()
    {
        var random = new Random();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            int port = random.Next(30000, 50000);
            if (TryBind(port) && TryBind(port + 1))
                return (port, port + 1);
        }

        throw new InvalidOperationException("The harness could not allocate an isolated UDP port pair.");
    }

    private static bool TryBind(int port)
    {
        try
        {
            using var socket = new System.Net.Sockets.UdpClient(port);
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    internal static List<string> ApplyProperties(IEnumerable<string> existing, int port, int ipv6Port, string runId, bool strictMovement, bool creativeMode = false, bool lanVisibility = false)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["server-port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["server-portv6"] = ipv6Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["online-mode"] = "false",
            ["allow-list"] = "false",
            ["enable-lan-visibility"] = lanVisibility ? "true" : "false",
            ["texturepack-required"] = "false",
            ["level-name"] = $"umpk-live-{runId}",
            ["content-log-file-enabled"] = "false",
        };
        if (strictMovement)
            overrides["server-authoritative-movement-strict"] = "true";
        if (creativeMode)
            overrides["gamemode"] = "creative";
        var lines = new List<string>();
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in existing)
        {
            string key = line.Split('=', 2)[0].Trim();
            if (overrides.TryGetValue(key, out string? value))
            {
                lines.Add($"{key}={value}");
                replaced.Add(key);
            }
            else
            {
                lines.Add(line);
            }
        }

        foreach ((string key, string value) in overrides)
        {
            if (!replaced.Contains(key))
                lines.Add($"{key}={value}");
        }

        return lines;
    }

    private string WriteServerProperties(string runId)
    {
        string path = Path.Combine(RunDirectory, "server.properties");
        IReadOnlyList<string> existing = File.Exists(path) ? File.ReadAllLines(path) : [];
        List<string> lines = ApplyProperties(existing, Port, IPv6Port, runId, _strictMovement, _creativeMode, _lanVisibility);
        File.WriteAllLines(path, lines);
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }
}
