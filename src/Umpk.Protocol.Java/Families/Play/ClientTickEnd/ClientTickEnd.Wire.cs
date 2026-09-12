using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayCommonCodecs
{
    /// <summary>Client tick end: an empty body (<c>StreamCodec.unit</c>).</summary>
    public static readonly PacketCodec<ServerboundClientTickEndPacket> ClientTickEnd =
        PacketCodec<ServerboundClientTickEndPacket>.Of(
            static (ref PacketWriter _, ServerboundClientTickEndPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ServerboundClientTickEndPacket(),
            WireShape.Of("empty"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareClientTickEnd(PacketBindings bindings)
    {
        bindings.Packet(PlayPackets.Serverbound.ClientTickEnd)
            .From(JavaEras.WideIds, PlayCommonCodecs.ClientTickEnd);
    }
}
