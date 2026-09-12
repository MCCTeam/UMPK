using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ResourcePackCodecs
{
    /// <summary>Resource pack push, 765-769 (1.20.3-1.21.4): uuid, url, hash, required flag, optional prompt. The packet arrives at 1.20.3 already carrying a network-NBT prompt, so the transport never moves inside this band; only the prompt's click/hover interaction era does, and that moves at 1.21.5.</summary>
    public static readonly PacketCodec<ClientboundResourcePackPushPacket> ResourcePackPushV1_20_3 =
        MakeResourcePackPush(ComponentWire.V1_20_3, withUuid: true);

    /// <summary>Resource pack push (770/776): the same body with modern prompt interactions.</summary>
    public static readonly PacketCodec<ClientboundResourcePackPushPacket> ResourcePackPushV1_21_5 =
        MakeResourcePackPush(ComponentWire.V1_21_5, withUuid: true);

    private static PacketCodec<ClientboundResourcePackPushPacket> MakeResourcePackPush(
        ComponentWire text, bool withUuid) =>
        PacketCodec<ClientboundResourcePackPushPacket>.Of(
            (ref PacketWriter w, ClientboundResourcePackPushPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackPush(
                    ref w, new ResourcePackPushFields(p.Id, p.Url, p.Hash, p.Required, p.Prompt), withUuid, text),
            (ref PacketReader r, PacketCodecContext _) =>
            {
                ResourcePackPushFields f = CommonPayloads.ReadResourcePackPush(ref r, withUuid, text);
                return new ClientboundResourcePackPushPacket(f.Id, f.Url, f.Hash, f.Required, f.Prompt);
            },
            WireShape.Of(
                withUuid ? "uuid,string,string,bool,opt_component" : "string,string,bool,opt_component",
                text.Form));

    /// <summary>Resource pack send, 477-754 (1.14-1.16.5): a url and a hash, and nothing else. Decoded into the modern push record with an empty pack uuid so a single applier path answers every era; the pack uuid only exists from 1.20.3 and the era codec is what decides whether it is on the wire.</summary>
    /// <remarks>The payload contains the same two strings as protocols 47-404. A server running <c>require-resource-pack</c> kicks a client that never answers.</remarks>
    public static readonly PacketCodec<ClientboundResourcePackPushPacket> ResourcePackPushV1_14 =
        PacketCodec<ClientboundResourcePackPushPacket>.Of(
            static (ref PacketWriter w, ClientboundResourcePackPushPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Url);
                w.WriteString(p.Hash, 40);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundResourcePackPushPacket(Guid.Empty, r.ReadString(), r.ReadString(40), Required: false, Prompt: null),
            WireShape.Of("string,string"));

    /// <summary>Resource pack send, 755-764 (1.17-1.20.2): url, hash, a required flag, and an optional prompt component carried as a JSON string. Still no pack uuid: that arrives with the 1.20.3 push/pop split.</summary>
    /// <remarks>The payload reads <c>readUtf(); readUtf(40); readBoolean();</c> then an optional component. The component is JSON-string component on every protocol through 764; network NBT only arrives at 765.</remarks>
    public static readonly PacketCodec<ClientboundResourcePackPushPacket> ResourcePackPushV1_17 =
        MakeResourcePackPush(ComponentWire.V1_8, withUuid: false);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResourcePackPushPlay(PacketBindings bindings)
    {
        // The optional prompt is a component: NBT for the whole 765+ range, legacy interactions to 769.
        bindings.Packet(UiPackets.Clientbound.ResourcePackPush)
            .From(JavaEras.ComponentNbtTransport, ResourcePackCodecs.ResourcePackPushV1_20_3)
            .From(JavaEras.ModernComponents, ResourcePackCodecs.ResourcePackPushV1_21_5);
    }
}
