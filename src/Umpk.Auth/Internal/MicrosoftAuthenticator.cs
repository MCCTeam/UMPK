using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Umpk.Auth.Internal;

/// <summary>Runs the Microsoft chain from MSA token through XBL, XSTS, Minecraft login, entitlement, and profile on an asynchronous, cancellable, token-redacting surface. Every call goes through <see cref="AuthHttpClient"/>. The chain never logs tokens; failures raise typed exceptions that carry error codes only.</summary>
internal sealed class MicrosoftAuthenticator
{
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string DeviceCodeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    private const string AuthorizeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    private const string XblUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string LoginWithXboxUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string EntitlementUrl = "https://api.minecraftservices.com/entitlements/mcstore";
    private const string ProfileUrl = "https://api.minecraftservices.com/minecraft/profile";
    private const string Scope = "XboxLive.signin offline_access openid email";
    private const string XblUserAgent =
        "Mozilla/5.0 (XboxReplay; XboxLiveAuth/3.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/71.0.3578.98 Safari/537.36";

    private readonly AuthHttpClient _http;
    private readonly string _clientId;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly string _userAgent;

    public MicrosoftAuthenticator(AuthHttpClient http, string clientId, TimeProvider time, ILogger logger)
    {
        _http = http;
        _clientId = clientId;
        _time = time;
        _logger = logger;
        _userAgent = "UMPK.Auth";
    }

    /// <summary>Builds the interactive authorize URL for the browser flow.</summary>
    /// <param name="redirectUri">The redirect URI. It MUST be one registered against <see cref="_clientId"/>; Microsoft rejects the request outright otherwise.</param>
    /// <param name="state">The CSRF state echoed back on the redirect.</param>
    /// <param name="mode">How the code comes back. <see cref="BrowserRedirectMode.HostedPastePage"/> adds <c>response_mode=fragment</c> so the code stays in the browser and is never sent to the hosting server; the loopback listener uses the default query response mode so it can read the code from the request it receives.</param>
    public Uri BuildAuthorizeUrl(Uri redirectUri, string state, BrowserRedirectMode mode)
    {
        string url = AuthorizeUrl
            + "?client_id=" + Uri.EscapeDataString(_clientId)
            + "&response_type=code"
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri.AbsoluteUri)
            + "&scope=" + Uri.EscapeDataString(Scope)
            + "&prompt=select_account"
            + "&state=" + Uri.EscapeDataString(state);
        if (mode == BrowserRedirectMode.HostedPastePage)
            url += "&response_mode=fragment";

        return new Uri(url);
    }

    public async Task<DeviceCodeResult> RequestDeviceCodeAsync(CancellationToken ct)
    {
        string form = "client_id=" + Uri.EscapeDataString(_clientId) + "&scope=" + Uri.EscapeDataString(Scope);
        AuthHttpResponse response = await _http.PostFormAsync(new Uri(DeviceCodeUrl), form, _userAgent, ct).ConfigureAwait(false);
        using JsonDocument doc = response.ParseJson();
        JsonElement root = doc.RootElement;
        ThrowIfError(root, "devicecode", response.StatusCode);

        return new DeviceCodeResult(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            new Uri(root.GetProperty("verification_uri").GetString()!),
            root.GetProperty("message").GetString()!,
            GetInt(root, "expires_in"),
            GetInt(root, "interval"));
    }

    /// <summary>Polls the token endpoint until the user authorizes, then resolves the full session.</summary>
    public async Task<MsaToken> PollDeviceCodeAsync(DeviceCodeResult device, CancellationToken ct)
    {
        const int SlowDownIncrementSeconds = 5;
        string form =
            "client_id=" + Uri.EscapeDataString(_clientId)
            + "&grant_type=urn:ietf:params:oauth:grant-type:device_code"
            + "&device_code=" + Uri.EscapeDataString(device.DeviceCode);

        DateTimeOffset deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(device.ExpiresInSeconds);
        int intervalSeconds = device.IntervalSeconds;

        while (_time.GetUtcNow() < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), _time, ct).ConfigureAwait(false);

            AuthHttpResponse response = await _http.PostFormAsync(new Uri(TokenUrl), form, _userAgent, ct).ConfigureAwait(false);
            using JsonDocument doc = response.ParseJson();
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement error))
            {
                string code = error.GetString() ?? "unknown";
                switch (code)
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        intervalSeconds += SlowDownIncrementSeconds;
                        continue;
                    case "expired_token":
                        throw new DeviceCodeAuthorizationException(DeviceCodeFailure.Expired, "The device code expired before authorization.");
                    case "authorization_declined":
                        throw new DeviceCodeAuthorizationException(DeviceCodeFailure.Declined, "The user declined the authorization request.");
                    default:
                        throw new AuthServiceException("devicecode_poll", response.StatusCode, "Device-code token request failed with error code '" + code + "'.");
                }
            }

            return ReadMsaToken(root);
        }

        throw new DeviceCodeAuthorizationException(DeviceCodeFailure.TimedOut, "Device-code authorization timed out.");
    }

    public async Task<MsaToken> ExchangeAuthCodeAsync(string authCode, Uri redirectUri, CancellationToken ct)
    {
        string form =
            "client_id=" + Uri.EscapeDataString(_clientId)
            + "&grant_type=authorization_code"
            + "&redirect_uri=" + Uri.EscapeDataString(redirectUri.AbsoluteUri)
            + "&code=" + Uri.EscapeDataString(authCode);
        return await RequestTokenAsync(form, "authcode", ct).ConfigureAwait(false);
    }

    public async Task<MsaToken> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        string form =
            "client_id=" + Uri.EscapeDataString(_clientId)
            + "&grant_type=refresh_token"
            + "&refresh_token=" + Uri.EscapeDataString(refreshToken);
        return await RequestTokenAsync(form, "refresh", ct).ConfigureAwait(false);
    }

    private async Task<MsaToken> RequestTokenAsync(string form, string stage, CancellationToken ct)
    {
        AuthHttpResponse response = await _http.PostFormAsync(new Uri(TokenUrl), form, _userAgent, ct).ConfigureAwait(false);
        using JsonDocument doc = response.ParseJson();
        JsonElement root = doc.RootElement;
        ThrowIfError(root, stage, response.StatusCode);
        return ReadMsaToken(root);
    }

    /// <summary>Runs the full XBL/XSTS/services chain from an MSA access token to a resolved session.</summary>
    public async Task<JavaSession> CompleteChainAsync(MsaToken msa, CancellationToken ct)
    {
        XboxToken xbl = await XblAuthenticateAsync(msa.AccessToken, ct).ConfigureAwait(false);
        XboxToken xsts = await XstsAuthenticateAsync(xbl.Token, ct).ConfigureAwait(false);
        string mcToken = await LoginWithXboxAsync(xsts.UserHash, xsts.Token, ct).ConfigureAwait(false);

        if (!await HasEntitlementAsync(mcToken, ct).ConfigureAwait(false))
            throw new NoMinecraftEntitlementException("The signed-in account does not own Minecraft.");

        GameProfile profile = await GetProfileAsync(mcToken, ct).ConfigureAwait(false);
        DateTimeOffset expiresAt = _time.GetUtcNow() + TimeSpan.FromSeconds(msa.ExpiresInSeconds);
        return new JavaSession(profile, mcToken, expiresAt, msa.RefreshToken, AuthKind.Microsoft);
    }

    private async Task<XboxToken> XblAuthenticateAsync(string msaAccessToken, CancellationToken ct)
    {
        // OAuth tokens from our own client id require the "d=" RpsTicket prefix.
        string rps = "d=" + msaAccessToken;
        string payload =
            "{\"Properties\":{\"AuthMethod\":\"RPS\",\"SiteName\":\"user.auth.xboxlive.com\",\"RpsTicket\":\""
            + JsonEncode(rps)
            + "\"},\"RelyingParty\":\"http://auth.xboxlive.com\",\"TokenType\":\"JWT\"}";

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-xbl-contract-version"] = "0",
            ["User-Agent"] = XblUserAgent,
        };
        AuthHttpResponse response = await _http.PostJsonAsync(new Uri(XblUrl), payload, null, headers, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            throw new AuthServiceException("xbl", response.StatusCode, "Xbox Live authentication failed.");

        using JsonDocument doc = response.ParseJson();
        return ReadXboxToken(doc.RootElement);
    }

    private async Task<XboxToken> XstsAuthenticateAsync(string xblToken, CancellationToken ct)
    {
        string payload =
            "{\"Properties\":{\"SandboxId\":\"RETAIL\",\"UserTokens\":[\""
            + JsonEncode(xblToken)
            + "\"]},\"RelyingParty\":\"rp://api.minecraftservices.com/\",\"TokenType\":\"JWT\"}";

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-xbl-contract-version"] = "1",
            ["User-Agent"] = XblUserAgent,
        };
        AuthHttpResponse response = await _http.PostJsonAsync(new Uri(XstsUrl), payload, null, headers, ct).ConfigureAwait(false);
        if (response.IsSuccess)
        {
            using JsonDocument doc = response.ParseJson();
            return ReadXboxToken(doc.RootElement);
        }

        if (response.StatusCode == 401)
        {
            using JsonDocument doc = response.ParseJson();
            if (doc.RootElement.TryGetProperty("XErr", out JsonElement xErrElement))
            {
                long xErr = ReadXErr(xErrElement);
                (XstsErrorReason reason, string message) = ClassifyXErr(xErr);
                throw new XstsAuthorizationException(xErr, reason, message);
            }
        }

        throw new AuthServiceException("xsts", response.StatusCode, "XSTS authorization failed.");
    }

    private async Task<string> LoginWithXboxAsync(string userHash, string xstsToken, CancellationToken ct)
    {
        string payload = "{\"identityToken\":\"XBL3.0 x=" + JsonEncode(userHash) + ";" + JsonEncode(xstsToken) + "\"}";
        AuthHttpResponse response = await _http.PostJsonAsync(new Uri(LoginWithXboxUrl), payload, null, null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            throw new AuthServiceException("login_with_xbox", response.StatusCode, "Minecraft services login failed.");

        using JsonDocument doc = response.ParseJson();
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<bool> HasEntitlementAsync(string mcToken, CancellationToken ct)
    {
        AuthHttpResponse response = await _http.GetAsync(new Uri(EntitlementUrl), mcToken, null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            throw new AuthServiceException("entitlement", response.StatusCode, "Entitlement check failed.");

        using JsonDocument doc = response.ParseJson();
        return doc.RootElement.TryGetProperty("items", out JsonElement items)
            && items.ValueKind == JsonValueKind.Array
            && items.GetArrayLength() > 0;
    }

    private async Task<GameProfile> GetProfileAsync(string mcToken, CancellationToken ct)
    {
        AuthHttpResponse response = await _http.GetAsync(new Uri(ProfileUrl), mcToken, null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            throw new AuthServiceException("profile", response.StatusCode, "Profile fetch failed.");

        using JsonDocument doc = response.ParseJson();
        JsonElement root = doc.RootElement;
        string id = root.GetProperty("id").GetString()!;
        string name = root.GetProperty("name").GetString()!;
        return new GameProfile(ParseUndashedGuid(id), name);
    }

    private static (XstsErrorReason Reason, string Message) ClassifyXErr(long xErr) => xErr switch
    {
        2148916227 => (XstsErrorReason.AccountBanned, "The account is banned or has had its Xbox Live privileges revoked."),
        2148916233 => (XstsErrorReason.NoXboxAccount, "The Microsoft account has no linked Xbox account."),
        2148916235 => (XstsErrorReason.RegionUnavailable, "Xbox Live is not available in the account's country or region."),
        2148916236 or 2148916237 => (XstsErrorReason.AdultVerificationRequired, "The account requires adult verification."),
        2148916238 => (XstsErrorReason.ChildAccount, "The account belongs to a minor and must be added to a Microsoft Family."),
        _ => (XstsErrorReason.Unknown, "XSTS authorization failed with code " + xErr.ToString(CultureInfo.InvariantCulture) + "."),
    };

    private MsaToken ReadMsaToken(JsonElement root)
    {
        string access = root.GetProperty("access_token").GetString()!;
        string? refresh = root.TryGetProperty("refresh_token", out JsonElement r) ? r.GetString() : null;
        int expires = GetInt(root, "expires_in");
        _logger.LogDebug("Acquired Microsoft access token (expires in {ExpiresIn}s).", expires);
        return new MsaToken(access, refresh, expires);
    }

    private static XboxToken ReadXboxToken(JsonElement root)
    {
        string token = root.GetProperty("Token").GetString()!;
        string userHash = root.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;
        return new XboxToken(token, userHash);
    }

    private static void ThrowIfError(JsonElement root, string stage, int statusCode)
    {
        if (root.TryGetProperty("error", out JsonElement error))
        {
            string code = error.GetString() ?? "unknown";
            throw new AuthServiceException(stage, statusCode, "Auth token request failed with error code '" + code + "'.");
        }
    }

    private static int GetInt(JsonElement root, string name)
    {
        JsonElement element = root.GetProperty(name);
        return element.ValueKind == JsonValueKind.Number
            ? element.GetInt32()
            : int.Parse(element.GetString()!, CultureInfo.InvariantCulture);
    }

    private static long ReadXErr(JsonElement element) => element.ValueKind == JsonValueKind.Number
        ? element.GetInt64()
        : long.Parse(element.GetString()!, CultureInfo.InvariantCulture);

    private static Guid ParseUndashedGuid(string id) =>
        id.Contains('-', StringComparison.Ordinal) ? Guid.Parse(id) : Guid.ParseExact(id, "N");

    private static string JsonEncode(string value) => JsonEncodedText.Encode(value).ToString();
}

internal readonly record struct DeviceCodeResult(
    string DeviceCode, string UserCode, Uri VerificationUri, string Message, int ExpiresInSeconds, int IntervalSeconds);

internal readonly record struct MsaToken(string AccessToken, string? RefreshToken, int ExpiresInSeconds);

internal readonly record struct XboxToken(string Token, string UserHash);
