// ConnectedBot ties four layers together in one program: status ping, auth, connect, and an interactive chat loop.
//
// Run it against a server you control:
//
//   dotnet run --project samples/ConnectedBot -- localhost Steve
//   dotnet run --project samples/ConnectedBot -- mc.example.com:25566 Steve
//   dotnet run --project samples/ConnectedBot -- localhost player@example.com --online
//
// Without --online it joins in offline mode, like MinimalBot. With --online it runs the Microsoft device code flow, caches the session outside the repo, and signs chat on 1.19 and later. Type a line and press Enter to send it. A leading slash runs a command instead of sending chat. Type /quit to leave.
//
// For the longer walkthrough see docs/getting-started/connected-bot.md. For each layer on its own, see samples/StatusPing, samples/AuthOffline, and samples/ChatRender. Those run with no server.
using System.Net.Sockets;
using System.Text.Json;
using Umpk;
using Umpk.Auth;
using Umpk.Auth.Session;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Data.Lang;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Signing;
using Umpk.Protocol.Java.Transport;
using Umpk.Text;

bool online = args.Contains("--online");
string[] rest = args.Where(a => a != "--online").ToArray();

if (rest.Length is 0 or > 2 || rest.Any(a => a is "-h" or "--help"))
{
    Console.Error.WriteLine("Usage: ConnectedBot <host[:port]> <username> [--online]");
    Console.Error.WriteLine("  username is the offline name, or the login hint (email) with --online.");
    return 2;
}

if (!ServerEndpoint.TryParse(rest[0], out ServerEndpoint requested))
{
    Console.Error.WriteLine($"'{rest[0]}' is not a host[:port] address.");
    return 2;
}

string who = rest[1];
if (!online && who.Length is 0 or > 16)
{
    Console.Error.WriteLine("An offline username has 1 to 16 characters.");
    return 2;
}

if (online && string.IsNullOrWhiteSpace(who))
{
    Console.Error.WriteLine("With --online, pass your login hint (usually your email) as the second argument.");
    return 2;
}

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // Keep Ctrl+C for ourselves so the session can close cleanly. The chat read below blocks, so after Ctrl+C press Enter once to let the loop notice the cancel and run the logout.
    eventArgs.Cancel = true;
    stopping.Cancel();
};

try
{
    return await RunAsync(requested, who, online, stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"{requested} did not finish in time.");
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
catch (AuthException ex)
{
    Console.Error.WriteLine($"Authentication failed: {ex.Message}");
    return 1;
}

static async Task<int> RunAsync(
    ServerEndpoint requested, string who, bool online, CancellationToken ct)
{
    // Step 1, ping. Vanilla resolves the SRV record only when the player typed no port, so match that. The ping answers two questions at once: what to print, and which protocol to build the client for.
    ServerEndpoint endpoint = requested.Port == ServerEndpoint.DefaultJavaPort
        ? await new DnsSrvResolver().ResolveAsync(requested, ct)
        : requested;

    ServerStatus status = await JavaStatus.QueryAsync(
        endpoint, TcpConnectionFactory.Shared, new JavaStatusOptions(), ct);
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

    Console.WriteLine($"Host:     {endpoint}");
    Console.WriteLine($"Version:  {status.VersionName ?? "unknown"} (protocol {protocol})");
    Console.WriteLine($"Players:  {(status.OnlinePlayers is int on && status.MaxPlayers is int max ? $"{on}/{max}" : "unknown")}");
    Console.WriteLine($"MOTD:     {status.Description?.ToPlainText() ?? string.Empty}");
    Console.WriteLine();

    // Translations resolve join lines and server messages against the same protocol the ping reported. Without a table you get raw keys in the log.
    ITranslationSource translations = VanillaTranslations.ForProtocol(protocol);

    // Step 2, auth. Offline mode is the absence of an authenticator: derive the profile and move on. Online mode logs in through the device code flow and keeps the session service and the flow alive for the whole session, because the client borrows both and owns neither.
    GameProfile profile;
    YggdrasilSessionService? sessionService = null;
    MinecraftAuthFlow? flow = null;
    JavaSession? session = null;
    try
    {
        if (!online)
        {
            profile = OfflineIdentity.ComputeProfile(who);
            Console.WriteLine($"Playing offline as {profile.Name} ({profile.Id}).");
        }
        else
        {
            // The cache lives under the user profile, never inside a repo tree. A directory inside the working tree would make *.tok files committable.
            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".umpk-connectedbot");
            flow = new MinecraftAuthFlow(new MinecraftAuthOptions
            {
                FlowKind = AuthFlowKind.MicrosoftDeviceCode,
                TokenStore = new FileTokenStore(cacheDir, TokenProtectors.CreateDefault()),
            });

            session = await flow.TryResumeAsync(who, ct)
                ?? await flow.LoginAsync(new ConsoleInteraction(), ct, who);
            profile = session.Profile;
            Console.WriteLine($"Playing online as {profile.Name} ({profile.Id}).");
            sessionService = new YggdrasilSessionService(new SessionServiceOptions());
        }

        // Step 3, build. Registries let item stacks and entity types decode. Skip them and the first packet with a real item ends the session.
        var builder = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(profile)
            .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol));

        if (sessionService is not null && session is not null)
        {
            builder.UseAuthenticator(
                sessionService,
                new ProfileCredentials(session.Profile, session.AccessToken));

            // Certificates let 1.19 and later servers verify our messages. Without them chat goes out unsigned, which servers with enforce-secure-profile accept and strict ones reject.
            MinecraftAuthFlow certFlow = flow!;
            JavaSession certSession = session;
            builder.UseChatSigning(new FlowSigningProvider(certFlow, certSession));
        }

        await using UmpkClient client = builder.Build();

        // Handlers run on the session loop, so keep them short and return fast. They hand the news to the main flow instead of doing work inline.
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Received chat arrives on one event on every version. Message is the composed line, already decorated with the sender name where the era applies one, so print it as is. The prefix names the category, and player messages carry a short note when the signature did not verify. Overlay lines are the action bar, not the chat log, so they print apart.
        using IDisposable chatSubscription = client.Events.Subscribe<ChatMessageReceived>(
            chat =>
            {
                string line = FormatChat(chat, translations);
                if (line.Length > 0)
                    Console.WriteLine(line);
            });
        using IDisposable disconnectSubscription = client.Events.Subscribe<Disconnected>(gone =>
        {
            string reason = gone.Info.Message?.ToPlainText(translations) ?? gone.Info.Reason.ToString();
            Console.WriteLine($"Session ended: {reason}");
            ended.TrySetResult();
        });

        // Step 4, connect. Wait for the spawn, not just play start. Kicks for a whitelist or a ban land between those two points, so waiting only on connect would treat a session that is already over as a live one.
        if (!await client.ConnectAndWaitForSpawnAsync(endpoint, ct))
            return 1;

        Console.WriteLine($"Joined {endpoint} on {client.Session!.Version.Version.Name}.");

        // Read one value on the loop so the print is consistent with the rest of the state. Reads from our own thread race packet application.
        Vec3d where = await client.InvokeAsync(c => c.State.Self.Position, ct);
        Console.WriteLine($"Spawned at ({where.X:F1}, {where.Y:F1}, {where.Z:F1}).");
        Console.WriteLine("Type chat and press Enter. A leading slash runs a command. /quit leaves.");
        Console.WriteLine();

        // Step 5, chat loop. Console.ReadLine blocks, so a kick while typing surfaces on the next Enter: the loop top sees the ended signal first.
        while (!ended.Task.IsCompleted && !ct.IsCancellationRequested)
        {
            string? line = await Task.Run(Console.ReadLine, CancellationToken.None);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase))
                break;

            if (ended.Task.IsCompleted || ct.IsCancellationRequested)
                break;

            try
            {
                if (line.StartsWith('/'))
                    await client.Actions.Chat.SendCommandAsync(line, CancellationToken.None);
                else
                    await client.Actions.Chat.SendChatAsync(line, CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ConnectionClosedException ex)
            {
                Console.Error.WriteLine($"Send failed, the connection is gone: {ex.Reason}.");
                break;
            }
        }

        // Give the logout a fresh token. On the Ctrl+C path ct is cancelled and the disconnect packet still has to reach the server.
        await client.DisconnectAsync(CancellationToken.None);
        return 0;
    }
    finally
    {
        sessionService?.Dispose();
        flow?.Dispose();
    }
}

// Formats one received chat line for the console. The composed message already carries the sender name on eras that decorate server side, so this adds only the category prefix and, for player chat, the signature standing. Empty lines print as nothing, since blank action bar clears would spam the log.
static string FormatChat(ChatMessageReceived chat, ITranslationSource translations)
{
    string text = chat.Message.ToPlainText(translations);
    if (string.IsNullOrWhiteSpace(text))
        return string.Empty;

    if (chat.IsOverlay)
        return $"[actionbar] {text}";

    string prefix = chat.Category switch
    {
        ChatCategory.Player => "[chat]",
        ChatCategory.System => "[system]",
        ChatCategory.Disguised => "[disguised]",
        _ => "[chat]",
    };

    string standing = (chat.Category, chat.Verification) switch
    {
        (ChatCategory.Player, ChatVerification.Verified) => string.Empty,
        (ChatCategory.Player, ChatVerification.Unverified) => " (?)",
        (ChatCategory.Player, ChatVerification.Failed) => " (rejected)",
        (ChatCategory.Player, ChatVerification.Insecure) => " (unsigned)",
        _ => string.Empty,
    };

    return $"{prefix}{standing} {text}";
}

// Console rendering of the device code flow. The library never touches the console itself. A GUI host would implement the same three members with windows instead of prints. Only the device code member runs in this sample.
sealed class ConsoleInteraction : IAuthInteraction
{
    public Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct)
    {
        // Message is Microsoft ready made instruction text. Print it as is.
        Console.WriteLine(prompt.Message);
        Console.WriteLine($"Code: {prompt.UserCode}");
        Console.WriteLine($"URL:  {prompt.VerificationUri}");
        return Task.CompletedTask;
    }

    public Task<string> GetBrowserAuthCodeAsync(Uri signInUrl, CancellationToken ct)
    {
        throw new InvalidOperationException("This sample uses the device code flow, not the browser flow.");
    }

    public Task<YggdrasilCredentials> GetYggdrasilCredentialsAsync(CancellationToken ct)
    {
        throw new InvalidOperationException("This sample uses offline or Microsoft login, not Yggdrasil.");
    }
}

// Thin wrapper around the auth flow certificate cache. The flow caches by profile name, so no extra caching belongs here. Returning null or throwing both fall back to unsigned chat, which is the honest offline shape.
sealed class FlowSigningProvider(MinecraftAuthFlow flow, JavaSession session) : IChatSigningProvider
{
    public async ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await flow.GetCertificatesAsync(session, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Chat signing unavailable, sending unsigned: {ex.Message}");
            return null;
        }
    }
}
