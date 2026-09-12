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
        // command suggestions

        /// <summary>Command suggestions (<c>minecraft:command_suggestions</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundCommandSuggestionsPacket> CommandSuggestions =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("command_suggestions"));
    }
}

/// <summary>Command suggestions (770/776): a transaction id, a replace range (start + length), and a list of suggestions with optional tooltips.</summary>
public sealed record ClientboundCommandSuggestionsPacket(
    int TransactionId,
    int RangeStart,
    int RangeLength,
    IReadOnlyList<CommandSuggestion> Suggestions) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.CommandSuggestions;
}
