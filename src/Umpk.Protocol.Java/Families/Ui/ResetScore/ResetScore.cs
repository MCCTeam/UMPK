using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Reset-score (<c>minecraft:reset_score</c>, 770/776; a separate packet from set-score).</summary>
        public static readonly PacketType<ClientboundResetScorePacket> ResetScore =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("reset_score"));
    }
}

/// <summary>Reset-score (770/776): owner, and an optional objective name (absent resets every objective).</summary>
public sealed record ClientboundResetScorePacket(string Owner, string? ObjectiveName) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ResetScore;
}
