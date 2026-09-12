using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class ContainerCodecs
{
    /// <summary>1.8 open-window: ubyte window id, string type, chat title, ubyte slots, optional int entity.</summary>
    public static PacketCodec<ClientboundOpenScreenPacket> OpenScreenV1_8 { get; } =
        PacketCodec<ClientboundOpenScreenPacket>.Of(
            static (ref PacketWriter w, ClientboundOpenScreenPacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteString(p.LegacyType ?? string.Empty);
                // 1.8 window titles are JSON chat strings, not NBT.
                w.WriteString(ComponentJson.ToJsonString(p.Title, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), 32767);
                w.WriteByte((byte)p.LegacySlotCount);
                if (string.Equals(p.LegacyType, "EntityHorse", StringComparison.Ordinal))
                    w.WriteInt(p.LegacyEntityId ?? 0);

            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadByte();
                string type = r.ReadString();
                Component title = ComponentJson.Parse(r.ReadString(32767), ComponentWireEra.Legacy);
                int slots = r.ReadByte();
                int? entity = string.Equals(type, "EntityHorse", StringComparison.Ordinal) ? r.ReadInt() : null;
                return new ClientboundOpenScreenPacket(id, -1, title, type, slots, entity);
            });

    /// <summary>1.21.5+ open-screen: VarInt container id, VarInt menu type registry id, network-NBT component title in the MODERN interaction dialect. Pre-1.20.3 titles are JSON strings (<see cref="OpenScreenV1_14"/>), 1.20.3-1.21.1 use the byte-container-id legacy-era NBT component (<see cref="OpenScreenV1_20_3"/>), and 1.21.2-1.21.4 use the VarInt container id with the LEGACY dialect (<see cref="OpenScreenV1_21_2"/>).</summary>
    public static PacketCodec<ClientboundOpenScreenPacket> OpenScreenModern { get; } =
        MakeOpenScreenModern(ComponentWireEra.Modern);

    /// <summary>768/769 (1.21.2-1.21.4) open-screen. 1.21.2 widened the container id to a VarInt, but the title's interaction dialect only modernizes at 1.21.5 (the earlier form names the style fields <c>clickEvent</c>/<c>hoverEvent</c>), so this band needs the legacy dialect with the modern header. Binding <see cref="OpenScreenModern"/> here wrote <c>click_event</c> for any container title that carried an interaction.</summary>
    public static PacketCodec<ClientboundOpenScreenPacket> OpenScreenV1_21_2 { get; } =
        MakeOpenScreenModern(ComponentWireEra.Legacy);

    private static PacketCodec<ClientboundOpenScreenPacket> MakeOpenScreenModern(ComponentWireEra era) =>
        PacketCodec<ClientboundOpenScreenPacket>.Of(
            (ref PacketWriter w, ClientboundOpenScreenPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.MenuTypeId);
                ItemCodecPrimitives.WriteNetworkComponent(ref w, p.Title, era);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                int menu = r.ReadVarInt();
                Component title = ItemCodecPrimitives.ReadNetworkComponent(ref r, era);
                return new ClientboundOpenScreenPacket(id, menu, title, null, 0, null);
            });

    /// <summary>477-764 (1.14-1.20.2) open-screen: VarInt id, VarInt menu type, JSON-string title (legacy click/hover). The title stays a JSON string across this whole range (readComponent = readUtf, up to 262144 chars) and flips to NBT at 1.20.3 (<see cref="OpenScreenV1_20_3"/>). Verified vs The fields are two VarInts followed by a JSON component.</summary>
    public static PacketCodec<ClientboundOpenScreenPacket> OpenScreenV1_14 { get; } =
        PacketCodec<ClientboundOpenScreenPacket>.Of(
            static (ref PacketWriter w, ClientboundOpenScreenPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.MenuTypeId);
                w.WriteString(ComponentJson.ToJsonString(p.Title, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), 262144);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                int menu = r.ReadVarInt();
                Component title = ComponentJson.Parse(r.ReadString(262144), ComponentWireEra.Legacy);
                return new ClientboundOpenScreenPacket(id, menu, title, null, 0, null);
            });

    /// <summary>765-767 open-screen: VarInt id, VarInt menu type, NBT title (legacy click/hover).</summary>
    public static PacketCodec<ClientboundOpenScreenPacket> OpenScreenV1_20_3 { get; } =
        PacketCodec<ClientboundOpenScreenPacket>.Of(
            static (ref PacketWriter w, ClientboundOpenScreenPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.MenuTypeId);
                w.WriteComponent(p.Title, ComponentWireEra.Legacy, Nbt.NbtWireFormat.JavaRootTagOrString);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                int menu = r.ReadVarInt();
                Component title = r.ReadComponent(ComponentWireEra.Legacy, Nbt.NbtWireFormat.JavaRootTagOrString);
                return new ClientboundOpenScreenPacket(id, menu, title, null, 0, null);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareOpenScreen(PacketBindings bindings)
    {
        // Open-screen title wire history: a JSON string from 1.14 through 1.20.2, legacy-era NBT at 1.20.3, and the modern network-NBT component from 1.21.2. The open-window wire is identical from 1.8 through 1.13.2 (unsigned byte window id, string type capped at 32, chat-JSON title, unsigned byte slot count, and an int entity id only when the type is "EntityHorse"). The V1_8 codec therefore runs through 1.13.2.
        bindings.Packet(ItemPackets.Clientbound.OpenScreen)
            .From(JavaProtocols.V1_8, ContainerCodecs.OpenScreenV1_8)
            .From(JavaProtocols.V1_14, ContainerCodecs.OpenScreenV1_14)
            .From(JavaProtocols.V1_20_3, ContainerCodecs.OpenScreenV1_20_3)
            .From(JavaProtocols.V1_21_2, ContainerCodecs.OpenScreenV1_21_2)
            .From(JavaProtocols.V1_21_5, ContainerCodecs.OpenScreenModern);
    }
}
