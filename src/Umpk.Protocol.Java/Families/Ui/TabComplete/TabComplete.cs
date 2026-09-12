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
        /// <summary>Legacy 1.8 tab complete (<c>minecraft:tab_complete</c>, 47).</summary>
        public static readonly PacketType<ClientboundLegacyTabCompletePacket> LegacyTabComplete =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("tab_complete"));
    }

    public static partial class Serverbound
    {
        /// <summary>Legacy 1.8 tab complete request (<c>minecraft:tab_complete</c>, 47).</summary>
        public static readonly PacketType<ServerboundLegacyTabCompletePacket> LegacyTabComplete =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("tab_complete"));
    }
}

/// <summary>Legacy 1.8 tab complete (47): a list of completion strings.</summary>
public sealed record ClientboundLegacyTabCompletePacket(IReadOnlyList<string> Matches) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.LegacyTabComplete;
}

/// <summary>Pre-Brigadier tab complete request (47-340): the text and an optional looked-at block position. <paramref name="AssumeCommand"/> is the flag 1.9 added between them and is null on 1.8.</summary>
public sealed record ServerboundLegacyTabCompletePacket(string Text, BlockPos? LookedAtBlock, bool? AssumeCommand) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.LegacyTabComplete;
}
