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
        /// <summary>The server's chat-preview toggle (<c>minecraft:set_display_chat_preview</c>), which exists on protocols 759 and 760 only and was deleted at 1.19.3.</summary>
        public static readonly PacketType<ClientboundSetDisplayChatPreviewPacket> SetDisplayChatPreview =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_display_chat_preview"));
    }
}

/// <summary>The server's chat-preview toggle (protocols 759 and 760 only): one boolean, true when the server has <c>previews-chat</c> enabled.</summary>
/// <remarks>
/// <para>The payload is a single <c>enabled</c> boolean. The whole chat-preview family (this, <c>minecraft:chat_preview</c> in both directions) was deleted at 1.19.3, so this identity exists on exactly two protocols.</para>
/// <para>UMPK decodes it for one reason: it is the only way a client learns that the server will DECORATE the messages it sends, and UMPK drives no preview round trip, so on such a server its outbound signature covers the plain text rather than the decoration. That is the wire behaviour of a client with chat preview disabled, but it must not be silent, which is what decoding this frame is for: it is the only notice a client gets.</para>
/// </remarks>
/// <param name="Enabled">Whether the server previews (and therefore may decorate) chat.</param>
public sealed record ClientboundSetDisplayChatPreviewPacket(bool Enabled) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetDisplayChatPreview;
}
