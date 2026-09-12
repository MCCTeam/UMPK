// AuthOnline logs in with a real Microsoft account through the device code flow. It prints the profile and stops. It never connects to a server. Pair it with samples/ConnectedBot, which runs this same flow and then joins.
//
// Run it like this:
//
//   dotnet run --project samples/AuthOnline -- player@example.com
//   dotnet run --project samples/AuthOnline -- player@example.com --fresh
//
// The first run shows a code. You type that code into a browser on any device and approve. Later runs reuse the cached session with no browser step, until --fresh forces a new login. For no account at all, see samples/AuthOffline. For a third party auth server, see samples/AuthThirdParty.
using Umpk.Auth;

bool fresh = args.Contains("--fresh");
string[] rest = args.Where(a => a != "--fresh").ToArray();

if (rest.Length != 1 || rest[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: AuthOnline <loginHint> [--fresh]");
    Console.Error.WriteLine("  loginHint is usually your email. --fresh skips the cache and logs in again.");
    return 2;
}

string loginHint = rest[0];
if (string.IsNullOrWhiteSpace(loginHint))
{
    Console.Error.WriteLine("The login hint must not be empty.");
    return 2;
}

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // The device code poll waits between attempts, so cancel cuts that wait short.
    eventArgs.Cancel = true;
    stopping.Cancel();
};

try
{
    return await RunAsync(loginHint, fresh, stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled before login finished.");
    return 0;
}
catch (AuthException ex)
{
    Console.Error.WriteLine($"Authentication failed: {ex.Message}");
    return 1;
}

static async Task<int> RunAsync(string loginHint, bool fresh, CancellationToken ct)
{
    // The cache lives under the user profile, never inside a repo tree. A directory inside the working tree would make *.tok files committable, and those files hold access tokens and refresh tokens.
    string cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".umpk-authonline");
    using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
    {
        FlowKind = AuthFlowKind.MicrosoftDeviceCode,
        TokenStore = new FileTokenStore(cacheDir, TokenProtectors.CreateDefault()),
    });

    if (fresh)
    {
        // Drops the cached entry for this hint so the login below runs fresh. Note this removes only the hint key. A copy cached under the profile name stays until it expires. That is fine here, since a fresh login overwrites the cache on success.
        await flow.InvalidateAsync(loginHint, ct);
    }

    // Resume first so repeat runs skip the browser entirely. A cached session that is still valid returns with no network call. An expired one with a refresh token refreshes behind the scenes. Anything else returns null and the sample falls through to a full login.
    JavaSession? resumed = await flow.TryResumeAsync(loginHint, ct);
    JavaSession session = resumed
        ?? await flow.LoginAsync(new ConsoleInteraction(), ct, loginHint);
    Console.WriteLine(resumed is not null ? "Resumed a cached session." : "Logged in fresh.");

    // Tokens never reach the console. JavaSession.ToString redacts them for the same reason, so print the fields you actually need one by one.
    Console.WriteLine($"Player:   {session.Profile.Name}");
    Console.WriteLine($"UUID:     {session.Profile.Id}");
    Console.WriteLine($"Expires:  {session.ExpiresAt:O}");
    Console.WriteLine($"Kind:     {session.Kind}");
    Console.WriteLine();

    // What to do with this session. Hand the profile plus an authenticator to the client. Keep the session service alive for the whole session, since the client borrows it and never owns it:
    //
    //   using var sessionService = new YggdrasilSessionService(new SessionServiceOptions());
    //   await using UmpkClient client = new UmpkClientBuilder()
    //       .UseVersion(version)
    //       .UseProfile(session.Profile)
    //       .UseAuthenticator(sessionService, new ProfileCredentials(session.Profile, session.AccessToken))
    //       .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
    //       .Build();
    //
    // See docs/guides/authentication.md for the full online client, and samples/ConnectedBot for a program that runs it.
    Console.WriteLine("Session cached. Run again without --fresh to resume it.");
    return 0;
}

// Console rendering of the device code flow. The library never touches the console itself. A GUI host would implement the same three members with windows instead of prints. Only the first member runs in this sample.
sealed class ConsoleInteraction : IAuthInteraction
{
    public Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct)
    {
        // Message is the instruction text from Microsoft. Print it as is, then repeat the two parts that matter in case the text wraps badly.
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
        throw new InvalidOperationException("This sample uses Microsoft login, not Yggdrasil.");
    }
}
