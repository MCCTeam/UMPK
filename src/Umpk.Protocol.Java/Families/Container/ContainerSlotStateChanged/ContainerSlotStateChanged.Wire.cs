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
    /// <summary>Modern container-slot-state-changed: VarInt slot, VarInt container id, bool new state.</summary>
    public static PacketCodec<ServerboundContainerSlotStateChangedPacket> ContainerSlotStateChangedModern { get; } =
        PacketCodec<ServerboundContainerSlotStateChangedPacket>.Of(
            static (ref PacketWriter w, ServerboundContainerSlotStateChangedPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.SlotId);
                w.WriteVarInt(p.ContainerId);
                w.WriteBool(p.NewState);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundContainerSlotStateChangedPacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareContainerSlotStateChanged(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Serverbound.ContainerSlotStateChanged)
            .From(JavaEras.ComponentNbtTransport, ContainerCodecs.ContainerSlotStateChangedModern);
    }
}
