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
    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCreativeInventoryAction(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Serverbound.LegacyCreativeSlot)
            .From(JavaProtocols.V1_8, ContainerCodecs.CreativeSlotV1_8);
    }
}
