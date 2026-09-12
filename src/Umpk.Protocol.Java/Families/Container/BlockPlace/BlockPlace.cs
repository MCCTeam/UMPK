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
        /// <summary>The 1.8 block placement (<c>minecraft:block_place</c>) carrying a held item slot.</summary>
        public static readonly PacketType<ServerboundLegacyBlockPlacePacket> LegacyBlockPlace =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("block_place"));
    }
}

/// <summary>The 1.8 block placement carrying a held item slot and byte cursor coordinates.</summary>
/// <param name="Position">The clicked block position.</param>
/// <param name="Face">The clicked face (255 = item use).</param>
/// <param name="HeldItem">The held item slot.</param>
/// <param name="CursorX">The in-block cursor X (0..15).</param>
/// <param name="CursorY">The in-block cursor Y (0..15).</param>
/// <param name="CursorZ">The in-block cursor Z (0..15).</param>
public sealed record ServerboundLegacyBlockPlacePacket(
    BlockPos Position,
    int Face,
    ItemStack HeldItem,
    byte CursorX,
    byte CursorY,
    byte CursorZ) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.LegacyBlockPlace;
}
