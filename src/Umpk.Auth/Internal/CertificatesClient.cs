using System.Globalization;
using System.Text.Json;
using Umpk.Protocol.Java.Signing;

namespace Umpk.Auth.Internal;

/// <summary>Fetches player profile-key certificates from the Minecraft-services endpoint (or a Yggdrasil provider's mirror). Parses key-pair PEM strings, both signature variants, and the validity window. The access token is sent as a bearer and never logged.</summary>
internal sealed class CertificatesClient
{
    private const string MojangCertificatesUrl = "https://api.minecraftservices.com/player/certificates";

    private readonly AuthHttpClient _http;
    private readonly Uri? _yggdrasilBaseUrl;

    public CertificatesClient(AuthHttpClient http, Uri? yggdrasilBaseUrl)
    {
        _http = http;
        _yggdrasilBaseUrl = yggdrasilBaseUrl;
    }

    public async Task<PlayerCertificates> FetchAsync(string accessToken, CancellationToken ct)
    {
        Uri url = _yggdrasilBaseUrl is null
            ? new Uri(MojangCertificatesUrl)
            : new Uri(_yggdrasilBaseUrl, "minecraftservices/player/certificates");

        AuthHttpResponse response = await _http.PostJsonAsync(url, "", accessToken, null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            throw new AuthServiceException("certificates", response.StatusCode, "Certificate endpoint returned a non-success status.");

        using JsonDocument doc = response.ParseJson();
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("keyPair", out JsonElement keyPair)
            || !keyPair.TryGetProperty("publicKey", out JsonElement publicKey)
            || !keyPair.TryGetProperty("privateKey", out JsonElement privateKey)
            || !root.TryGetProperty("publicKeySignature", out JsonElement sig)
            || !root.TryGetProperty("publicKeySignatureV2", out JsonElement sigV2)
            || !root.TryGetProperty("expiresAt", out JsonElement expiresAt)
            || !root.TryGetProperty("refreshedAfter", out JsonElement refreshedAfter))
            throw new AuthServiceException("certificates", response.StatusCode, "Certificate endpoint returned an unexpected payload.");

        return new PlayerCertificates(
            publicKey.GetString()!,
            privateKey.GetString()!,
            sig.GetString()!,
            sigV2.GetString()!,
            ParseTimestamp(expiresAt.GetString()!),
            ParseTimestamp(refreshedAfter.GetString()!));
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
