using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldBorderCodecs
{
    /// <summary>Modern initialize border: full center / size / lerp / warning block.</summary>
    public static readonly PacketCodec<ClientboundInitializeBorderPacket> InitializeBorderV1_17 =
        PacketCodec<ClientboundInitializeBorderPacket>.Of(
            static (ref PacketWriter w, ClientboundInitializeBorderPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.CenterX);
                w.WriteDouble(p.CenterZ);
                w.WriteDouble(p.OldSize);
                w.WriteDouble(p.NewSize);
                w.WriteVarLong(p.LerpTime);
                w.WriteVarInt(p.NewAbsoluteMaxSize);
                w.WriteVarInt(p.WarningBlocks);
                w.WriteVarInt(p.WarningTime);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundInitializeBorderPacket(
                r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(),
                r.ReadVarLong(), r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareInitializeBorder(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.InitializeBorder)
            .From(JavaEras.Caves, WorldBorderCodecs.InitializeBorderV1_17);
    }
}
