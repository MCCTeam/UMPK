using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityStateCodecs
{
    /// <summary>1.8 use bed: player id VarInt and bed block position.</summary>
    public static readonly PacketCodec<ClientboundUseBedPacket> UseBedV1_8 =
        PacketCodec<ClientboundUseBedPacket>.Of(
            static (ref PacketWriter w, ClientboundUseBedPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.PlayerId);
                w.WriteBlockPos(p.Position, BlockPosLayout.PrePacked114);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundUseBedPacket(r.ReadVarInt(), r.ReadBlockPos(BlockPosLayout.PrePacked114)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUseBed(PacketBindings bindings)
    {
        // VarInt player id + pre-1.14 packed block position, unchanged 47 through 404. The packet leaves the protocol at 1.14, where sleeping moved into entity metadata.
        bindings.Packet(EntityPackets.Clientbound.UseBed)
            .From(JavaProtocols.V1_8, EntityStateCodecs.UseBedV1_8);
    }
}
