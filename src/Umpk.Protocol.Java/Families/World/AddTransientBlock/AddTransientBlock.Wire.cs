using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBlockCodecs
{
    /// <summary>26.3 transient block: packed BlockPos + VarInt state, the block-update body under a new identity.</summary>
    public static readonly PacketCodec<ClientboundAddTransientBlockPacket> AddTransientBlockV26_3 =
        PacketCodec<ClientboundAddTransientBlockPacket>.Of(
            static (ref PacketWriter w, ClientboundAddTransientBlockPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Position, BlockPosLayout.Packed114);
                w.WriteVarInt(p.BlockStateId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundAddTransientBlockPacket(r.ReadBlockPos(BlockPosLayout.Packed114), r.ReadVarInt()),
            WireShape.Of("block_pos,varint", BlockPosLayout.Packed114.ToString()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAddTransientBlock(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.AddTransientBlock)
            .From(JavaProtocols.V26_3, WorldBlockCodecs.AddTransientBlockV26_3);
    }
}
