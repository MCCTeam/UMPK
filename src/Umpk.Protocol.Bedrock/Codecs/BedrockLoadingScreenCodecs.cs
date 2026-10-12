using System.Buffers;

namespace Umpk.Protocol.Bedrock;

internal enum BedrockLoadingScreenType
{
    Unknown = 0,
    StartLoadingScreen = 1,
    EndLoadingScreen = 2
}

internal readonly record struct BedrockLoadingScreenPacket(BedrockLoadingScreenType Type, uint? LoadingScreenId);

/// <summary>Reads and writes protocol-2169 loading notifications while preserving optional identifier engagement.</summary>
internal static class BedrockLoadingScreenCodecs
{
    private const int MaximumPayloadBytes = 10;

    internal static byte[] Encode(BedrockLoadingScreenPacket packet)
    {
        ValidateType(packet.Type);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteZigZag32((int)packet.Type);
        writer.WriteBoolean(packet.LoadingScreenId.HasValue);
        if (packet.LoadingScreenId is uint loadingScreenId)
            writer.WriteUInt32LittleEndian(loadingScreenId);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockLoadingScreenPacket Decode(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        if (payload.Length > MaximumPayloadBytes)
            throw new BedrockCodecException($"The loading-screen payload has {payload.Length} bytes and exceeds the {MaximumPayloadBytes} byte limit.");
        var reader = new BedrockReader(payload, limits);
        var type = (BedrockLoadingScreenType)reader.ReadZigZag32();
        ValidateType(type);
        uint? loadingScreenId = reader.ReadBoolean() ? reader.ReadUInt32LittleEndian() : null;
        if (!reader.End)
            throw new BedrockCodecException($"The loading-screen payload has {reader.Remaining} unread bytes.");
        return new BedrockLoadingScreenPacket(type, loadingScreenId);
    }

    private static void ValidateType(BedrockLoadingScreenType type)
    {
        if (type is not (BedrockLoadingScreenType.Unknown or BedrockLoadingScreenType.StartLoadingScreen or BedrockLoadingScreenType.EndLoadingScreen))
            throw new BedrockCodecException($"Unsupported loading-screen type {(int)type}.");
    }
}
