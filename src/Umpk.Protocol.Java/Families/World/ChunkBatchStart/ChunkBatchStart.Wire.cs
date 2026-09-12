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
    /// <summary>Chunk batch start: empty payload.</summary>
    public static readonly PacketCodec<ClientboundChunkBatchStartPacket> ChunkBatchStartV1_20_2 =
        PacketCodec<ClientboundChunkBatchStartPacket>.Of(
            static (ref PacketWriter _, ClientboundChunkBatchStartPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ClientboundChunkBatchStartPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChunkBatchStart(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.ChunkBatchStart)
            .From(JavaEras.ConfigurationPhase, WorldStateCodecs.ChunkBatchStartV1_20_2);
    }
}
