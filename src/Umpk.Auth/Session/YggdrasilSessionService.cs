using System.Net;
using System.Text.Json;
using Umpk.Auth.Internal;
using Umpk.Protocol.Java;

namespace Umpk.Auth.Session;

/// <summary>The Yggdrasil session service, covering both roles: the client-side join call (<see cref="JoinServerAsync"/>, POST <c>/session/minecraft/join</c>) and the server-side verification call (<see cref="VerifyJoinAsync"/>, GET <c>/session/minecraft/hasJoined</c>). Defaults to Mojang's session server; the base URL is overridable for authlib-injector.</summary>
/// <remarks>Implements the two role-symmetric session seams declared in <c>Umpk.Protocol.Java</c>: <see cref="ISessionAuthenticator"/> (client join) and <see cref="IServerSessionVerifier"/> (server hasJoined). The method bodies and signatures match the seam contracts directly.</remarks>
public sealed class YggdrasilSessionService : ISessionAuthenticator, IServerSessionVerifier, IDisposable
{
    private const string MojangBase = "https://sessionserver.mojang.com/";

    private readonly AuthHttpClient _http;
    private readonly Uri _baseUrl;

    public YggdrasilSessionService(SessionServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = new AuthHttpClient(options.HttpHandlerFactory);
        _baseUrl = options.BaseUrl ?? new Uri(MojangBase);
    }

    /// <summary>Client role: proves session ownership to the session server before joining an online-mode server. Sends the access token, profile UUID (undashed), and the SHA-1 server-id hash. A 204 No Content (or any 2xx) is success; anything else raises <see cref="AuthServiceException"/>.</summary>
    public async ValueTask JoinServerAsync(string serverIdHash, ProfileCredentials creds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(serverIdHash);
        ArgumentNullException.ThrowIfNull(creds);

        string payload =
            "{\"accessToken\":\"" + JsonEncode(creds.AccessToken)
            + "\",\"selectedProfile\":\"" + JsonEncode(ToUndashed(creds.Profile.Id))
            + "\",\"serverId\":\"" + JsonEncode(serverIdHash) + "\"}";

        var url = new Uri(_baseUrl, "session/minecraft/join");
        AuthHttpResponse response = await _http.PostJsonAsync(url, payload, null, null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            throw new AuthServiceException("session_join", response.StatusCode, "Session join was rejected by the session server.");

    }

    /// <summary>Server role: verifies that a joining client authenticated with the session server (<c>hasJoined</c>). Returns the authenticated <see cref="GameProfile"/> (including signed textures properties) on 200, or null when the session server reports no matching join (204/empty).</summary>
    public async ValueTask<GameProfile?> VerifyJoinAsync(
        string username, string serverIdHash, IPAddress? clientIp, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(serverIdHash);

        string query = "session/minecraft/hasJoined?username=" + Uri.EscapeDataString(username)
            + "&serverId=" + Uri.EscapeDataString(serverIdHash);
        if (clientIp is not null)
            query += "&ip=" + Uri.EscapeDataString(clientIp.ToString());

        var url = new Uri(_baseUrl, query);
        AuthHttpResponse response = await _http.GetAsync(url, null, null, ct).ConfigureAwait(false);

        if (response.StatusCode == 204 || string.IsNullOrWhiteSpace(response.Body))
            return null;

        if (!response.IsSuccess)
            throw new AuthServiceException("session_hasjoined", response.StatusCode, "hasJoined verification failed.");

        using JsonDocument doc = response.ParseJson();
        return ReadProfile(doc.RootElement);
    }

    private static GameProfile ReadProfile(JsonElement root)
    {
        string id = root.GetProperty("id").GetString()!;
        string name = root.GetProperty("name").GetString()!;
        var profile = new GameProfile(FromUndashed(id), name);

        if (root.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Array)
        {
            var list = new List<ProfileProperty>(props.GetArrayLength());
            foreach (JsonElement prop in props.EnumerateArray())
            {
                string pName = prop.GetProperty("name").GetString()!;
                string value = prop.GetProperty("value").GetString()!;
                string? signature = prop.TryGetProperty("signature", out JsonElement sig) ? sig.GetString() : null;
                list.Add(new ProfileProperty(pName, value, signature));
            }

            profile = profile with { Properties = list };
        }

        return profile;
    }

    private static string ToUndashed(Guid id) => id.ToString("N");

    private static Guid FromUndashed(string id) =>
        id.Contains('-', StringComparison.Ordinal) ? Guid.Parse(id) : Guid.ParseExact(id, "N");

    private static string JsonEncode(string value) => JsonEncodedText.Encode(value).ToString();

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}
