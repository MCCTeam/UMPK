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
    /// <summary>Serverbound chunk batch received: a desired-chunks-per-tick float.</summary>
    public static readonly PacketCodec<ServerboundChunkBatchReceivedPacket> ChunkBatchReceivedV1_20_2 =
        PacketCodec<ServerboundChunkBatchReceivedPacket>.Of(
            static (ref PacketWriter w, ServerboundChunkBatchReceivedPacket p, PacketCodecContext _) => w.WriteFloat(p.DesiredChunksPerTick),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundChunkBatchReceivedPacket(r.ReadFloat()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChunkBatchReceived(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Serverbound.ChunkBatchReceived)
            .From(JavaEras.ConfigurationPhase, WorldStateCodecs.ChunkBatchReceivedV1_20_2);
    }
}
