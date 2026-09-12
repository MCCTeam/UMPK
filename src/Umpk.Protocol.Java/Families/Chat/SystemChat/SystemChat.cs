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
        /// <summary>System chat (<c>minecraft:system_chat</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSystemChatPacket> SystemChat =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("system_chat"));
    }
}

/// <summary>System chat (770/776): a component and an overlay (action-bar) flag.</summary>
public sealed record ClientboundSystemChatPacket(Component Content, bool Overlay) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SystemChat;
}
