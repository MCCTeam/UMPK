using System.Buffers;

namespace Umpk.Protocol.Bedrock;

/// <summary>Represents the protocol-2169 respawn position, state byte, and runtime entity identifier exactly as they appear on the wire.</summary>
internal sealed class BedrockRespawnPacket : IEquatable<BedrockRespawnPacket>
{
    internal BedrockRespawnPacket(BedrockVector3 position, byte state, ulong runtimeEntityId)
    {
        Position = position;
        State = state;
        RuntimeEntityId = runtimeEntityId;
    }

    internal BedrockVector3 Position { get; }

    internal byte State { get; }

    internal ulong RuntimeEntityId { get; }

    public bool Equals(BedrockRespawnPacket? other) =>
        other is not null
        && Position.Equals(other.Position)
        && State == other.State
        && RuntimeEntityId == other.RuntimeEntityId;

    public override bool Equals(object? obj) => Equals(obj as BedrockRespawnPacket);

    public override int GetHashCode() => HashCode.Combine(Position, State, RuntimeEntityId);
}

/// <summary>Reads and writes the bounded protocol-2169 respawn payload without assigning gameplay meaning to the state byte.</summary>
internal static class BedrockRespawnCodecs
{
    private const int MaximumPayloadBytes = 64;

    internal static byte[] Encode(BedrockRespawnPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteSingleLittleEndian(packet.Position.X);
        writer.WriteSingleLittleEndian(packet.Position.Y);
        writer.WriteSingleLittleEndian(packet.Position.Z);
        writer.WriteByte(packet.State);
        writer.WriteUInt64VarInt(packet.RuntimeEntityId);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockRespawnPacket Decode(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        if (payload.Length > MaximumPayloadBytes)
            throw new BedrockCodecException($"The respawn payload has {payload.Length} bytes and exceeds the {MaximumPayloadBytes} byte limit.");
        var reader = new BedrockReader(payload, limits);
        BedrockVector3 position = ReadPosition(ref reader);
        byte state = reader.ReadByte();
        ulong runtimeEntityId = reader.ReadUInt64VarInt();
        if (!reader.End)
            throw new BedrockCodecException($"The respawn payload has {reader.Remaining} unread bytes.");
        return new BedrockRespawnPacket(position, state, runtimeEntityId);
    }

    private static BedrockVector3 ReadPosition(ref BedrockReader reader)
    {
        float x = reader.ReadSingleLittleEndian();
        float y = reader.ReadSingleLittleEndian();
        float z = reader.ReadSingleLittleEndian();
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            throw new BedrockCodecException("The respawn position contains a non-finite component.");
        return new BedrockVector3(x, y, z);
    }
}
