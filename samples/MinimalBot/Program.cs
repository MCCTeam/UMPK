// MinimalBot is the shortest honest UMPK session. It connects in offline mode, prints the chat it hears, says one line, and logs out when you press Ctrl+C.
//
// Run it against a server you control, since it sends chat on join:
//
//   dotnet run --project samples/MinimalBot -- localhost Steve
//   dotnet run --project samples/MinimalBot -- mc.example.com:25566 Steve
//
// For the longer explanation see docs/getting-started/minimal-bot.md. For an offline program that needs no server, see samples/ChatRender, samples/NbtLab, samples/PhysicsWalk, and samples/VersionTable.
using System.Net.Sockets;
using System.Text.Json;
using Umpk;
using Umpk.Auth;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Keep the Ctrl+C for ourselves so the session can close cleanly. Without this the process dies with a half open socket.
    eventArgs.Cancel = true;
    stopping.Cancel();
};

// The AOT smoke reuses this program through the same public calls. Keeping it here means CI publishes exactly what a reader runs.
if (args.Length > 0 && args[0] == "--aot-smoke")
    return await AotSmoke.RunAsync(args[1..], stopping.Token);

if (args.Length != 2 || !ServerEndpoint.TryParse(args[0], out ServerEndpoint requested))
{
    Console.Error.WriteLine("Usage: MinimalBot <host[:port]> <username>");
    return 2;
}

try
{
    return await RunAsync(requested, args[1], stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"{requested} did not finish the handshake in time.");
    return 1;
}
catch (SocketException ex)
{
    Console.Error.WriteLine($"Could not reach {requested}: {ex.SocketErrorCode}.");
    return 1;
}
catch (ConnectionClosedException ex)
{
    Console.Error.WriteLine($"{requested} closed the connection: {ex.Reason}.");
    return 1;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"{requested} sent a status response that is not valid JSON: {ex.Message}");
    return 1;
}

static async Task<int> RunAsync(ServerEndpoint requested, string username, CancellationToken ct)
{
    // Vanilla does the SRV lookup only when the player typed no port. Match that.
    ServerEndpoint endpoint = requested.Port == ServerEndpoint.DefaultJavaPort
        ? await new DnsSrvResolver().ResolveAsync(requested, ct)
        : requested;

    // A client is built for one protocol, so ask the server which one it speaks first. This is the same ping StatusPing performs. Only version.protocol matters here.
    ServerStatus status = await JavaStatus
        .QueryAsync(endpoint, TcpConnectionFactory.Shared, new JavaStatusOptions(), ct);
    if (status.Protocol is not int protocol)
    {
        Console.Error.WriteLine($"{endpoint} answered the ping without a version.protocol field.");
        return 1;
    }

    if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion version))
    {
        Console.Error.WriteLine($"{endpoint} speaks protocol {protocol}, which UMPK has no data for.");
        return 1;
    }

    await using UmpkClient client = new UmpkClientBuilder()
        .UseVersion(version)
        // Offline mode means no authenticator. The UUID matches what a vanilla offline server derives from the name, so the bot keeps the same identity on reconnect.
        .UseProfile(OfflineIdentity.ComputeProfile(username))
        // Item stacks and entity types decode against the version registries. Skip this and the first packet with a real item fails to decode and ends the session.
        .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
        .Build();

    // Handlers run on the session loop, so keep them short. Hand the news to the main flow and return, so packet processing never stalls behind your code.
    var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    using IDisposable chatSubscription = client.Events.Subscribe<ChatMessageReceived>(
        chat => Console.WriteLine(chat.Message.ToPlainText()));

    using IDisposable disconnectSubscription = client.Events.Subscribe<Disconnected>(gone =>
    {
        Console.WriteLine($"Session ended: {gone.Info.Message?.ToPlainText() ?? gone.Info.Reason.ToString()}");
        ended.TrySetResult();
    });

    // Wait for the spawn, not just the connect. Connect alone means play started, not that the server placed the player. A whitelist or ban kick lands in that gap, after login and before spawn, so waiting only on connect would treat a session that is already over as a live one.
    if (!await client.ConnectAndWaitForSpawnAsync(endpoint, ct))
        return 1;

    Console.WriteLine(
        $"Joined {endpoint} as {client.Session!.Profile.Name} on {client.Session.Version.Version.Name}.");

    await client.Actions.Chat.SendChatAsync("Hello from UMPK.", ct);

    // Nothing left to do but listen. Ctrl+C cancels this wait. The server ending the session completes it.
    try
    {
        await ended.Task.WaitAsync(ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
    }

    // Give the logout a fresh token. On the Ctrl+C path ct is already cancelled, and the disconnect packet still has to reach the server.
    await client.DisconnectAsync(CancellationToken.None);
    return 0;
}
