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
    /// <summary>Chunk batch finished: a VarInt batch size.</summary>
    public static readonly PacketCodec<ClientboundChunkBatchFinishedPacket> ChunkBatchFinishedV1_20_2 =
        PacketCodec<ClientboundChunkBatchFinishedPacket>.Of(
            static (ref PacketWriter w, ClientboundChunkBatchFinishedPacket p, PacketCodecContext _) => w.WriteVarInt(p.BatchSize),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundChunkBatchFinishedPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChunkBatchFinished(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.ChunkBatchFinished)
            .From(JavaEras.ConfigurationPhase, WorldStateCodecs.ChunkBatchFinishedV1_20_2);
    }
}
