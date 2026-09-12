using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Clientbound
    {
        /// <summary>One container slot (<c>minecraft:set_slot</c> / <c>container_set_slot</c>).</summary>
        public static readonly PacketType<ClientboundContainerSetSlotPacket> ContainerSetSlot =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("container_set_slot"));
    }
}
