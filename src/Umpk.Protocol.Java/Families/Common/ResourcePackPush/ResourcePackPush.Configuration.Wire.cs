using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Resource pack push, pop, and response

    // 1.20.2 carries one configuration resource-pack packet, minecraft:resource_pack, with url + hash + required + optional prompt and NO pack uuid; 1.20.3 split it into resource_pack_push / resource_pack_pop and gave the push a leading uuid. The prompt follows the component transport of its version, so it is a JSON string on 764 and network NBT from 765, and its interaction dialect moves at 770 like every other component. These are the same three eras the play-phase copies in ResourcePackCodecs already carry.

    /// <summary>764 configuration resource-pack push (<c>minecraft:resource_pack</c>): url, hash, required flag, optional JSON-string prompt, and no UUID. The packet record's <c>Id</c> is <see cref="Guid.Empty"/> on this era because the wire has no field for it.</summary>
    public static PacketCodec<ClientboundConfigResourcePackPushPacket> ResourcePackPushV1_20_2 { get; } =
        MakeResourcePackPush(hasId: false, ComponentWire.V1_8);

    /// <summary>765-769 configuration resource-pack push: uuid + network-NBT prompt, legacy interactions.</summary>
    public static PacketCodec<ClientboundConfigResourcePackPushPacket> ResourcePackPushV1_20_3 { get; } =
        MakeResourcePackPush(hasId: true, ComponentWire.V1_20_3);

    /// <summary>770+ configuration resource-pack push: uuid + network-NBT prompt, modern interactions.</summary>
    public static PacketCodec<ClientboundConfigResourcePackPushPacket> ResourcePackPushV1_21_5 { get; } =
        MakeResourcePackPush(hasId: true, ComponentWire.V1_21_5);

    private static PacketCodec<ClientboundConfigResourcePackPushPacket> MakeResourcePackPush(
        bool hasId, ComponentWire text) =>
        PacketCodec<ClientboundConfigResourcePackPushPacket>.Of(
            (ref PacketWriter w, ClientboundConfigResourcePackPushPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteResourcePackPush(
                    ref w, new ResourcePackPushFields(p.Id, p.Url, p.Hash, p.Required, p.Prompt), hasId, text),
            (ref PacketReader r, PacketCodecContext _) =>
            {
                ResourcePackPushFields f = CommonPayloads.ReadResourcePackPush(ref r, hasId, text);
                return new ClientboundConfigResourcePackPushPacket(f.Id, f.Url, f.Hash, f.Required, f.Prompt);
            },
            WireShape.Of(
                hasId ? "uuid,string,string,bool,opt_component" : "string,string,bool,opt_component",
                text.Form));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareResourcePackPushConfiguration(PacketBindings bindings)
    {
        // 1.20.2 has ONE configuration resource-pack packet, spelled minecraft:resource_pack, with no pack uuid and a JSON-string prompt; 1.20.3 split it into push/pop and gave the push a uuid. The prompt's interaction dialect moves again at protocol 770.
        bindings.Packet(LoginFamilyPackets.Config.ResourcePackPush)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.ResourcePackPushV1_20_2)
            .From(JavaEras.ComponentNbtTransport, ConfigurationCodecs.ResourcePackPushV1_20_3)
            .From(JavaEras.ModernComponents, ConfigurationCodecs.ResourcePackPushV1_21_5)
            .AliasedAs(Identifier.Minecraft("resource_pack"), JavaEras.ConfigurationPhase, JavaEras.ConfigurationPhase);
    }
}
