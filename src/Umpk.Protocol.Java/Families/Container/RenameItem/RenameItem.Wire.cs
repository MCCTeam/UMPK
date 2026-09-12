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
    /// <summary>Anvil rename (1.13+): the new item name string. Constant across eras.</summary>
    public static PacketCodec<ServerboundRenameItemPacket> RenameItemV1_13 { get; } =
        PacketCodec<ServerboundRenameItemPacket>.Of(
            static (ref PacketWriter w, ServerboundRenameItemPacket p, PacketCodecContext _) => w.WriteString(p.Name),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundRenameItemPacket(r.ReadString()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRenameItem(PacketBindings bindings)
    {
        // Pre-1.13 anvil rename used the "MC|ItemName" plugin channel; the dedicated packet is 1.13+.
        bindings.Packet(ItemPackets.Serverbound.RenameItem)
            .From(JavaEras.Flattening, ContainerCodecs.RenameItemV1_13);
    }
}
