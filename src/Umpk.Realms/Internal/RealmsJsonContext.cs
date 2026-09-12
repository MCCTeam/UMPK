using System.Text.Json.Serialization;

namespace Umpk.Realms.Internal;

/// <summary>The source-generated <see cref="JsonSerializerContext"/> that decodes the Realms REST payloads. No reflection-based JSON path is used; deserialization always binds to these generated type infos, keeping the client AOT-safe and within the no-reflection BannedSymbols rule.</summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RealmsWorldsResponseDto))]
[JsonSerializable(typeof(RealmsJoinResponseDto))]
[JsonSerializable(typeof(RealmsErrorDto))]
internal sealed partial class RealmsJsonContext : JsonSerializerContext
{
}
