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
    /// <summary>1.8 clientbound close: byte window id.</summary>
    public static PacketCodec<ClientboundContainerClosePacket> ContainerCloseClientV1_8 { get; } =
        PacketCodec<ClientboundContainerClosePacket>.Of(
            static (ref PacketWriter w, ClientboundContainerClosePacket p, PacketCodecContext _) => w.WriteByte((byte)p.ContainerId),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundContainerClosePacket(r.ReadByte()));

    /// <summary>Modern clientbound close: VarInt container id.</summary>
    public static PacketCodec<ClientboundContainerClosePacket> ContainerCloseClientModern { get; } =
        PacketCodec<ClientboundContainerClosePacket>.Of(
            static (ref PacketWriter w, ClientboundContainerClosePacket p, PacketCodecContext _) => w.WriteVarInt(p.ContainerId),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundContainerClosePacket(r.ReadVarInt()));

    /// <summary>1.8 serverbound close: byte window id.</summary>
    public static PacketCodec<ServerboundContainerClosePacket> ContainerCloseServerV1_8 { get; } =
        PacketCodec<ServerboundContainerClosePacket>.Of(
            static (ref PacketWriter w, ServerboundContainerClosePacket p, PacketCodecContext _) => w.WriteByte((byte)p.ContainerId),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundContainerClosePacket(r.ReadByte()));

    /// <summary>Modern serverbound close: VarInt container id.</summary>
    public static PacketCodec<ServerboundContainerClosePacket> ContainerCloseServerModern { get; } =
        PacketCodec<ServerboundContainerClosePacket>.Of(
            static (ref PacketWriter w, ServerboundContainerClosePacket p, PacketCodecContext _) => w.WriteVarInt(p.ContainerId),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundContainerClosePacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareContainerClose(PacketBindings bindings)
    {
        // The container id is an unsigned byte through 1.21.1 (767) and becomes a VarInt at 1.21.2. Byte and VarInt agree for ids 0..127, but the timeline must still bind the declared wire type. The byte body is unchanged from protocol 47 through 767. Protocol 47 differs only in packet name: the dataset spells it minecraft:close_screen and carries the same unsigned byte.
        bindings.Packet(ItemPackets.Clientbound.ContainerClose)
            .From(JavaProtocols.V1_8, ContainerCodecs.ContainerCloseClientV1_8)
            .From(JavaProtocols.V1_21_2, ContainerCodecs.ContainerCloseClientModern)
            .AliasedAs(Identifier.Minecraft("close_screen"));

        // Byte container id through 1.21.1, VarInt from 1.21.2 - see the container_close note above.
        bindings.Packet(ItemPackets.Serverbound.ContainerClose)
            .From(JavaProtocols.V1_8, ContainerCodecs.ContainerCloseServerV1_8)
            .From(JavaProtocols.V1_21_2, ContainerCodecs.ContainerCloseServerModern);
    }
}
