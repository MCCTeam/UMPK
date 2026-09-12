using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatCodecs
{
    /// <summary>1.19.3+ serverbound chat session update (<c>minecraft:chat_session_update</c>): the chat session UUID followed by the profile public key data (expiry epoch-millis long, VarInt-prefixed DER SubjectPublicKeyInfo key bytes, VarInt-prefixed v2 signature). Frozen wire from 1.19.3 through the latest signing era.</summary>
    public static readonly PacketCodec<ServerboundChatSessionUpdatePacket> ChatSessionUpdateV1_19_3 =
        PacketCodec<ServerboundChatSessionUpdatePacket>.Of(
            static (ref PacketWriter w, ServerboundChatSessionUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteUuid(p.SessionId);
                w.WriteLong(p.Key.ExpiresAtMillis);
                w.WriteByteArray(p.Key.KeyDer);
                w.WriteByteArray(p.Key.KeySignature);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                Guid sessionId = r.ReadUuid();
                long expiresAt = r.ReadLong();
                byte[] keyDer = r.ReadByteArray().ToArray();
                byte[] signature = r.ReadByteArray().ToArray();
                return new ServerboundChatSessionUpdatePacket(
                    sessionId, new ProfilePublicKeyData(expiresAt, keyDer, signature));
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChatSessionUpdate(PacketBindings bindings)
    {
        // Serverbound chat session update (1.19.3+): announces the chat session id + profile public key so the server can validate signed chat. Standalone chat acknowledgement (1.19.3+): a VarInt offset. Both wire forms remain unchanged from 1.19.3 onward.
        bindings.Packet(PlayPackets.Serverbound.ChatSessionUpdate)
            .From(JavaProtocols.V1_19_3, ChatCodecs.ChatSessionUpdateV1_19_3);
    }
}
