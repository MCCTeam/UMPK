using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Umpk.TestKit;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>Owns an isolated vanilla process through the maintained launcher, retaining its server log and readiness/exit receipts outside the checkout.</summary>
internal sealed class VanillaInteractionAcceptanceHarness : IAsyncDisposable
{
    private readonly Process _launcher;
    private readonly Task<string> _stdout;
    private readonly Task<string> _stderr;
    private readonly string _script;
    private readonly List<object> _commands = [];
    private bool _disposed;

    private VanillaInteractionAcceptanceHarness(Process launcher, string script, string directory, int port, int rconPort)
    {
        _launcher = launcher;
        _script = script;
        DirectoryPath = directory;
        Port = port;
        RconPort = rconPort;
        _stdout = launcher.StandardOutput.ReadToEndAsync();
        _stderr = launcher.StandardError.ReadToEndAsync();
    }

    public string DirectoryPath { get; }
    public int Port { get; }
    public int RconPort { get; }
    public string ServerLog => File.Exists(Path.Combine(DirectoryPath, "server.log")) ? File.ReadAllText(Path.Combine(DirectoryPath, "server.log")) : string.Empty;

    public static async Task<VanillaInteractionAcceptanceHarness> StartAsync(string release, int protocol, CancellationToken ct)
    {
        string producer = Environment.GetEnvironmentVariable("UMPK_INTERACTION_PRODUCER") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(producer))
            throw new InvalidOperationException("Set UMPK_INTERACTION_PRODUCER to the immutable source snapshot identity before accepting a live leg.");
        string oracle = Environment.GetEnvironmentVariable("UMPK_ORACLE_ROOT")
            ?? throw new InvalidOperationException("Set UMPK_ORACLE_ROOT to the verified vanilla artifact root.");
        string seed = IntegrationConfig.ServerDir(release);
        using JsonDocument artifacts = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(seed, "artifacts.json"), ct));
        JsonElement server = artifacts.RootElement.EnumerateArray().Single(row => row.GetProperty("name").GetString() == "server.jar");
        string jarHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(seed, "server.jar"), ct)));
        Assert.Equal(server.GetProperty("sha256").GetString(), jarHash);
        Assert.Contains("eula=true", await File.ReadAllLinesAsync(Path.Combine(seed, "eula.txt"), ct));

        string evidenceRoot = Environment.GetEnvironmentVariable("UMPK_INTERACTION_EVIDENCE") ?? Path.Combine(Path.GetTempPath(), "umpk-interaction-acceptance");
        string directory = Path.Combine(evidenceRoot, release + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceRoot);
        string script = Path.Combine(FixturePaths.RepoRoot(), "tools", "minecraft-official", "server.py");
        using var gameReservation = new TcpListener(IPAddress.Loopback, 0);
        using var rconReservation = new TcpListener(IPAddress.Loopback, 0);
        gameReservation.Start();
        rconReservation.Start();
        int port = ((IPEndPoint)gameReservation.LocalEndpoint).Port;
        int rconPort = ((IPEndPoint)rconReservation.LocalEndpoint).Port;
        var start = Python(script, release, "--root", oracle, "--run-dir", directory, "--java", JavaRuntimes.ForProtocol(protocol), "--port", port.ToString(CultureInfo.InvariantCulture), "--rcon-port", rconPort.ToString(CultureInfo.InvariantCulture), "--timeout", "180");
        start.RedirectStandardInput = true;
        gameReservation.Stop();
        rconReservation.Stop();
        var harness = new VanillaInteractionAcceptanceHarness(Process.Start(start) ?? throw new InvalidOperationException("Could not start the owned vanilla launcher."), script, directory, port, rconPort);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            while (!File.Exists(Path.Combine(directory, "ready.json")))
            {
                if (harness._launcher.HasExited)
                    throw new InvalidOperationException($"Vanilla launcher exited {harness._launcher.ExitCode}: {await harness._stderr}\n{harness.ServerLog}");
                await Task.Delay(250, deadline.Token);
            }
            await harness.WriteAsync("producer.json", new
            {
                commit = producer,
                release,
                protocol,
                java = JavaRuntimes.ForProtocol(protocol),
                port,
                rconPort,
                serverSha256 = jarHash,
                testSources = new[] { "VanillaInteractionAcceptanceHarness.cs", "VanillaInteractionAcceptanceLiveTests.cs", "VanillaInteractionAcceptanceDiagnostics.cs" }
                    .ToDictionary(name => name, name => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.IntegrationTests", name))))),
                binaries = Directory.GetFiles(AppContext.BaseDirectory, "Umpk.*.dll").ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))),
            });
            return harness;
        }
        catch
        {
            await harness.DisposeAsync();
            throw;
        }
    }

    public async Task<string> CommandAsync(string command, CancellationToken ct)
    {
        using Process process = Process.Start(Python(_script, "--rcon-port", RconPort.ToString(CultureInfo.InvariantCulture), "--password-file", Path.Combine(DirectoryPath, "rcon-password.txt"), "--rcon-command", command))
            ?? throw new InvalidOperationException("Could not start the RCON client.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // The owned command can exit between the status check and termination.
            }
            using var killDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(killDeadline.Token);
            throw;
        }
        string response = await stdout;
        string error = await stderr;
        _commands.Add(new { command, response, error, exitCode = process.ExitCode });
        await WriteAsync("commands.json", _commands);
        Assert.True(process.ExitCode == 0, $"RCON failed for {command}: {error}");
        return response.Trim();
    }

    public async Task ConsoleAsync(string command, CancellationToken ct)
    {
        // Legacy RCON executes on its own thread. World mutations must use the server's console queue.
        int before = ServerLog.Length;
        await _launcher.StandardInput.WriteLineAsync(command.AsMemory(), ct);
        await _launcher.StandardInput.FlushAsync(ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (ServerLog.Length == before)
            await Task.Delay(25, deadline.Token);
        string marker = "interaction-console-" + Guid.NewGuid().ToString("N");
        await _launcher.StandardInput.WriteLineAsync(("say " + marker).AsMemory(), ct);
        await _launcher.StandardInput.FlushAsync(ct);
        while (!ServerLog.Contains(marker, StringComparison.Ordinal))
            await Task.Delay(25, deadline.Token);
        _commands.Add(new { command, channel = "console", completionMarker = marker, response = ServerLog[before..] });
        await WriteAsync("commands.json", _commands);
    }

    public Task WriteAsync(string name, object value) => File.WriteAllTextAsync(Path.Combine(DirectoryPath, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }) + "\n");

    private static ProcessStartInfo Python(params string[] arguments)
    {
        var start = new ProcessStartInfo("python3") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        bool forced = false;
        string? pipeError = null;
        try
        {
            if (!_launcher.HasExited)
            {
                try
                {
                    await _launcher.StandardInput.WriteLineAsync("stop");
                    await _launcher.StandardInput.FlushAsync();
                }
                catch (Exception error) when (error is IOException or InvalidOperationException)
                {
                    pipeError = error.ToString();
                }
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await _launcher.WaitForExitAsync(deadline.Token);
                }
                catch (OperationCanceledException)
                {
                    forced = true;
                    try
                    {
                        _launcher.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException) when (_launcher.HasExited)
                    {
                        // The launcher can exit between the deadline and the owned-tree termination.
                    }
                    using var killDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await _launcher.WaitForExitAsync(killDeadline.Token);
                }
            }
        }
        finally
        {
            Directory.CreateDirectory(DirectoryPath);
            bool gameClosed = await PortClosedAsync(Port);
            bool rconClosed = await PortClosedAsync(RconPort);
            int? exitCode = _launcher.HasExited ? _launcher.ExitCode : null;
            await WriteAsync("cleanup.json", new { forced, pipeError, launcherExitCode = exitCode, gameClosed, rconClosed });
            try
            {
                await File.WriteAllTextAsync(Path.Combine(DirectoryPath, "launcher.stdout.log"), await _stdout.WaitAsync(TimeSpan.FromSeconds(5)));
                await File.WriteAllTextAsync(Path.Combine(DirectoryPath, "launcher.stderr.log"), await _stderr.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                _launcher.Dispose();
            }
            Assert.False(forced, "The owned server required forced cleanup.");
            Assert.Equal(0, exitCode);
            Assert.True(gameClosed && rconClosed, "An owned vanilla listener remained open.");
            using JsonDocument stopped = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(DirectoryPath, "stopped.json")));
            Assert.Equal(0, stopped.RootElement.GetProperty("exit_code").GetInt32());
        }
    }

    private static async Task<bool> PortClosedAsync(int port)
    {
        using var socket = new TcpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await socket.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            return false;
        }
        catch (SocketException error)
        {
            return error.SocketErrorCode == SocketError.ConnectionRefused;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
