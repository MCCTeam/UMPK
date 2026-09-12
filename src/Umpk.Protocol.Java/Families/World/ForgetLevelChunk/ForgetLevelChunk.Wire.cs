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
    /// <summary>1.9-1.13.2 forget level chunk: two plain BIG-ENDIAN INTS (chunk x then chunk z), not the packed ChunkPos long the 1.14 form uses. Versions 1.9 through 1.13.2 carry <c>x</c> and <c>z</c> as separate ints. Version 1.14 packs both coordinates into one long. The two forms are the same eight bytes in a different order, so a mis-binding would decode every unload to a wrong-but-plausible chunk rather than fault.</summary>
    public static readonly PacketCodec<ClientboundForgetLevelChunkPacket> ForgetLevelChunkV1_9 =
        PacketCodec<ClientboundForgetLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundForgetLevelChunkPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.Chunk.X);
                w.WriteInt(p.Chunk.Z);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundForgetLevelChunkPacket(new ChunkPos(r.ReadInt(), r.ReadInt())));

    /// <summary>Modern forget level chunk: a packed ChunkPos long (z high, x low).</summary>
    public static readonly PacketCodec<ClientboundForgetLevelChunkPacket> ForgetLevelChunkV1_14 =
        PacketCodec<ClientboundForgetLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundForgetLevelChunkPacket p, PacketCodecContext _) => WriteChunkPos(ref w, p.Chunk),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundForgetLevelChunkPacket(ReadChunkPos(ref r)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareForgetLevelChunk(PacketBindings bindings)
    {
        // Protocols 107-404 use two plain integers; 1.14+ uses a packed ChunkPos long. Both forms are eight bytes but order their coordinates differently, so they require distinct codecs.
        bindings.Packet(WorldPackets.Clientbound.ForgetLevelChunk)
            .From(JavaEras.Combat, WorldStateCodecs.ForgetLevelChunkV1_9)
            .From(JavaEras.Palettes, WorldStateCodecs.ForgetLevelChunkV1_14);
    }
}
