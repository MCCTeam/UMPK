using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Serverbound
    {
        /// <summary>Update the text of a sign being edited (<c>minecraft:sign_update</c>).</summary>
        public static readonly PacketType<ServerboundSignUpdatePacket> SignUpdate =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("sign_update"));
    }
}

/// <summary>Update the text of the sign currently being edited. Carries the sign's block position, the front/back flag (1.20+; the modern sign has two text sides), and the four text lines. Pre-1.20 has no front/back flag; pre-1.14 packs the block position with the y in the middle 12 bits. The pre-1.9 form sent the lines as JSON chat components and is not modeled here.</summary>
/// <param name="Pos">The sign's block position.</param>
/// <param name="IsFrontText">Whether the front side is being edited (1.20+); ignored on older versions.</param>
/// <param name="Line1">The first text line.</param>
/// <param name="Line2">The second text line.</param>
/// <param name="Line3">The third text line.</param>
/// <param name="Line4">The fourth text line.</param>
public sealed record ServerboundSignUpdatePacket(
    BlockPos Pos, bool IsFrontText, string Line1, string Line2, string Line3, string Line4) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.SignUpdate;
}
