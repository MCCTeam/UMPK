using System.Text.Json.Serialization;

namespace Umpk.Client.Bedrock.Authentication;

internal sealed record BedrockJwtHeader(
    [property: JsonPropertyName("alg")] string Algorithm,
    [property: JsonPropertyName("x5u")] string PublicKey);

internal sealed record BedrockOfflineMultiplayerClaims(
    [property: JsonPropertyName("cpk")] string ConnectionPublicKey,
    [property: JsonPropertyName("xid")] string Xuid,
    [property: JsonPropertyName("xname")] string DisplayName,
    [property: JsonPropertyName("identity")] string Identity,
    [property: JsonPropertyName("iat")] long IssuedAt,
    [property: JsonPropertyName("nbf")] long NotBefore,
    [property: JsonPropertyName("exp")] long ExpiresAt,
    [property: JsonPropertyName("aud")] string Audience,
    [property: JsonPropertyName("iss")] string Issuer);

internal sealed record BedrockHandshakeClaims(
    [property: JsonPropertyName("salt")] string Salt,
    [property: JsonPropertyName("signedToken")] string? SignedToken);

internal sealed record BedrockCertificateEnvelope(
    [property: JsonPropertyName("chain")] IReadOnlyList<string> Chain);

internal sealed record BedrockIdentityEnvelope(
    [property: JsonPropertyName("Certificate")] string Certificate,
    [property: JsonPropertyName("AuthenticationType")] int AuthenticationType,
    [property: JsonPropertyName("Token")] string Token);

internal sealed record BedrockClientSkinProfile(
    string SkinId,
    string SkinData,
    int SkinImageWidth,
    int SkinImageHeight,
    string SkinResourcePatch,
    string SkinGeometryData,
    string SkinGeometryDataEngineVersion,
    string CapeData,
    int CapeImageWidth,
    int CapeImageHeight,
    bool CapeOnClassicSkin,
    bool PersonaSkin,
    bool PremiumSkin,
    IReadOnlyList<BedrockSkinAnimationData> AnimatedImageData,
    string ArmSize,
    string CapeId,
    IReadOnlyList<BedrockPersonaPiece> PersonaPieces,
    IReadOnlyList<BedrockPersonaPieceTintColor> PieceTintColors,
    string SkinAnimationData,
    string SkinColor);

internal sealed record BedrockClientDataProfile(
    long ClientRandomId,
    int CurrentInputMode,
    int DefaultInputMode,
    Guid DeviceId,
    string DeviceModel,
    int DeviceOperatingSystem,
    string GameVersion,
    string LanguageCode,
    string ServerAddress,
    string ThirdPartyName,
    int MaximumViewDistance);

internal sealed class BedrockSkinAnimationData
{
    [JsonPropertyName("AnimationExpression")]
    public int AnimationExpression { get; init; }

    [JsonPropertyName("Frames")]
    public int Frames { get; init; }

    [JsonPropertyName("Image")]
    public required string Image { get; init; }

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; init; }

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; init; }

    [JsonPropertyName("Type")]
    public int Type { get; init; }
}

internal sealed class BedrockPersonaPiece
{
    [JsonPropertyName("IsDefault")]
    public bool IsDefault { get; init; }

    [JsonPropertyName("PackId")]
    public required string PackId { get; init; }

    [JsonPropertyName("PieceId")]
    public required string PieceId { get; init; }

    [JsonPropertyName("PieceType")]
    public required string PieceType { get; init; }

    [JsonPropertyName("ProductId")]
    public required string ProductId { get; init; }
}

internal sealed class BedrockPersonaPieceTintColor
{
    [JsonPropertyName("Colors")]
    public required IReadOnlyList<string> Colors { get; init; }

    [JsonPropertyName("PieceType")]
    public required string PieceType { get; init; }
}

internal sealed class BedrockClientDataClaims
{
    [JsonPropertyName("ClientRandomId")]
    public long ClientRandomId { get; init; }

    [JsonPropertyName("CurrentInputMode")]
    public int CurrentInputMode { get; init; }

    [JsonPropertyName("DefaultInputMode")]
    public int DefaultInputMode { get; init; }

    [JsonPropertyName("DeviceId")]
    public required string DeviceId { get; init; }

    [JsonPropertyName("DeviceModel")]
    public required string DeviceModel { get; init; }

    [JsonPropertyName("DeviceOS")]
    public int DeviceOperatingSystem { get; init; }

    [JsonPropertyName("GameVersion")]
    public required string GameVersion { get; init; }

    [JsonPropertyName("GuiScale")]
    public int GuiScale { get; init; }

    [JsonPropertyName("LanguageCode")]
    public required string LanguageCode { get; init; }

    [JsonPropertyName("GraphicsMode")]
    public int GraphicsMode { get; init; }

    [JsonPropertyName("PlatformOfflineId")]
    public string PlatformOfflineId { get; init; } = string.Empty;

    [JsonPropertyName("PlatformOnlineId")]
    public string PlatformOnlineId { get; init; } = string.Empty;

    [JsonPropertyName("PlayFabId")]
    public required string PlayFabId { get; init; }

    [JsonPropertyName("SelfSignedId")]
    public required string SelfSignedId { get; init; }

    [JsonPropertyName("ServerAddress")]
    public required string ServerAddress { get; init; }

    [JsonPropertyName("ThirdPartyName")]
    public required string ThirdPartyName { get; init; }

    [JsonPropertyName("UIProfile")]
    public int UserInterfaceProfile { get; init; }

    [JsonPropertyName("IsEditorMode")]
    public bool IsEditorMode { get; init; }

    [JsonPropertyName("TrustedSkin")]
    public bool TrustedSkin { get; init; }

    [JsonPropertyName("OverrideSkin")]
    public bool OverrideSkin { get; init; }

    [JsonPropertyName("CompatibleWithClientSideChunkGen")]
    public bool CompatibleWithClientSideChunkGeneration { get; init; }

    [JsonPropertyName("MaxViewDistance")]
    public int MaximumViewDistance { get; init; }

    [JsonPropertyName("MemoryTier")]
    public int MemoryTier { get; init; }

    [JsonPropertyName("PlatformType")]
    public int PlatformType { get; init; }

    [JsonPropertyName("SkinId")]
    public required string SkinId { get; init; }

    [JsonPropertyName("SkinData")]
    public required string SkinData { get; init; }

    [JsonPropertyName("SkinImageWidth")]
    public int SkinImageWidth { get; init; }

    [JsonPropertyName("SkinImageHeight")]
    public int SkinImageHeight { get; init; }

    [JsonPropertyName("SkinResourcePatch")]
    public required string SkinResourcePatch { get; init; }

    [JsonPropertyName("SkinGeometryData")]
    public required string SkinGeometryData { get; init; }

    [JsonPropertyName("SkinGeometryDataEngineVersion")]
    public required string SkinGeometryDataEngineVersion { get; init; }

    [JsonPropertyName("CapeData")]
    public required string CapeData { get; init; }

    [JsonPropertyName("CapeImageWidth")]
    public int CapeImageWidth { get; init; }

    [JsonPropertyName("CapeImageHeight")]
    public int CapeImageHeight { get; init; }

    [JsonPropertyName("CapeOnClassicSkin")]
    public bool CapeOnClassicSkin { get; init; }

    [JsonPropertyName("PersonaSkin")]
    public bool PersonaSkin { get; init; }

    [JsonPropertyName("PremiumSkin")]
    public bool PremiumSkin { get; init; }

    [JsonPropertyName("AnimatedImageData")]
    public required IReadOnlyList<BedrockSkinAnimationData> AnimatedImageData { get; init; }

    [JsonPropertyName("ArmSize")]
    public required string ArmSize { get; init; }

    [JsonPropertyName("CapeId")]
    public required string CapeId { get; init; }

    [JsonPropertyName("PersonaPieces")]
    public required IReadOnlyList<BedrockPersonaPiece> PersonaPieces { get; init; }

    [JsonPropertyName("PieceTintColors")]
    public required IReadOnlyList<BedrockPersonaPieceTintColor> PieceTintColors { get; init; }

    [JsonPropertyName("SkinAnimationData")]
    public required string SkinAnimationData { get; init; }

    [JsonPropertyName("SkinColor")]
    public required string SkinColor { get; init; }
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BedrockJwtHeader))]
[JsonSerializable(typeof(BedrockOfflineMultiplayerClaims))]
[JsonSerializable(typeof(BedrockHandshakeClaims))]
[JsonSerializable(typeof(BedrockCertificateEnvelope))]
[JsonSerializable(typeof(BedrockIdentityEnvelope))]
[JsonSerializable(typeof(BedrockClientDataClaims))]
[JsonSerializable(typeof(BedrockSkinAnimationData))]
[JsonSerializable(typeof(BedrockPersonaPiece))]
[JsonSerializable(typeof(BedrockPersonaPieceTintColor))]
internal sealed partial class BedrockAuthJsonContext : JsonSerializerContext
{
}
