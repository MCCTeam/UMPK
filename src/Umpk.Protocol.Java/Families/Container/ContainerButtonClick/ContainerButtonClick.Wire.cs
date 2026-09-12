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
    /// <summary>1.8 enchant-item: byte window id, byte button.</summary>
    public static PacketCodec<ServerboundContainerButtonClickPacket> ContainerButtonClickV1_8 { get; } =
        PacketCodec<ServerboundContainerButtonClickPacket>.Of(
            static (ref PacketWriter w, ServerboundContainerButtonClickPacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteByte((byte)p.ButtonId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundContainerButtonClickPacket(r.ReadByte(), r.ReadByte()));

    /// <summary>Modern container-button-click: VarInt container id, VarInt button id.</summary>
    public static PacketCodec<ServerboundContainerButtonClickPacket> ContainerButtonClickModern { get; } =
        PacketCodec<ServerboundContainerButtonClickPacket>.Of(
            static (ref PacketWriter w, ServerboundContainerButtonClickPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.ButtonId);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundContainerButtonClickPacket(r.ReadVarInt(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareContainerButtonClick(PacketBindings bindings)
    {
        // Both fields are bytes from 1.8 through 1.20.4: readByte containerId, readByte buttonId, verified at 1.14.4, 1.16.5, 1.17.1, 1.19, 1.20.4) and both become VarInts at 1.20.5, where the packet moved to a STREAM_CODEC of ByteBufCodecs.VAR_INT + VAR_INT (verified at 1.20.6, 1.21, 1.21.1). 1.21.2 swaps the first for ByteBufCodecs.CONTAINER_ID, which is itself a VarInt, so the wire does not change again. This was mis-bound at BOTH ends: 477-763 used the VarInt form on a byte band, and 766/767 used the byte form on a VarInt band. The 1.9-1.13.2 marker gap is pre-existing and untouched.
        bindings.Packet(ItemPackets.Serverbound.ContainerButtonClick)
            .From(JavaProtocols.V1_8, ContainerCodecs.ContainerButtonClickV1_8)
            .From(JavaProtocols.V1_20_5, ContainerCodecs.ContainerButtonClickModern);
    }
}
