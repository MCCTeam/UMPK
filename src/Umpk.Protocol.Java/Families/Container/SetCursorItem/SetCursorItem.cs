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
        /// <summary>The cursor (carried) item (<c>minecraft:set_cursor_item</c>, 1.21.2+).</summary>
        public static readonly PacketType<ClientboundSetCursorItemPacket> SetCursorItem =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_cursor_item"));
    }
}

/// <summary>The cursor (carried) stack (1.21.2+).</summary>
/// <param name="Item">The cursor stack.</param>
public sealed record ClientboundSetCursorItemPacket(ItemStack Item) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.SetCursorItem;
}
