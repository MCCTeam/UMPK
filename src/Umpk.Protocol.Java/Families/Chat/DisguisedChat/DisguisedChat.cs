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
        /// <summary>Disguised chat (<c>minecraft:disguised_chat</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundDisguisedChatPacket> DisguisedChat =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("disguised_chat"));
    }
}

/// <summary>Disguised chat (770/776): a message component and a bound chat type: a VarInt chat-type registry id, a sender-name component, and an optional target-name component.</summary>
public sealed record ClientboundDisguisedChatPacket(
    Component Message,
    int ChatTypeId,
    Component SenderName,
    Component? TargetName) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.DisguisedChat;
}
