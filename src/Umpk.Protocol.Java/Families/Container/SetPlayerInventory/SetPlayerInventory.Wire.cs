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
    /// <summary>set-player-inventory (1.21.2+): VarInt slot, item stack.</summary>
    public static PacketCodec<ClientboundSetPlayerInventoryPacket> SetPlayerInventory770 { get; } = MakeSetPlayerInventory(Table770);

    /// <summary>26.2 set-player-inventory.</summary>
    public static PacketCodec<ClientboundSetPlayerInventoryPacket> SetPlayerInventory776 { get; } = MakeSetPlayerInventory(Table776);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetPlayerInventory(PacketBindings bindings)
    {
        PacketTimelineBuilder<ClientboundSetPlayerInventoryPacket> playerInventory =
            bindings.Packet(ItemPackets.Clientbound.SetPlayerInventory);

        foreach ((int protocol, string era, ItemComponentTable table) in ItemPacketCodecShared.ComponentEras)
            playerInventory.From(
                protocol,
                ItemPacketCodecShared.MakeSetPlayerInventory(table),
                $"MakeSetPlayerInventory({era})");

    }
}
