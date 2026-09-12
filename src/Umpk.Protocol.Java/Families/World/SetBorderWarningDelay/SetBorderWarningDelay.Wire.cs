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
    /// <summary>Set border warning delay: a VarInt.</summary>
    public static readonly PacketCodec<ClientboundSetBorderWarningDelayPacket> SetBorderWarningDelayV1_17 =
        PacketCodec<ClientboundSetBorderWarningDelayPacket>.Of(
            static (ref PacketWriter w, ClientboundSetBorderWarningDelayPacket p, PacketCodecContext _) => w.WriteVarInt(p.WarningDelay),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundSetBorderWarningDelayPacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetBorderWarningDelay(PacketBindings bindings)
    {
        bindings.Packet(WorldPackets.Clientbound.SetBorderWarningDelay)
            .From(JavaEras.Caves, WorldBorderCodecs.SetBorderWarningDelayV1_17);
    }
}
