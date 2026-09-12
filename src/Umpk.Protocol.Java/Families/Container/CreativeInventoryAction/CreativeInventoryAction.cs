using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Serverbound
    {
        /// <summary>The 1.8 creative-set-slot (<c>minecraft:creative_inventory_action</c>).</summary>
        public static readonly PacketType<ServerboundSetCreativeModeSlotPacket> LegacyCreativeSlot =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("creative_inventory_action"));
    }
}
