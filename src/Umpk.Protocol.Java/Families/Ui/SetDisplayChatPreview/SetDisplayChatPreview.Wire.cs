using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatDisplayCodecs
{
    /// <summary>The chat-preview toggle (protocols 759 and 760 ONLY): one boolean and nothing else, so the whole frame body is a single byte.</summary>
    /// <remarks>The payload is a single <c>enabled</c> boolean. 1.19.3 deleted the packet along with the rest of the chat-preview family, so this codec must never be bound above 760.</remarks>
    public static readonly PacketCodec<ClientboundSetDisplayChatPreviewPacket> SetDisplayChatPreviewV1_19 =
        PacketCodec<ClientboundSetDisplayChatPreviewPacket>.Of(
            static (ref PacketWriter w, ClientboundSetDisplayChatPreviewPacket p, PacketCodecContext _) =>
                w.WriteBool(p.Enabled),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetDisplayChatPreviewPacket(r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetDisplayChatPreview(PacketBindings bindings)
    {
        // The chat-preview toggle exists on 759 and 760 and nowhere else: 1.19.3 deleted the whole preview family. No upper bound is written here because none is needed - the identity is only registered in the 759 and 760 tables, so the timeline cannot reach another protocol.
        bindings.Packet(UiPackets.Clientbound.SetDisplayChatPreview)
            .From(JavaEras.ChatSigning, ChatDisplayCodecs.SetDisplayChatPreviewV1_19);
    }
}
