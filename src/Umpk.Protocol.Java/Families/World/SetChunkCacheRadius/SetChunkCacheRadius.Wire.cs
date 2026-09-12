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
    /// <summary>Set chunk cache radius: a VarInt radius.</summary>
    public static readonly PacketCodec<ClientboundSetChunkCacheRadiusPacket> SetChunkCacheRadiusV1_14 =
        PacketCodec<ClientboundSetChunkCacheRadiusPacket>.Of(
            static (ref PacketWriter w, ClientboundSetChunkCacheRadiusPacket p, PacketCodecContext _) => w.WriteVarInt(p.Radius),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetChunkCacheRadiusPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetChunkCacheRadius(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetChunkCacheRadius)
            .From(JavaEras.Palettes, WorldStateCodecs.SetChunkCacheRadiusV1_14);
    }
}
