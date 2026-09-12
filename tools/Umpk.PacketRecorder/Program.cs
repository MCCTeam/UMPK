using System.Globalization;
using Microsoft.Extensions.Logging;
using Umpk;
using Umpk.Data.Java;
using Umpk.PacketRecorder;
using Umpk.Protocol.Java;
using Umpk.TestKit.Corpus;

// Umpk.PacketRecorder: records .umpkcap packet corpora from a live offline-mode server by tapping the raw pre-decode frame view of a real client JavaConnection. See LiveCorpusRecorder.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

if (!string.Equals(args[0], "record", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
    PrintUsage();
    return 2;
}

CliOptions? options = CliOptions.Parse(args.AsSpan(1));
if (options is null)
{
    PrintUsage();
    return 2;
}

if (!JavaVersions.TryGetByName(options.Version, out JavaVersion? version))
    if (!int.TryParse(options.Version, NumberStyles.Integer, CultureInfo.InvariantCulture, out int protocol)
        || !JavaVersions.TryGetByProtocol(protocol, out version))
    {
        Console.Error.WriteLine($"Unknown version '{options.Version}'. Known: 1.8 (47), 1.21.5 (770), 26.2 (776).");
        return 2;
    }

ILogger logger = new ConsoleLogger(LogLevel.Information);

var endpoint = new ServerEndpoint(options.Host, options.Port);
var recorder = new LiveCorpusRecorder(logger);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

DateTimeOffset started = DateTimeOffset.UtcNow;
IReadOnlyList<RecordedFrame> frames;
try
{
    frames = await recorder.RecordAsync(
        version!, endpoint, options.Username, TimeSpan.FromSeconds(options.HoldSeconds), cts.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Recording cancelled.");
    return 130;
}
catch (Exception ex)
{
    logger.LogError(ex, "Recording failed.");
    return 1;
}

long durationMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;

// Optional lean filter: keep at most N frames per (direction, phase, wire id) in wire order, so a representative corpus does not carry hundreds of duplicate chunk/keep-alive frames. Login/config frames (needed to drive the phase machine) are kept regardless.
if (options.MaxPerKey is { } maxPerKey && maxPerKey > 0)
{
    var seen = new Dictionary<(CorpusDirection, CorpusPhase, int), int>();
    var kept = new List<RecordedFrame>(frames.Count);
    foreach (RecordedFrame f in frames)
    {
        if (f.Phase is CorpusPhase.Login or CorpusPhase.Configuration)
        {
            kept.Add(f);
            continue;
        }

        var key = (f.Direction, f.Phase, f.WireId);
        int count = seen.TryGetValue(key, out int c) ? c : 0;
        if (count < maxPerKey)
        {
            kept.Add(f);
            seen[key] = count + 1;
        }
    }

    logger.LogInformation("Lean filter: {Kept}/{Total} frames kept (max {Max} per phase/flow/wire id).",
        kept.Count, frames.Count, maxPerKey);
    frames = kept;
}

await UmpkCapWriter.WriteFilesAsync(
    options.Output,
    frames,
    version!.Version.Name,
    version.Version.Protocol,
    options.Scenario,
    started,
    durationMs,
    options.Notes,
    cts.Token);

logger.LogInformation("Wrote {Count} frames to {Path} (+ manifest).", frames.Count, options.Output);
return 0;

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Umpk.PacketRecorder - record .umpkcap packet corpora from a live offline server.

        Usage:
          record --version <name|protocol> --host <h> [--port <p>] --scenario <name>
                 --out <path.umpkcap> [--hold <seconds>] [--user <name>] [--notes <text>]

        Example:
          record --version 1.21.5 --host 127.0.0.1 --port 25599 --scenario chunk-join
                 --out fixtures/corpus/770/chunk-join.umpkcap --hold 20
        """);
}

internal sealed record CliOptions(
    string Version,
    string Host,
    ushort Port,
    string Scenario,
    string Output,
    double HoldSeconds,
    string Username,
    string? Notes,
    int? MaxPerKey)
{
    public static CliOptions? Parse(ReadOnlySpan<string> args)
    {
        string? version = null, host = null, scenario = null, output = null, notes = null;
        string username = "UmpkRecorder";
        ushort port = 25565;
        double hold = 20;
        int? maxPerKey = null;

        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--version": version = ArgValue(args, ref i); break;
                case "--host": host = ArgValue(args, ref i); break;
                case "--port":
                    if (!ushort.TryParse(ArgValue(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                        return null;

                    break;
                case "--scenario": scenario = ArgValue(args, ref i); break;
                case "--out": output = ArgValue(args, ref i); break;
                case "--hold":
                    if (!double.TryParse(ArgValue(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out hold))
                        return null;

                    break;
                case "--user": username = ArgValue(args, ref i) ?? username; break;
                case "--notes": notes = ArgValue(args, ref i); break;
                case "--max-per-key":
                    if (!int.TryParse(ArgValue(args, ref i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mpk))
                        return null;

                    maxPerKey = mpk;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
                    return null;
            }

        if (version is null || host is null || scenario is null || output is null)
        {
            Console.Error.WriteLine("--version, --host, --scenario, and --out are required.");
            return null;
        }

        return new CliOptions(version, host, port, scenario, output, hold, username, notes, maxPerKey);
    }

    private static string? ArgValue(ReadOnlySpan<string> args, ref int i) => i + 1 < args.Length ? args[++i] : null;
}
