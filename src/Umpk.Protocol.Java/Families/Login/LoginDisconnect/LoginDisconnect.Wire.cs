using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;

namespace Umpk.Protocol.Java.Codecs;

public static partial class LoginCodecs
{
    // Clientbound disconnect

    /// <summary>The 47-764 login disconnect: a JSON-string reason in the legacy component dialect (<c>clickEvent</c>/<c>hoverEvent</c>, hover body under <c>contents</c>). A pure literal uses the object form <c>{"text":"..."}</c>.</summary>
    /// <remarks>The literal form changes at 765 while the interaction dialect changes at 770, so protocols 765-769 require <see cref="LoginCodecs.DisconnectV1_20_3"/>. A pure literal is encoded as <c>{"text":"bare"}</c> through 764 and as <c>"bare"</c> from 765.</remarks>
    public static readonly PacketCodec<ClientboundLoginDisconnectPacket> DisconnectV1_8 = Disconnect(LoginWire.V1_8);

    /// <summary>The 765-769 login disconnect: still the legacy component dialect, but a pure literal reason already collapses to a bare JSON string.</summary>
    public static readonly PacketCodec<ClientboundLoginDisconnectPacket> DisconnectV1_20_3 = Disconnect(LoginWire.V1_20_3);

    /// <summary>The 770+ login disconnect: still a JSON string, in the modern component dialect (<c>click_event</c>/<c>hover_event</c>, hover body inlined). 1.21.5 renamed the fields.</summary>
    public static readonly PacketCodec<ClientboundLoginDisconnectPacket> DisconnectV1_21_5 = Disconnect(LoginWire.V1_21_5);

    private static PacketCodec<ClientboundLoginDisconnectPacket> Disconnect(LoginWire wire) =>
        PacketCodec<ClientboundLoginDisconnectPacket>.Of(
            (ref PacketWriter w, ClientboundLoginDisconnectPacket p, PacketCodecContext _) =>
            {
                // The login disconnect reason is a length-prefixed JSON string on every version, never NBT, because the client has no registries yet during login. TWO independent things move on that string: the component dialect at 1.21.5, and the pure-literal spelling at 1.20.3.
                w.WriteString(ComponentJson.ToJsonString(
                    p.Reason,
                    wire.ReasonLegacyComponents ? ComponentWireEra.Legacy : ComponentWireEra.Modern,
                    wire.ReasonCollapsesLiteral ? ComponentJsonLiteralForm.Collapsed : ComponentJsonLiteralForm.Object));
            },
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundLoginDisconnectPacket(ComponentJson.Parse(r.ReadString(262144),
                    wire.ReasonLegacyComponents ? ComponentWireEra.Legacy : ComponentWireEra.Modern)),
            WireShape.OfEra("login_disconnect", wire));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLoginDisconnect(PacketBindings bindings)
    {
        // The login disconnect reason is a JSON string on every version (login has no registry access, so it never got the NBT transport the play/configuration disconnects took in 1.20.3). TWO things move on that string and they move one release apart, which is why this timeline has three entries rather than two: the pure-literal spelling collapses to a bare JSON string at 1.20.3 (765), and the component dialect turns modern at 1.21.5 (770).
        bindings.Packet(LoginPackets.Clientbound.Disconnect)
            .From(JavaProtocols.V1_8, LoginCodecs.DisconnectV1_8)
            .From(JavaProtocols.V1_20_3, LoginCodecs.DisconnectV1_20_3)
            .From(JavaProtocols.V1_21_5, LoginCodecs.DisconnectV1_21_5);
    }
}
