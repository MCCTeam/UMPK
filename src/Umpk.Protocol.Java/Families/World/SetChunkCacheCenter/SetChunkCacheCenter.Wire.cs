using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldStateCodecs
{
    /// <summary>Set chunk cache center: two VarInt chunk coordinates.</summary>
    public static readonly PacketCodec<ClientboundSetChunkCacheCenterPacket> SetChunkCacheCenterV1_14 =
        PacketCodec<ClientboundSetChunkCacheCenterPacket>.Of(
            static (ref PacketWriter w, ClientboundSetChunkCacheCenterPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ChunkX);
                w.WriteVarInt(p.ChunkZ);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetChunkCacheCenterPacket(r.ReadVarInt(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetChunkCacheCenter(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetChunkCacheCenter)
            .From(JavaEras.Palettes, WorldStateCodecs.SetChunkCacheCenterV1_14);
    }
}
