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
    /// <summary>Modern chunks biomes: a list of (ChunkPos long, VarInt-prefixed biome buffer).</summary>
    public static readonly PacketCodec<ClientboundChunksBiomesPacket> ChunksBiomesV1_19_4 =
        PacketCodec<ClientboundChunksBiomesPacket>.Of(
            static (ref PacketWriter w, ClientboundChunksBiomesPacket p, PacketCodecContext _) =>
                w.WriteList(p.Chunks, static (ref PacketWriter ew, ChunkBiomeEntry e) =>
                {
                    WriteChunkPos(ref ew, e.Chunk);
                    ew.WriteByteArray(e.Buffer);
                }),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundChunksBiomesPacket(r.ReadList(static (ref PacketReader er) =>
                    new ChunkBiomeEntry(ReadChunkPos(ref er), er.ReadByteArray().ToArray()))));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChunksBiomes(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.ChunksBiomes)
            .From(JavaProtocols.V1_19_4, WorldStateCodecs.ChunksBiomesV1_19_4);
    }
}
