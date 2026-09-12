using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBlockCodecs
{
    /// <summary>1.8 block break animation: VarInt breaker id, packed BlockPos, progress byte.</summary>
    public static readonly PacketCodec<ClientboundBlockDestructionPacket> BlockDestructionV1_8 = MakeBlockDestruction(BlockPosLayout.PrePacked114);

    /// <summary>Modern block destruction: VarInt id, packed BlockPos, progress byte.</summary>
    public static readonly PacketCodec<ClientboundBlockDestructionPacket> BlockDestructionV1_14 = MakeBlockDestruction(BlockPosLayout.Packed114);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBlockDestruction(PacketBindings bindings)
    {
        // 47-404 is one wire form (VarInt breaker, PRE-1.14 packed block pos, progress byte); 1.14 only changes the block-pos packing.
        bindings.Packet(WorldPackets.Clientbound.BlockDestruction)
            .From(JavaProtocols.V1_8, WorldBlockCodecs.BlockDestructionV1_8)
            .From(JavaProtocols.V1_14, WorldBlockCodecs.BlockDestructionV1_14)
            .AliasedAs(Identifier.Minecraft("block_break_animation"));
    }
}
