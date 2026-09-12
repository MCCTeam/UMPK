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
    /// <summary>set-cursor-item (1.21.2+): item stack (optional stream codec).</summary>
    public static PacketCodec<ClientboundSetCursorItemPacket> SetCursorItem770 { get; } = MakeSetCursorItem(Table770);

    /// <summary>26.2 set-cursor-item.</summary>
    public static PacketCodec<ClientboundSetCursorItemPacket> SetCursorItem776 { get; } = MakeSetCursorItem(Table776);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetCursorItem(PacketBindings bindings)
    {
        PacketTimelineBuilder<ClientboundSetCursorItemPacket> cursorItem =
            bindings.Packet(ItemPackets.Clientbound.SetCursorItem);

        foreach ((int protocol, string era, ItemComponentTable table) in ItemPacketCodecShared.ComponentEras)
            cursorItem.From(protocol, ItemPacketCodecShared.MakeSetCursorItem(table), $"MakeSetCursorItem({era})");

    }
}
