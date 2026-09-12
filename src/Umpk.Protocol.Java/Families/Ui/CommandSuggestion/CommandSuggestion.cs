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
        /// <summary>Command suggestion request (<c>minecraft:command_suggestion</c>, 770/776).</summary>
        public static readonly PacketType<ServerboundCommandSuggestionPacket> CommandSuggestion =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("command_suggestion"));
    }
}

/// <summary>Command suggestion request (770/776): a transaction id and the command text so far.</summary>
public sealed record ServerboundCommandSuggestionPacket(int TransactionId, string Command) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.CommandSuggestion;
}
