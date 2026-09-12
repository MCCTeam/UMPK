using System.Text.Json;

namespace Umpk.Auth.Internal;

/// <summary>Runs authlib-injector Yggdrasil login through <c>/authserver/authenticate</c> on the asynchronous surface. The base URL belongs to the third-party provider; the request agent is Minecraft version 1. Tokens are never logged, and failures raise typed exceptions.</summary>
internal sealed class YggdrasilAuthenticator
{
    private readonly AuthHttpClient _http;
    private readonly Uri _baseUrl;
    private readonly TimeProvider _time;

    public YggdrasilAuthenticator(AuthHttpClient http, Uri baseUrl, TimeProvider time)
    {
        _http = http;
        _baseUrl = baseUrl;
        _time = time;
    }

    public async Task<JavaSession> AuthenticateAsync(YggdrasilCredentials credentials, string clientToken, CancellationToken ct)
    {
        string payload =
            "{\"agent\":{\"name\":\"Minecraft\",\"version\":1},\"username\":\""
            + JsonEncode(credentials.Username)
            + "\",\"password\":\""
            + JsonEncode(credentials.Password)
            + "\",\"clientToken\":\""
            + JsonEncode(clientToken)
            + "\",\"requestUser\":false}";

        var url = new Uri(_baseUrl, "authserver/authenticate");
        AuthHttpResponse response = await _http.PostJsonAsync(url, payload, null, null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            throw new AuthServiceException("yggdrasil_authenticate", response.StatusCode, "Yggdrasil authentication failed.");

        using JsonDocument doc = response.ParseJson();
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("selectedProfile", out JsonElement selected))
            throw new AuthServiceException("yggdrasil_authenticate", response.StatusCode, "Yggdrasil response had no selected profile (account may have multiple profiles).");

        string accessToken = root.GetProperty("accessToken").GetString()!;
        string id = selected.GetProperty("id").GetString()!;
        string name = selected.GetProperty("name").GetString()!;

        // Yggdrasil access tokens do not carry an explicit lifetime; treat as long-lived until re-validated.
        DateTimeOffset expiresAt = _time.GetUtcNow() + TimeSpan.FromHours(24);
        var profile = new GameProfile(ParseUndashedGuid(id), name);
        return new JavaSession(profile, accessToken, expiresAt, null, AuthKind.Yggdrasil);
    }

    private static Guid ParseUndashedGuid(string id) =>
        id.Contains('-', StringComparison.Ordinal) ? Guid.Parse(id) : Guid.ParseExact(id, "N");

    private static string JsonEncode(string value) => JsonEncodedText.Encode(value).ToString();
}
