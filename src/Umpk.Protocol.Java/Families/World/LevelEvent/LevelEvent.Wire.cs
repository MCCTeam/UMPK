using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldEffectCodecs
{
    /// <summary>1.8 effect: int effect id, packed BlockPos, int data, bool disable-relative-volume.</summary>
    public static readonly PacketCodec<ClientboundLevelEventPacket> LevelEventV1_8 = MakeLevelEvent(BlockPosLayout.PrePacked114);

    /// <summary>Modern level event: int type, packed BlockPos, int data, bool global.</summary>
    public static readonly PacketCodec<ClientboundLevelEventPacket> LevelEventV1_14 = MakeLevelEvent(BlockPosLayout.Packed114);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLevelEvent(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.LevelEvent)
            .From(JavaProtocols.V1_8, WorldEffectCodecs.LevelEventV1_8)
            .From(JavaProtocols.V1_14, WorldEffectCodecs.LevelEventV1_14);
    }
}
