using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>World map-item data codecs (1.8 and modern), including the color-patch encoders.</summary>
public static partial class WorldMapCodecs
{
    // The pre-1.13 icon byte packs the decoration type in the high nibble and rotation in the low nibble. Player markers use type 0, item frames use type 1, and banner decorations carry their type and rotation explicitly. Reversing the nibbles swaps each icon type and heading while still round-tripping byte-exactly, so consumers must observe the decoded fields to validate this packing.
    private static byte PackIconNibbles(MapIcon icon) => (byte)(((icon.Type & 0xF) << 4) | (icon.Rotation & 0xF));

    private static MapIcon UnpackIcon(byte packed, sbyte x, sbyte z) =>
        new((packed >> 4) & 0xF, x, z, (byte)(packed & 0xF), DisplayName: null);

    /// <summary>1.8 map: VarInt id, scale byte, VarInt icon count, per-icon (packed byte, x, z), then the pixel patch.</summary>
    public static readonly PacketCodec<ClientboundMapItemDataPacket> MapItemDataV1_8 =
        PacketCodec<ClientboundMapItemDataPacket>.Of(
            static (ref PacketWriter w, ClientboundMapItemDataPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.MapId);
                w.WriteByte(p.Scale);
                IReadOnlyList<MapIcon> icons = p.Icons ?? [];
                w.WriteVarInt(icons.Count);
                for (int i = 0; i < icons.Count; i++)
                {
                    MapIcon icon = icons[i];
                    w.WriteByte(PackIconNibbles(icon));
                    w.WriteSByte(icon.X);
                    w.WriteSByte(icon.Z);
                }

                WriteMapPatchLegacy(ref w, p.Patch);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int mapId = r.ReadVarInt();
                byte scale = (byte)r.ReadSByte();
                int iconCount = r.ReadVarInt();
                var icons = new MapIcon[iconCount];
                for (int i = 0; i < iconCount; i++)
                {
                    byte packed = r.ReadByte();
                    icons[i] = UnpackIcon(packed, r.ReadSByte(), r.ReadSByte());
                }

                MapPatch patch = ReadMapPatchLegacy(ref r);
                return new ClientboundMapItemDataPacket(mapId, scale, Locked: false, icons, patch, TrackingPosition: null);
            });

    /// <summary>1.9-1.12.2 (107-340) map: the 1.8 body with a <c>trackingPosition</c> BOOL inserted between the scale byte and the icon list. Versions 1.9 through 1.12.2 place the bool before the icon count, while 1.8 goes directly from the scale byte to the icon count. The icons keep the pre-1.13 packed nibble byte and carry no display name.</summary>
    /// <remarks>The extra bool is the whole difference, so binding the 1.8 codec here would consume the bool as the icon-count VarInt and then read icon triples out of the pixel patch: a decode that produces plausible garbage on a map with no icons and faults on one that has them. The 1.14 codec is equally wrong in the other direction (it expects a <c>locked</c> bool plus an OPTIONAL icon list and VarInt icon types).</remarks>
    public static readonly PacketCodec<ClientboundMapItemDataPacket> MapItemDataV1_9 =
        PacketCodec<ClientboundMapItemDataPacket>.Of(
            static (ref PacketWriter w, ClientboundMapItemDataPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.MapId);
                w.WriteByte(p.Scale);
                w.WriteBool(p.TrackingPosition ?? throw new ProtocolViolationException("A 1.9-1.13.2 map packet requires the tracking-position flag."));
                IReadOnlyList<MapIcon> icons = p.Icons ?? [];
                w.WriteVarInt(icons.Count);
                for (int i = 0; i < icons.Count; i++)
                {
                    MapIcon icon = icons[i];
                    w.WriteByte(PackIconNibbles(icon));
                    w.WriteSByte(icon.X);
                    w.WriteSByte(icon.Z);
                }

                WriteMapPatchLegacy(ref w, p.Patch);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int mapId = r.ReadVarInt();
                byte scale = (byte)r.ReadSByte();
                bool trackingPosition = r.ReadBool();
                int iconCount = r.ReadVarInt();
                var icons = new MapIcon[iconCount];
                for (int i = 0; i < iconCount; i++)
                {
                    byte packed = r.ReadByte();
                    icons[i] = UnpackIcon(packed, r.ReadSByte(), r.ReadSByte());
                }

                MapPatch patch = ReadMapPatchLegacy(ref r);
                return new ClientboundMapItemDataPacket(mapId, scale, Locked: false, icons, patch, trackingPosition);
            });

    /// <summary>1.13-1.13.2 (393-404) map: VarInt id, scale byte, <c>trackingPosition</c> bool, then a VarInt-counted icon list whose entries became <c>(VarInt type, byte x, byte z, byte rotation &amp; 15, optional JSON display name)</c>, then the pixel patch.</summary>
    /// <remarks>The icon list is still a bare VarInt-counted list here, NOT the optional list the 1.14 codec writes, and the display name is a JSON-string component rather than network NBT. Those are the two reasons the 1.14 codec cannot be reused on 393-404.</remarks>
    public static readonly PacketCodec<ClientboundMapItemDataPacket> MapItemDataV1_13 =
        PacketCodec<ClientboundMapItemDataPacket>.Of(
            static (ref PacketWriter w, ClientboundMapItemDataPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.MapId);
                w.WriteByte(p.Scale);
                w.WriteBool(p.TrackingPosition ?? throw new ProtocolViolationException("A 1.9-1.13.2 map packet requires the tracking-position flag."));
                w.WriteList(p.Icons ?? [], static (ref PacketWriter ew, MapIcon icon) =>
                {
                    ew.WriteVarInt(icon.Type);
                    ew.WriteSByte(icon.X);
                    ew.WriteSByte(icon.Z);
                    ew.WriteByte((byte)(icon.Rotation & 0xF));
                    ew.WriteOptional(icon.DisplayName, UiCodecShared.WriteLegacyComponent);
                });
                WriteMapPatchLegacy(ref w, p.Patch);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int mapId = r.ReadVarInt();
                byte scale = (byte)r.ReadSByte();
                bool trackingPosition = r.ReadBool();
                MapIcon[] icons = r.ReadList(static (ref PacketReader er) =>
                {
                    int type = er.ReadVarInt();
                    sbyte x = er.ReadSByte();
                    sbyte z = er.ReadSByte();
                    var rotation = (byte)(er.ReadByte() & 0xF);
                    Component? name = er.ReadOptional(UiCodecShared.ReadLegacyComponent);
                    return new MapIcon(type, x, z, rotation, name);
                });
                MapPatch patch = ReadMapPatchLegacy(ref r);
                return new ClientboundMapItemDataPacket(mapId, scale, Locked: false, icons, patch, trackingPosition);
            });

    /// <summary>477-754 (1.14-1.16.5) map: VarInt id, scale byte, <c>trackingPosition</c> bool, <c>locked</c> bool, then a BARE VarInt-counted decoration list and the pixel patch. Decoration names are JSON-string components.</summary>
    /// <remarks>Version 1.17 drops <c>trackingPosition</c> and wraps the decoration list in a present flag, in the same change. A later codec reads <c>trackingPosition</c> as <c>locked</c> and then the icon count as the list's present flag, so any map with more than one decoration desynchronised the frame and any map with none decoded a phantom empty list and then ran into the patch.</remarks>
    public static readonly PacketCodec<ClientboundMapItemDataPacket> MapItemDataV1_14 =
        MakeMapItemData(hasTrackingPosition: true, ComponentWire.V1_8);

    /// <summary>755-764 (1.17-1.20.2) map: no <c>trackingPosition</c>, an OPTIONAL decoration list, and decoration names still carried as JSON-string components (network NBT arrives at 1.20.3).</summary>
    /// <remarks>The list-presence flag follows the locked bool. Each optional display name is a JSON component. Version 1.20.3 changes that component transport to network NBT.</remarks>
    public static readonly PacketCodec<ClientboundMapItemDataPacket> MapItemDataV1_17 =
        MakeMapItemData(hasTrackingPosition: false, ComponentWire.V1_8);

    /// <summary>765-769 (1.20.3-1.21.4) map: the 1.17 body with decoration names as network NBT carrying the LEGACY click/hover shapes. The interaction dialect is a separate boundary and it moves at 1.21.5.</summary>
    public static readonly PacketCodec<ClientboundMapItemDataPacket> MapItemDataV1_20_3 =
        MakeMapItemData(hasTrackingPosition: false, ComponentWire.V1_20_3);

    /// <summary>Modern map data (770+): VarInt id, scale byte, locked bool, optional decoration list, then the pixel patch. Decoration type is a registry id, the name an optional network-NBT component with modern interactions.</summary>
    public static readonly PacketCodec<ClientboundMapItemDataPacket> MapItemDataV1_21_5 =
        MakeMapItemData(hasTrackingPosition: false, ComponentWire.V1_21_5);

    // The 477+ family. Two era switches and nothing else: whether the tracking-position bool is present (and, inseparably, whether the decoration list is bare or optional, both of which moved at 1.17), and which component dialect the decoration name uses.
    private static PacketCodec<ClientboundMapItemDataPacket> MakeMapItemData(
        bool hasTrackingPosition, ComponentWire label) =>
        PacketCodec<ClientboundMapItemDataPacket>.Of(
            (ref PacketWriter w, ClientboundMapItemDataPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.MapId);
                w.WriteByte(p.Scale);
                if (hasTrackingPosition)
                    w.WriteBool(p.TrackingPosition ?? throw new ProtocolViolationException("A 1.14-1.16.5 map packet requires the tracking-position flag."));

                w.WriteBool(p.Locked);

                WriterAction<MapIcon> writeIcon = (ref PacketWriter ew, MapIcon icon) =>
                {
                    ew.WriteVarInt(icon.Type);
                    ew.WriteSByte(icon.X);
                    ew.WriteSByte(icon.Z);
                    ew.WriteByte((byte)(icon.Rotation & 0xF));
                    ew.WriteOptional(icon.DisplayName, label.Write);
                };

                if (hasTrackingPosition)
                    w.WriteList(p.Icons ?? [], writeIcon);

                else
                    w.WriteOptional(
                        p.Icons is null ? null : (IReadOnlyList<MapIcon>)p.Icons,
                        (ref PacketWriter ow, IReadOnlyList<MapIcon> icons) => ow.WriteList(icons, writeIcon));

                WriteMapPatchModern(ref w, p.Patch);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int mapId = r.ReadVarInt();
                byte scale = r.ReadByte();
                bool? trackingPosition = hasTrackingPosition ? r.ReadBool() : null;
                bool locked = r.ReadBool();

                ReaderFunc<MapIcon> readIcon = (ref PacketReader er) =>
                {
                    int type = er.ReadVarInt();
                    sbyte x = er.ReadSByte();
                    sbyte z = er.ReadSByte();
                    var rotation = (byte)(er.ReadByte() & 0xF);
                    Component? name = er.ReadOptional(label.Read);
                    return new MapIcon(type, x, z, rotation, name);
                };

                IReadOnlyList<MapIcon>? icons = hasTrackingPosition
                    ? r.ReadList(readIcon)
                    : r.ReadOptional((ref PacketReader or) => (IReadOnlyList<MapIcon>)or.ReadList(readIcon));

                MapPatch patch = ReadMapPatchModern(ref r);
                return new ClientboundMapItemDataPacket(mapId, scale, locked, icons, patch, trackingPosition);
            },
            WireShape.Of(
                hasTrackingPosition
                    ? "varint,byte,bool,bool,varint*icon,patch"
                    : "varint,byte,bool,opt(varint*icon),patch",
                label.Form));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMapItemData(PacketBindings bindings)
    {
        // Seven era forms. Below 1.14: 47 has no tracking-position bool, 107-340 adds it, and 393-404 rewrites the icon entry (VarInt type, separate rotation byte, optional JSON name). From 1.14 the packet also gains a locked bool, so 477-754 carries BOTH bools and still a bare VarInt-counted icon list; 1.17 drops tracking-position and makes the icon list optional in one change; 1.20.3 moves the icon name from a JSON string to network NBT; 1.21.5 moves its interaction dialect. Binding the 1.21.5 form to 1.14-1.16.5 reads tracking-position as locked and the icon count as a presence flag, desynchronizing the frame.
        bindings.Packet(WorldPackets.Clientbound.MapItemData)
            .From(JavaProtocols.V1_8, WorldMapCodecs.MapItemDataV1_8)
            .From(JavaProtocols.V1_9, WorldMapCodecs.MapItemDataV1_9)
            .From(JavaProtocols.V1_13, WorldMapCodecs.MapItemDataV1_13)
            .From(JavaProtocols.V1_14, WorldMapCodecs.MapItemDataV1_14)
            .From(JavaProtocols.V1_17, WorldMapCodecs.MapItemDataV1_17)
            .From(JavaProtocols.V1_20_3, WorldMapCodecs.MapItemDataV1_20_3)
            .From(JavaProtocols.V1_21_5, WorldMapCodecs.MapItemDataV1_21_5)
            .AliasedAs(Identifier.Minecraft("map"));
    }
}
