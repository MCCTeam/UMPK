using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Serverbound
    {
        /// <summary>Configure a command block (<c>minecraft:set_command_block</c>).</summary>
        public static readonly PacketType<ServerboundSetCommandBlockPacket> SetCommandBlock =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("set_command_block"));
    }
}

/// <summary>Configure a command block (1.13+): the block position, the command text, the block mode (<c>SEQUENCE=0</c> / <c>AUTO=1</c> / <c>REDSTONE=2</c>), and three flags packed into a byte (<c>track_output=1</c>, <c>conditional=2</c>, <c>automatic=4</c>). Pre-1.14 packs the block position with the y in the middle 12 bits.</summary>
/// <param name="Pos">The command block's position.</param>
/// <param name="Command">The command text.</param>
/// <param name="Mode">The command block mode ordinal (0 sequence, 1 auto, 2 redstone).</param>
/// <param name="TrackOutput">Whether the last output is stored/shown.</param>
/// <param name="Conditional">Whether the block is conditional.</param>
/// <param name="Automatic">Whether the block is always active (needs no redstone).</param>
public sealed record ServerboundSetCommandBlockPacket(
    Umpk.Geometry.BlockPos Pos, string Command, int Mode, bool TrackOutput, bool Conditional, bool Automatic) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => WorldPackets.Serverbound.SetCommandBlock;
}
