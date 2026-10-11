using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umpk.Client.Bedrock.Authentication;

internal static class BedrockCompactJwt
{
    private const int MaximumCompactTokenLength = 256 * 1024;
    private const int MaximumJsonLength = 192 * 1024;
    private const int MaximumJsonPropertyCount = 64;
    // The pinned 256x256 default skin carries about 350KB of base64 image data plus geometry, so its
    // client-data token is near 570KB. Handshake and multiplayer tokens keep the strict limits above.
    private const int MaximumClientDataTokenLength = 768 * 1024;
    private const int MaximumClientDataJsonLength = 512 * 1024;
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(2);
    private const string MultiplayerAudience = "api://auth-minecraft-services/multiplayer";

    internal static string CreateOfflineToken(
        string displayName,
        Guid identity,
        DateTimeOffset notBefore,
        DateTimeOffset expiresAt,
        string audience,
        BedrockConnectionKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentNullException.ThrowIfNull(key);
        var claims = new BedrockOfflineMultiplayerClaims(
            key.PublicKeyBase64,
            "0",
            displayName,
            identity.ToString("D"),
            notBefore.ToUnixTimeSeconds(),
            notBefore.ToUnixTimeSeconds(),
            expiresAt.ToUnixTimeSeconds(),
            audience,
            "self");
        return Sign(
            JsonSerializer.SerializeToUtf8Bytes(new BedrockJwtHeader("ES384", key.PublicKeyBase64), BedrockAuthJsonContext.Default.BedrockJwtHeader),
            JsonSerializer.SerializeToUtf8Bytes(claims, BedrockAuthJsonContext.Default.BedrockOfflineMultiplayerClaims),
            key);
    }

    internal static string CreateClientDataToken(BedrockClientDataProfile profile, BedrockClientSkinProfile skin, BedrockConnectionKey key)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(key);
        ValidateClientData(profile, skin);
        var claims = new BedrockClientDataClaims
        {
            ClientRandomId = profile.ClientRandomId,
            CurrentInputMode = profile.CurrentInputMode,
            DefaultInputMode = profile.DefaultInputMode,
            DeviceId = profile.DeviceId.ToString("D"),
            DeviceModel = profile.DeviceModel,
            DeviceOperatingSystem = profile.DeviceOperatingSystem,
            GameVersion = profile.GameVersion,
            GuiScale = -1,
            LanguageCode = profile.LanguageCode,
            GraphicsMode = 1,
            PlayFabId = profile.DeviceId.ToString("N")[..16],
            SelfSignedId = profile.DeviceId.ToString("D"),
            ServerAddress = profile.ServerAddress,
            ThirdPartyName = profile.ThirdPartyName,
            UserInterfaceProfile = 0,
            IsEditorMode = false,
            TrustedSkin = false,
            OverrideSkin = false,
            CompatibleWithClientSideChunkGeneration = false,
            MaximumViewDistance = profile.MaximumViewDistance,
            MemoryTier = 0,
            PlatformType = 0,
            SkinId = skin.SkinId,
            SkinData = skin.SkinData,
            SkinImageWidth = skin.SkinImageWidth,
            SkinImageHeight = skin.SkinImageHeight,
            SkinResourcePatch = skin.SkinResourcePatch,
            SkinGeometryData = skin.SkinGeometryData,
            SkinGeometryDataEngineVersion = skin.SkinGeometryDataEngineVersion,
            CapeData = skin.CapeData,
            CapeImageWidth = skin.CapeImageWidth,
            CapeImageHeight = skin.CapeImageHeight,
            CapeOnClassicSkin = skin.CapeOnClassicSkin,
            PersonaSkin = skin.PersonaSkin,
            PremiumSkin = skin.PremiumSkin,
            AnimatedImageData = skin.AnimatedImageData,
            ArmSize = skin.ArmSize,
            CapeId = skin.CapeId,
            PersonaPieces = skin.PersonaPieces,
            PieceTintColors = skin.PieceTintColors,
            SkinAnimationData = skin.SkinAnimationData,
            SkinColor = skin.SkinColor,
        };
        return Sign(
            JsonSerializer.SerializeToUtf8Bytes(new BedrockJwtHeader("ES384", key.PublicKeyBase64), BedrockAuthJsonContext.Default.BedrockJwtHeader),
            JsonSerializer.SerializeToUtf8Bytes(claims, BedrockAuthJsonContext.Default.BedrockClientDataClaims),
            key);
    }

    internal static BedrockOfflineMultiplayerClaims ValidateOfflineToken(string token, ReadOnlyMemory<byte> expectedConnectionKey, DateTimeOffset now)
    {
        JwtParts parts = Parse(token);
        BedrockJwtHeader header = DeserializeHeader(parts.Header);
        if (!string.Equals(header.Algorithm, "ES384", StringComparison.Ordinal))
            throw Failure(BedrockAuthenticationFailureCode.InvalidAlgorithm, "The JWT algorithm must be ES384.");

        byte[] headerKey = DecodeBase64(header.PublicKey, "JWT public key");
        if (!headerKey.AsSpan().SequenceEqual(expectedConnectionKey.Span))
            throw Failure(BedrockAuthenticationFailureCode.ConnectionKeyMismatch, "The JWT is not bound to the current connection key.");

        VerifySignature(parts, headerKey);
        ValidateUniqueProperties(parts.Payload, "iss", "aud", "nbf", "exp", "iat", "cpk", "xname", "identity", "xid");
        BedrockOfflineMultiplayerClaims claims = DeserializeClaims(parts.Payload);
        if (!string.Equals(claims.Issuer, "self", StringComparison.Ordinal))
            throw Failure(BedrockAuthenticationFailureCode.InvalidIssuer, "The offline token issuer must be self.");
        if (!string.Equals(claims.Audience, MultiplayerAudience, StringComparison.Ordinal))
            throw Failure(BedrockAuthenticationFailureCode.InvalidAudience, "The multiplayer token audience is invalid.");
        if (!string.Equals(claims.ConnectionPublicKey, header.PublicKey, StringComparison.Ordinal))
            throw Failure(BedrockAuthenticationFailureCode.ConnectionKeyMismatch, "The multiplayer token connection key does not match its header.");
        ValidateLifetime(claims.NotBefore, claims.ExpiresAt, now);
        ValidateIssuedAt(claims.IssuedAt, claims.NotBefore, claims.ExpiresAt, now);
        if (!Guid.TryParse(claims.Identity, out Guid identity) || identity == Guid.Empty)
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The offline identity claims are invalid.");
        if (string.IsNullOrWhiteSpace(claims.DisplayName) || claims.DisplayName.Length > 64 || claims.Xuid != "0")
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The offline identity values are invalid.");

        return claims;
    }

    internal static BedrockHandshakeClaims ValidateHandshakeToken(string token, out byte[] serverPublicKey)
    {
        JwtParts parts = Parse(token);
        BedrockJwtHeader header = DeserializeHeader(parts.Header);
        if (!string.Equals(header.Algorithm, "ES384", StringComparison.Ordinal))
            throw Failure(BedrockAuthenticationFailureCode.InvalidAlgorithm, "The handshake JWT algorithm must be ES384.");

        serverPublicKey = DecodeBase64(header.PublicKey, "handshake public key");
        try
        {
            VerifySignature(parts, serverPublicKey);
            ValidateUniqueProperties(parts.Payload, "salt");
            BedrockHandshakeClaims? claims = JsonSerializer.Deserialize(parts.Payload, BedrockAuthJsonContext.Default.BedrockHandshakeClaims);
            return claims ?? throw Failure(BedrockAuthenticationFailureCode.InvalidHandshake, "The handshake claims are missing.");
        }
        catch
        {
            CryptographicOperations.ZeroMemory(serverPublicKey);
            throw;
        }
    }

    internal static void ValidateClientDataToken(string token, ReadOnlyMemory<byte> expectedConnectionKey)
    {
        JwtParts parts = Parse(token, MaximumClientDataTokenLength, MaximumClientDataJsonLength);
        BedrockJwtHeader header = DeserializeHeader(parts.Header);
        if (!string.Equals(header.Algorithm, "ES384", StringComparison.Ordinal))
            throw Failure(BedrockAuthenticationFailureCode.InvalidAlgorithm, "The client-data JWT algorithm must be ES384.");
        byte[] headerKey = DecodeBase64(header.PublicKey, "client-data public key");
        if (!headerKey.AsSpan().SequenceEqual(expectedConnectionKey.Span))
            throw Failure(BedrockAuthenticationFailureCode.ConnectionKeyMismatch, "The client-data JWT is not bound to the current connection key.");
        VerifySignature(parts, headerKey);
        ValidateUniqueProperties(parts.Payload, "DeviceId", "GameVersion", "ServerAddress", "ThirdPartyName", "SkinId", "SkinData", "SkinImageWidth", "SkinImageHeight", "SkinResourcePatch");
        BedrockClientDataClaims? claims = JsonSerializer.Deserialize(parts.Payload, BedrockAuthJsonContext.Default.BedrockClientDataClaims);
        if (claims is null || !string.Equals(claims.GameVersion, "1.26.45", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(claims.ServerAddress) || string.IsNullOrWhiteSpace(claims.SkinData))
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The client-data JWT is incomplete.");
        if (claims.MaximumViewDistance < 0 || claims.MaximumViewDistance > 32)
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The client-data JWT view distance is outside the supported range.");
        if (claims.TrustedSkin || claims.OverrideSkin)
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The client-data JWT must not claim a trusted skin.");
        if (claims.SkinImageWidth <= 0 || claims.SkinImageHeight <= 0 || claims.SkinImageWidth > 256 || claims.SkinImageHeight > 256)
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The client-data JWT skin image is invalid.");
        byte[] skinData = DecodeBase64(claims.SkinData, "skin data");
        int expectedLength = checked(claims.SkinImageWidth * claims.SkinImageHeight * 4);
        if (skinData.Length != expectedLength)
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The client-data JWT skin image is invalid.");
    }

    internal static string SignRawPayload(ReadOnlySpan<byte> payload, BedrockConnectionKey key)
    {
        byte[] header = JsonSerializer.SerializeToUtf8Bytes(new BedrockJwtHeader("ES384", key.PublicKeyBase64), BedrockAuthJsonContext.Default.BedrockJwtHeader);
        return Sign(header, payload, key);
    }

    internal static byte[] DecodeSegment(string value) => DecodeSegment(value, MaximumCompactTokenLength);

    internal static byte[] DecodeSegment(string value, int maximumLength)
    {
        if (value.Length > maximumLength)
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT segment exceeds its size limit.");
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT segment is not valid base64url."),
        };
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT segment is not valid base64url.", exception);
        }
    }

    private static string Sign(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, BedrockConnectionKey key)
    {
        string encodedHeader = EncodeSegment(header);
        string encodedPayload = EncodeSegment(payload);
        byte[] content = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedPayload}");
        byte[] signature = key.Sign(content);
        if (signature.Length != 96)
            throw Failure(BedrockAuthenticationFailureCode.InvalidSignature, "An ES384 JWT signature must contain 96 bytes.");
        return $"{encodedHeader}.{encodedPayload}.{EncodeSegment(signature)}";
    }

    private static JwtParts Parse(string token) => Parse(token, MaximumCompactTokenLength, MaximumJsonLength);

    private static JwtParts Parse(string token, int maximumTokenLength, int maximumJsonLength)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > maximumTokenLength)
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The compact JWT is empty or exceeds its size limit.");
        string[] segments = token.Split('.');
        if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty))
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The compact JWT must contain three segments.");
        byte[] header = DecodeSegment(segments[0], maximumTokenLength);
        byte[] payload = DecodeSegment(segments[1], maximumTokenLength);
        byte[] signature = DecodeSegment(segments[2], maximumTokenLength);
        if (header.Length > maximumJsonLength || payload.Length > maximumJsonLength)
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT JSON exceeds its size limit.");
        return new JwtParts(segments[0], segments[1], header, payload, signature);
    }

    private static BedrockJwtHeader DeserializeHeader(byte[] json)
    {
        ValidateUniqueProperties(json, "alg", "x5u");
        BedrockJwtHeader? header = JsonSerializer.Deserialize(json, BedrockAuthJsonContext.Default.BedrockJwtHeader);
        if (header is null || string.IsNullOrWhiteSpace(header.PublicKey))
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT header is incomplete.");
        return header;
    }

    private static BedrockOfflineMultiplayerClaims DeserializeClaims(byte[] json) =>
        JsonSerializer.Deserialize(json, BedrockAuthJsonContext.Default.BedrockOfflineMultiplayerClaims)
        ?? throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The multiplayer token claims are missing.");

    private static void VerifySignature(JwtParts parts, ReadOnlySpan<byte> publicKey)
    {
        if (parts.Signature.Length != 96)
            throw Failure(BedrockAuthenticationFailureCode.InvalidSignature, "An ES384 JWT signature must contain 96 bytes.");
        using ECDsa key = BedrockConnectionKey.ImportP384PublicKey(publicKey);
        byte[] content = Encoding.ASCII.GetBytes($"{parts.EncodedHeader}.{parts.EncodedPayload}");
        if (!key.VerifyData(content, parts.Signature, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw Failure(BedrockAuthenticationFailureCode.InvalidSignature, "The JWT signature is invalid.");
    }

    private static void ValidateLifetime(long notBefore, long expiresAt, DateTimeOffset now)
    {
        DateTimeOffset starts;
        DateTimeOffset expires;
        try
        {
            starts = DateTimeOffset.FromUnixTimeSeconds(notBefore);
            expires = DateTimeOffset.FromUnixTimeSeconds(expiresAt);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The token lifetime is outside the supported range.", exception);
        }
        if (expires <= starts || expires - starts > MaximumLifetime)
            throw Failure(BedrockAuthenticationFailureCode.ArtifactLifetimeExceeded, "The token lifetime is invalid.");
        if (now + AllowedClockSkew < starts)
            throw Failure(BedrockAuthenticationFailureCode.ArtifactNotYetValid, "The token is not valid yet.");
        if (now - AllowedClockSkew >= expires)
            throw Failure(BedrockAuthenticationFailureCode.ExpiredArtifact, "The token has expired.");
    }

    private static void ValidateIssuedAt(long issuedAt, long notBefore, long expiresAt, DateTimeOffset now)
    {
        DateTimeOffset issued;
        try
        {
            issued = DateTimeOffset.FromUnixTimeSeconds(issuedAt);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The token issue time is outside the supported range.", exception);
        }
        if (issuedAt < notBefore || issuedAt > expiresAt)
            throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The token issue time is outside its validity window.");
        if (issued > now + AllowedClockSkew)
            throw Failure(BedrockAuthenticationFailureCode.ArtifactNotYetValid, "The token was issued in the future.");
    }

    private static void ValidateClientData(BedrockClientDataProfile profile, BedrockClientSkinProfile skin)
    {
        if (profile.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(profile.DeviceModel) || string.IsNullOrWhiteSpace(profile.GameVersion) || string.IsNullOrWhiteSpace(profile.LanguageCode) || string.IsNullOrWhiteSpace(profile.ServerAddress) || string.IsNullOrWhiteSpace(profile.ThirdPartyName))
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The Bedrock client profile is incomplete.");
        if (profile.MaximumViewDistance < 0 || profile.MaximumViewDistance > 32)
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The Bedrock client view distance is outside the supported range.");
        if (skin.SkinImageWidth <= 0 || skin.SkinImageHeight <= 0 || skin.SkinImageWidth > 256 || skin.SkinImageHeight > 256 || string.IsNullOrWhiteSpace(skin.SkinId) || string.IsNullOrWhiteSpace(skin.SkinData) || string.IsNullOrWhiteSpace(skin.SkinResourcePatch))
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The Bedrock skin profile is incomplete.");
        byte[] skinBytes = DecodeBase64(skin.SkinData, "skin data");
        int expectedLength = checked(skin.SkinImageWidth * skin.SkinImageHeight * 4);
        if (skinBytes.Length != expectedLength)
            throw Failure(BedrockAuthenticationFailureCode.InvalidClientData, "The Bedrock skin image length does not match its dimensions.");
    }

    internal static void ValidateUniqueProperties(ReadOnlySpan<byte> json, params string[] required)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT JSON root must be an object.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, $"The JWT contains a duplicate {property.Name} property.");
                if (names.Count > MaximumJsonPropertyCount)
                    throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT contains too many properties.");
            }
            foreach (string name in required)
            {
                if (!names.Contains(name))
                    throw Failure(BedrockAuthenticationFailureCode.InvalidArtifact, $"The JWT is missing the {name} property.");
            }
        }
        catch (JsonException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, "The JWT contains invalid JSON.", exception);
        }
    }

    private static string EncodeSegment(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64(string value, string label)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidArtifact, $"The {label} is not valid base64.", exception);
        }
    }

    private static BedrockAuthenticationException Failure(BedrockAuthenticationFailureCode code, string message) => new(code, message);

    private sealed record JwtParts(string EncodedHeader, string EncodedPayload, byte[] Header, byte[] Payload, byte[] Signature);
}
