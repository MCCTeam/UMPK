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
        /// <summary>Block placement carrying the used item hand (<c>minecraft:block_place</c> / <c>use_item_on</c>).</summary>
        public static readonly PacketType<ServerboundUseItemOnPacket> UseItemOn =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("use_item_on"));
    }
}

/// <summary>Use an item on a block (block placement). Modern only; carries hand, hit result, sequence.</summary>
/// <param name="Hand">The interaction hand (0 main, 1 off).</param>
/// <param name="Position">The clicked block position.</param>
/// <param name="Face">The clicked face (0 down.. 5 east).</param>
/// <param name="CursorX">The in-block cursor X (0..1).</param>
/// <param name="CursorY">The in-block cursor Y (0..1).</param>
/// <param name="CursorZ">The in-block cursor Z (0..1).</param>
/// <param name="Inside">Whether the player's head is inside the block.</param>
/// <param name="WorldBorderHit">Whether the hit was against the world border (1.21.5+).</param>
/// <param name="Sequence">The interaction sequence number.</param>
public sealed record ServerboundUseItemOnPacket(
    int Hand,
    BlockPos Position,
    int Face,
    float CursorX,
    float CursorY,
    float CursorZ,
    bool Inside,
    bool WorldBorderHit,
    int Sequence) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.UseItemOn;
}
