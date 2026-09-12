// AuthThirdParty logs in against a third party auth server (authlib-injector style) with a username and password. It prints the profile and stops. It never connects to a game server.
//
// Run it like this:
//
//   dotnet run --project samples/AuthThirdParty -- https://example.com/api/yggdrasil/
//
// The trailing slash matters. The request goes to authserver/authenticate resolved against that base. For Mojang accounts use samples/AuthOnline instead. For no account at all, see samples/AuthOffline.
using Umpk.Auth;

bool fresh = args.Contains("--fresh");
string[] rest = args.Where(a => a != "--fresh").ToArray();

if (rest.Length != 1 || rest[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: AuthThirdParty <baseUrl> [--fresh]");
    Console.Error.WriteLine("  baseUrl is the provider root with a trailing slash. --fresh skips the cache.");
    return 2;
}

if (!Uri.TryCreate(rest[0], UriKind.Absolute, out Uri? baseUrl))
{
    Console.Error.WriteLine($"'{rest[0]}' is not an absolute URL.");
    return 2;
}

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.Cancel();
};

try
{
    return await RunAsync(baseUrl, fresh, stopping.Token);
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

static async Task<int> RunAsync(Uri baseUrl, bool fresh, CancellationToken ct)
{
    // Ask for the username first, since the cache key needs it. The password prompt comes later, and only when no usable session is cached.
    Console.Write("Username: ");
    string? username = await Task.Run(Console.ReadLine, CancellationToken.None);
    if (string.IsNullOrWhiteSpace(username))
    {
        Console.Error.WriteLine("The username must not be empty.");
        return 2;
    }

    username = username.Trim();

    // The cache lives under the user profile, never inside a repo tree. A directory inside the working tree would make *.tok files committable, and those files hold access tokens.
    string cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".umpk-auththirdparty");
    using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
    {
        FlowKind = AuthFlowKind.Yggdrasil,
        YggdrasilBaseUrl = baseUrl,
        TokenStore = new FileTokenStore(cacheDir, TokenProtectors.CreateDefault()),
    });

    if (fresh)
        await flow.InvalidateAsync(username, ct);

    // A cached session that is still valid returns with no network call. Only authenticate is implemented on this flow, so there is no refresh. The expiry is stamped as now plus 24 hours, and an expired entry reads as a miss, which means the password comes out roughly once a day.
    JavaSession? resumed = await flow.TryResumeAsync(username, ct);
    JavaSession session = resumed
        ?? await flow.LoginAsync(new PasswordInteraction(username), ct, username);
    Console.WriteLine(resumed is not null ? "Resumed a cached session." : "Logged in fresh.");

    // Tokens never reach the console. Print the identity and the expiry only.
    Console.WriteLine($"Player:   {session.Profile.Name}");
    Console.WriteLine($"UUID:     {session.Profile.Id}");
    Console.WriteLine($"Expires:  {session.ExpiresAt:O} (stamped, roughly 24 hours out)");
    Console.WriteLine($"Kind:     {session.Kind}");
    Console.WriteLine();

    // What to do with this session. Hand the profile plus a session service to the client. Point the service at the provider session host, not Mojang:
    //
    //   using var sessionService = new YggdrasilSessionService(
    //       new SessionServiceOptions { BaseUrl = new Uri("https://example.com/api/session/") });
    //   await using UmpkClient client = new UmpkClientBuilder()
    //       .UseVersion(version)
    //       .UseProfile(session.Profile)
    //       .UseAuthenticator(sessionService, new ProfileCredentials(session.Profile, session.AccessToken))
    //       .UseStaticRegistries(JavaGameData.Registries(version.Version.Protocol))
    //       .Build();
    //
    // See docs/guides/authentication.md for the Yggdrasil limits, and samples/ConnectedBot for a program that joins after login.
    Console.WriteLine("Session cached. Run again to resume it without the password.");
    return 0;
}

// Prompts for the password only. The username arrives through the constructor, because the sample reads it before the login call so resume can use it too. The other two members never run on this flow.
sealed class PasswordInteraction(string username) : IAuthInteraction
{
    public Task ShowDeviceCodeAsync(DeviceCodePrompt prompt, CancellationToken ct)
    {
        throw new InvalidOperationException("This sample uses Yggdrasil login, not the device code flow.");
    }

    public Task<string> GetBrowserAuthCodeAsync(Uri signInUrl, CancellationToken ct)
    {
        throw new InvalidOperationException("This sample uses Yggdrasil login, not the browser flow.");
    }

    public Task<YggdrasilCredentials> GetYggdrasilCredentialsAsync(CancellationToken ct)
    {
        string password = ReadPassword();
        if (password.Length == 0)
            throw new AuthException("The password must not be empty.");

        return Task.FromResult(new YggdrasilCredentials(username, password));
    }

    // Reads a line without echoing it when the input is interactive. Masking with stars keeps shoulders away from the secret. Piped input falls back to a plain read, since there is no console to mask on.
    private static string ReadPassword()
    {
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? string.Empty;

        Console.Write("Password: ");
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.Enter)
            {
                Console.WriteLine();
                return builder.ToString();
            }

            if (key.Key is ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length -= 1;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (key.KeyChar == 0)
                continue;

            builder.Append(key.KeyChar);
            Console.Write('*');
        }
    }
}
