using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Disconnect, reset chat, and transfer

    // Configuration disconnect matches the play packet's wire era for era. Protocol 764 carries a JSON string, protocols 765-769 carry network NBT with legacy click/hover shapes, and protocol 770 moves to modern interactions. A plain-text reason cannot distinguish these forms, but an interactive reason can.

    /// <summary>764 configuration disconnect: JSON-string reason, legacy interactions.</summary>
    public static PacketCodec<ClientboundConfigDisconnectPacket> DisconnectV1_20_2 { get; } =
        Disconnect(ComponentWire.V1_8);

    /// <summary>765-769 configuration disconnect: network-NBT reason, legacy interactions.</summary>
    public static PacketCodec<ClientboundConfigDisconnectPacket> DisconnectV1_20_3 { get; } =
        Disconnect(ComponentWire.V1_20_3);

    /// <summary>770+ configuration disconnect: network-NBT reason, modern interactions.</summary>
    public static PacketCodec<ClientboundConfigDisconnectPacket> DisconnectV1_21_5 { get; } =
        Disconnect(ComponentWire.V1_21_5);

    private static PacketCodec<ClientboundConfigDisconnectPacket> Disconnect(
        ComponentWire text) =>
        PacketCodec<ClientboundConfigDisconnectPacket>.Of(
            (ref PacketWriter w, ClientboundConfigDisconnectPacket p, PacketCodecContext _) => text.Write(ref w, p.Reason),
            (ref PacketReader r, PacketCodecContext _) => new ClientboundConfigDisconnectPacket(text.Read(ref r)),
            WireShape.Of("component", text.Form));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareDisconnectConfiguration(PacketBindings bindings)
    {
        // Three component eras, not one: JSON on 764, network NBT with legacy interactions on 765-769, network NBT with modern interactions from 770. See ConfigurationCodecs for the evidence.
        bindings.Packet(LoginFamilyPackets.Config.Disconnect)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.DisconnectV1_20_2)
            .From(JavaEras.ComponentNbtTransport, ConfigurationCodecs.DisconnectV1_20_3)
            .From(JavaEras.ModernComponents, ConfigurationCodecs.DisconnectV1_21_5);
    }
}
