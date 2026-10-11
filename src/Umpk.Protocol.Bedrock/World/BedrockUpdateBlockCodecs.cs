using System.Buffers;

namespace Umpk.Protocol.Bedrock;

// UpdateBlock carries a single layered block edit with an exact block position, a session runtime ID, raw update flags, and a storage layer.
// Positions use zigzag32 coordinates so negative worlds survive the round trip.
// The runtime ID stays opaque here; resolution against the live StartGame palette belongs to later evidence, so unknown and custom IDs are preserved and never read as air.
// Flags preserve every wire bit; the agreed neighbor, network, no-graphic, and priority bits are named while any other bit survives untouched.
// The layer selects the storage layer inside one cell; only layers 0 and 1 are accepted.
[Flags]
internal enum BedrockUpdateBlockFlags : uint
{
    None = 0,
    Neighbors = 1,
    Network = 2,
    NoGraphic = 4,
    Priority = 8,
}

internal sealed class BedrockUpdateBlockPacket : IEquatable<BedrockUpdateBlockPacket>
{
    internal BedrockUpdateBlockPacket(int x, int y, int z, uint blockRuntimeId, uint flags, uint layer)
    {
        if (layer is not (0 or 1))
            throw new BedrockCodecException($"The update block layer {layer} is outside the supported range 0 to 1.");
        X = x;
        Y = y;
        Z = z;
        BlockRuntimeId = blockRuntimeId;
        Flags = flags;
        Layer = layer;
    }

    internal int X { get; }

    internal int Y { get; }

    internal int Z { get; }

    internal uint BlockRuntimeId { get; }

    internal uint Flags { get; }

    internal uint Layer { get; }

    public bool Equals(BedrockUpdateBlockPacket? other) =>
        other is not null
        && X == other.X
        && Y == other.Y
        && Z == other.Z
        && BlockRuntimeId == other.BlockRuntimeId
        && Flags == other.Flags
        && Layer == other.Layer;

    public override bool Equals(object? obj) => Equals(obj as BedrockUpdateBlockPacket);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, BlockRuntimeId, Flags, Layer);
}

internal static class BedrockUpdateBlockCodecs
{
    internal static byte[] EncodeUpdateBlock(BedrockUpdateBlockPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteZigZag32(packet.X);
        writer.WriteZigZag32(packet.Y);
        writer.WriteZigZag32(packet.Z);
        writer.WriteUInt32VarInt(packet.BlockRuntimeId);
        writer.WriteUInt32VarInt(packet.Flags);
        writer.WriteUInt32VarInt(packet.Layer);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockUpdateBlockPacket DecodeUpdateBlock(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        int x = reader.ReadZigZag32();
        int y = reader.ReadZigZag32();
        int z = reader.ReadZigZag32();
        uint blockRuntimeId = reader.ReadUInt32VarInt();
        uint flags = reader.ReadUInt32VarInt();
        uint layer = reader.ReadUInt32VarInt();
        if (!reader.End)
            throw new BedrockCodecException("The update block packet has unread bytes.");
        return new BedrockUpdateBlockPacket(x, y, z, blockRuntimeId, flags, layer);
    }
}
