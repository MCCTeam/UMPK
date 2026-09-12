// StatusPing shows the smallest useful thing UMPK can do. It asks a server for its list-ping row and prints it, the same row a launcher draws in the multiplayer menu. No account, no version choice, no session. The status exchange is version blind, so this program never picks a protocol.
//
// Run it like this:
//
//   dotnet run --project samples/StatusPing -- mc.example.com
//   dotnet run --project samples/StatusPing -- 127.0.0.1 25566
//
// Against a live server it prints something like:
//
//   Host:     mc.example.com:25565
//   Version:  1.21.8 (protocol 772)
//   Players:  0/20
//   Latency:  6 ms
//   MOTD:
//     A Minecraft Server
//
// If you are new here, read this file top to bottom. It is short on purpose. For the longer explanation see docs/getting-started/status-ping.md.
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Umpk;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;

if (args.Length is 0 or > 2)
{
    Console.Error.WriteLine("Usage: StatusPing <host> [port]");
    return 2;
}

ushort port = ServerEndpoint.DefaultJavaPort;
if (args.Length == 2 && !ushort.TryParse(args[1], CultureInfo.InvariantCulture, out port))
{
    Console.Error.WriteLine($"'{args[1]}' is not a port number.");
    return 2;
}

var requested = new ServerEndpoint(args[0], port);
var options = new JavaStatusOptions { Timeout = TimeSpan.FromSeconds(10) };

try
{
    // Vanilla looks up the _minecraft._tcp SRV record only when the player typed no port. This follows that rule. DnsSrvResolver returns the input unchanged for IP literals and for hosts with no record, so there is no extra branch to check here. If you never want the lookup, use DnsSrvResolver.Passthrough instead.
    ServerEndpoint endpoint = requested.Port == ServerEndpoint.DefaultJavaPort
        ? await new DnsSrvResolver().ResolveAsync(requested, CancellationToken.None)
        : requested;

    try
    {
        ServerStatus status = await JavaStatus
            .QueryAsync(endpoint, TcpConnectionFactory.Shared, options, CancellationToken.None);
        PrintStatus(endpoint, status);
    }
    catch (Exception ex) when (ex is ProtocolViolationException or ConnectionClosedException)
    {
        // Servers up to 1.6 have no modern status handshake. They answer it with a kick packet or they hang up, which arrives here as one of these two faults. That is the cue to try the old 0xFE 0x01 ping instead.
        Console.Error.WriteLine("No modern status response. Retrying with the legacy 1.6 ping.");
        LegacyServerStatus legacy = await JavaLegacyPing
            .QueryAsync(endpoint, TcpConnectionFactory.Shared, options, CancellationToken.None);
        PrintLegacyStatus(endpoint, legacy);
    }

    return 0;
}
catch (SocketException ex)
{
    Console.Error.WriteLine(DescribeSocketFailure(requested, ex));
    return 1;
}
catch (OperationCanceledException)
{
    // JavaStatus bounds the whole exchange with options.Timeout, so a cancel here means the server did not answer in time.
    Console.Error.WriteLine(
        $"{requested} did not answer within {options.Timeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)} seconds.");
    return 1;
}
catch (ConnectionClosedException ex)
{
    Console.Error.WriteLine($"{requested} closed the connection: {ex.Reason}.");
    return 1;
}
catch (ProtocolViolationException ex)
{
    Console.Error.WriteLine($"{requested} sent something that is not a status response: {ex.Message}");
    return 1;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"{requested} sent a status response that is not valid JSON: {ex.Message}");
    return 1;
}

static void PrintStatus(ServerEndpoint endpoint, ServerStatus status)
{
    // ServerStatus keeps both shapes for you. The decoded fields cover the common case and Json keeps the raw response for everything else, since real servers send more than vanilla documents.
    Console.WriteLine($"Host:     {endpoint}");
    Console.WriteLine($"Version:  {status.VersionName ?? "unknown"} (protocol {ProtocolText(status.Protocol)})");
    Console.WriteLine($"Players:  {PlayersText(status)}");
    Console.WriteLine($"Latency:  {FormatLatency(status.Latency)}");

    // A string shaped description already had its section sign codes expanded into styles
    // while parsing, so ToPlainText comes back clean. Nothing left to strip on this path.
    PrintMotd(status.Description?.ToPlainText() ?? string.Empty);
}

static void PrintLegacyStatus(ServerEndpoint endpoint, LegacyServerStatus status)
{
    Console.WriteLine($"Host:     {endpoint}");

    // The legacy reply carries a protocol number on the wire, but LegacyServerStatus does not expose it, so this prints what is known instead of guessing.
    Console.WriteLine($"Version:  {status.MinecraftVersion ?? "unknown"} (legacy ping, no protocol number)");
    Console.WriteLine($"Players:  {status.OnlinePlayers}/{status.MaxPlayers}");
    Console.WriteLine($"Latency:  {FormatLatency(status.Latency)}");

    // The legacy MOTD is a plain string with section sign codes still in it. It is never a component, so this is the one place that strips them by hand.
    PrintMotd(StripFormattingCodes(status.Motd));
}

static void PrintMotd(string motd)
{
    if (motd.Length == 0)
        return;

    Console.WriteLine("MOTD:");
    foreach (string line in motd.Split('\n'))
        Console.WriteLine($"  {line.TrimEnd('\r')}");

}

// Drops section sign color codes. They are formatting instructions, not text.
static string StripFormattingCodes(string text)
{
    if (!text.Contains('§', StringComparison.Ordinal))
        return text;

    var builder = new StringBuilder(text.Length);
    for (int i = 0; i < text.Length; i++)
    {
        if (text[i] == '§')
        {
            i++;
            continue;
        }

        builder.Append(text[i]);
    }

    return builder.ToString();
}

static string ProtocolText(int? protocol) =>
    protocol?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

static string PlayersText(ServerStatus status) =>
    status.OnlinePlayers is int online && status.MaxPlayers is int max ? $"{online}/{max}" : "unknown";

static string FormatLatency(TimeSpan latency) =>
    $"{latency.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)} ms";

static string DescribeSocketFailure(ServerEndpoint endpoint, SocketException ex) => ex.SocketErrorCode switch
{
    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
        $"No such host: {endpoint.Host}.",
    SocketError.ConnectionRefused =>
        $"{endpoint} refused the connection. Is a server listening on that port?",
    SocketError.TimedOut =>
        $"{endpoint} accepted no connection before the socket timed out.",
    SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
        $"{endpoint} is unreachable from this machine.",
    _ => $"Could not reach {endpoint}: {ex.Message}",
};
