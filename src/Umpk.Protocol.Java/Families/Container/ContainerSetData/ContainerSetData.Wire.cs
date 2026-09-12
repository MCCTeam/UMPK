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
    /// <summary>1.8 window-property: ubyte window id, short property, short value.</summary>
    public static PacketCodec<ClientboundContainerSetDataPacket> ContainerSetDataV1_8 { get; } =
        PacketCodec<ClientboundContainerSetDataPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetDataPacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort(p.PropertyId);
                w.WriteShort(p.Value);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundContainerSetDataPacket(r.ReadByte(), r.ReadShort(), r.ReadShort()));

    /// <summary>Modern container-set-data: VarInt container id, short property, short value.</summary>
    public static PacketCodec<ClientboundContainerSetDataPacket> ContainerSetDataModern { get; } =
        PacketCodec<ClientboundContainerSetDataPacket>.Of(
            static (ref PacketWriter w, ClientboundContainerSetDataPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteShort(p.PropertyId);
                w.WriteShort(p.Value);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundContainerSetDataPacket(r.ReadVarInt(), r.ReadShort(), r.ReadShort()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareContainerSetData(PacketBindings bindings)
    {
        // Byte container id through 1.21.1, VarInt from 1.21.2 - see the container_close note above. Byte container id plus short property and value, unchanged from protocol 107 through 1.21.1. Protocol 47 spells the same body minecraft:craft_progress_bar, so the era begins at 47.
        bindings.Packet(ItemPackets.Clientbound.ContainerSetData)
            .From(JavaProtocols.V1_8, ContainerCodecs.ContainerSetDataV1_8)
            .From(JavaProtocols.V1_21_2, ContainerCodecs.ContainerSetDataModern)
            .AliasedAs(Identifier.Minecraft("craft_progress_bar"));
    }
}
