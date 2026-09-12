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
    // disconnect
    //
    // One component, three encodings. The transport boundary (JSON string -> network NBT) is 765 and
    // the interaction boundary (legacy clickEvent/hoverEvent -> modern click_event/hover_event) is 770;
    // they are independent, which is why there are three eras rather than two. Both boundaries are the repo-wide component boundaries documented in UiCodecShared.

    /// <summary>47-764 play disconnect: JSON-string reason, legacy interactions.</summary>
    public static PacketCodec<ClientboundDisconnectPacket> DisconnectV1_8 { get; } =
        Disconnect(ComponentWire.V1_8);

    /// <summary>765-769 play disconnect: network-NBT reason, legacy interactions.</summary>
    public static PacketCodec<ClientboundDisconnectPacket> DisconnectV1_20_3 { get; } =
        Disconnect(ComponentWire.V1_20_3);

    /// <summary>770+ play disconnect: network-NBT reason, modern interactions.</summary>
    public static PacketCodec<ClientboundDisconnectPacket> DisconnectV1_21_5 { get; } =
        Disconnect(ComponentWire.V1_21_5);

    private static PacketCodec<ClientboundDisconnectPacket> Disconnect(
        ComponentWire text) =>
        PacketCodec<ClientboundDisconnectPacket>.Of(
            (ref PacketWriter w, ClientboundDisconnectPacket p, PacketCodecContext _) => text.Write(ref w, p.Reason),
            (ref PacketReader r, PacketCodecContext _) => new ClientboundDisconnectPacket(text.Read(ref r)),
            WireShape.Of("component", text.Form));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareDisconnectPlay(PacketBindings bindings)
    {
        // disconnect Three component eras. From 1.20.2 this is byte-identical to the configuration-phase disconnect.
        bindings.Packet(PlayPackets.Clientbound.Disconnect)
            .From(JavaProtocols.V1_8, PlayCommonCodecs.DisconnectV1_8)
            .From(JavaProtocols.V1_20_3, PlayCommonCodecs.DisconnectV1_20_3)
            .From(JavaProtocols.V1_21_5, PlayCommonCodecs.DisconnectV1_21_5);
    }
}
