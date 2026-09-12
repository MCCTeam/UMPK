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
        /// <summary>Custom chat completions (<c>minecraft:custom_chat_completions</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundCustomChatCompletionsPacket> CustomChatCompletions =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("custom_chat_completions"));
    }
}

/// <summary>Custom chat completions (770/776): an action and a list of completion strings.</summary>
public sealed record ClientboundCustomChatCompletionsPacket(
    ChatCompletionsAction Action,
    IReadOnlyList<string> Entries) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.CustomChatCompletions;
}

/// <summary>The custom-chat-completions action (add=0, remove=1, set=2).</summary>
public enum ChatCompletionsAction
{
    /// <summary>Add the given completions.</summary>
    Add = 0,

    /// <summary>Remove the given completions.</summary>
    Remove = 1,

    /// <summary>Replace the completions with the given set.</summary>
    Set = 2,
}
