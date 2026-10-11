using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umpk.Client.Bedrock.Authentication;

internal static class BedrockLoginArtifactTestFactory
{
    internal static BedrockLoginArtifacts CreateOffline(
        BedrockConnectionKey key,
        string name,
        Guid identity,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string audience)
    {
        string multiplayerToken = CreateOfflineToken(key, name, identity, notBefore, expiresAt, audience);
        string certificate = JsonSerializer.Serialize(new BedrockCertificateEnvelope([string.Empty]), BedrockAuthJsonContext.Default.BedrockCertificateEnvelope);
        string envelope = JsonSerializer.Serialize(new BedrockIdentityEnvelope(certificate, 2, multiplayerToken), BedrockAuthJsonContext.Default.BedrockIdentityEnvelope);
        var profile = new BedrockClientDataProfile(1, 1, 1, Guid.Parse("4afdd177-62c4-4781-b302-beb9402b65b9"), "UMPK", 7, "1.26.45", "en_GB", "127.0.0.1:19132", name, 4);
        var skin = new BedrockClientSkinProfile("skin-id", Convert.ToBase64String(new byte[64 * 64 * 4]), 64, 64, "e30=", "", "MS4xNC4w", "", 0, 0, false, false, false, [], "wide", "", [], [], "", "");
        string clientData = BedrockCompactJwt.CreateClientDataToken(profile, skin, key);
        return new BedrockLoginArtifacts(BedrockAuthenticationMode.Offline, envelope, clientData, expiresAt);
    }

    internal static string CreateOfflineToken(
        BedrockConnectionKey key,
        string name,
        Guid identity,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string audience) =>
        BedrockCompactJwt.CreateOfflineToken(name, identity, notBefore, expiresAt, audience, key);

    internal static string SignRawPayload(BedrockConnectionKey key, string payload) =>
        BedrockCompactJwt.SignRawPayload(Encoding.UTF8.GetBytes(payload), key);

    internal static JsonDocument DecodePayload(string token) => JsonDocument.Parse(BedrockCompactJwt.DecodeSegment(token.Split('.')[1]));

    internal static byte[] DecodeSignature(string token) => BedrockCompactJwt.DecodeSegment(token.Split('.')[2]);
}

internal static class BedrockHandshakeTestFactory
{
    internal static string CreateServerToken(BedrockConnectionKey server, byte[] salt)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new BedrockHandshakeClaims(Convert.ToBase64String(salt), server.PublicKeyBase64),
            BedrockAuthJsonContext.Default.BedrockHandshakeClaims);
        return BedrockCompactJwt.SignRawPayload(payload, server);
    }

    internal static string CreateServerTokenWithoutSignedToken(BedrockConnectionKey server, byte[] salt)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new BedrockHandshakeClaims(Convert.ToBase64String(salt), null),
            BedrockAuthJsonContext.Default.BedrockHandshakeClaims);
        return BedrockCompactJwt.SignRawPayload(payload, server);
    }

    internal static string CreateWrongCurveServerToken(byte[] salt)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new BedrockJwtHeader("ES384", publicKey), BedrockAuthJsonContext.Default.BedrockJwtHeader));
        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new BedrockHandshakeClaims(Convert.ToBase64String(salt), publicKey), BedrockAuthJsonContext.Default.BedrockHandshakeClaims));
        byte[] content = Encoding.ASCII.GetBytes($"{header}.{payload}");
        byte[] signature = key.SignData(content, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private static string Base64Url(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
