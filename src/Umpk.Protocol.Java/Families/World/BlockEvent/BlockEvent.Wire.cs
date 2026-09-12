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
    /// <summary>1.8 block action: packed BlockPos, two bytes, VarInt block id.</summary>
    public static readonly PacketCodec<ClientboundBlockEventPacket> BlockEventV1_8 = MakeBlockEvent(BlockPosLayout.PrePacked114);

    /// <summary>Modern block event: packed BlockPos, two bytes, VarInt block id.</summary>
    public static readonly PacketCodec<ClientboundBlockEventPacket> BlockEventV1_14 = MakeBlockEvent(BlockPosLayout.Packed114);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareBlockEvent(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.BlockEvent)
            .From(JavaProtocols.V1_8, WorldBlockCodecs.BlockEventV1_8)
            .From(JavaProtocols.V1_14, WorldBlockCodecs.BlockEventV1_14);
    }
}
