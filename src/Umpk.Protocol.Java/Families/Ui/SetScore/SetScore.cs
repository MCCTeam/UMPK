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
        /// <summary>Modern set-score (<c>minecraft:set_score</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSetScorePacket> SetScore =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_score"));

        /// <summary>Legacy 1.8 update-score (<c>minecraft:set_score</c>, 47).</summary>
        public static readonly PacketType<ClientboundLegacySetScorePacket> LegacySetScore =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_score"));
    }
}

/// <summary>Modern set-score (770/776), a record-shaped packet: owner, objective, VarInt value, optional display name component, and optional number format. Resetting a score is a separate packet.</summary>
public sealed record ClientboundSetScorePacket(
    string Owner,
    string ObjectiveName,
    int Value,
    Component? DisplayName,
    ScoreNumberFormat? NumberFormat) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetScore;
}

/// <summary>Legacy 1.8 update-score (47): owner name (40), action (change=0/remove=1), objective (16), and, only when the action is not remove, a VarInt value.</summary>
public sealed record ClientboundLegacySetScorePacket(
    string Owner,
    LegacyScoreAction Action,
    string ObjectiveName,
    int Value) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.LegacySetScore;
}

/// <summary>The 1.8 update-score action (change=0, remove=1).</summary>
public enum LegacyScoreAction : byte
{
    /// <summary>Set/change the score.</summary>
    Change = 0,

    /// <summary>Remove the score.</summary>
    Remove = 1,
}
