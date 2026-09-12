using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatCodecs
{
    /// <summary>1.8 serverbound chat (string, max 100 chars).</summary>
    public static readonly PacketCodec<ServerboundLegacyChatPacket> ServerLegacyV1_8 =
        PacketCodec<ServerboundLegacyChatPacket>.Of(
            static (ref PacketWriter w, ServerboundLegacyChatPacket p, PacketCodecContext _) => w.WriteString(p.Message, 100),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundLegacyChatPacket(r.ReadString(100)));

    /// <summary>1.8 clientbound chat (JSON component + position byte).</summary>
    public static readonly PacketCodec<ClientboundLegacyChatPacket> ClientLegacyV1_8 =
        PacketCodec<ClientboundLegacyChatPacket>.Of(
            static (ref PacketWriter w, ClientboundLegacyChatPacket p, PacketCodecContext _) =>
            {
                // Re-encode the exact decoded JSON when present (the component JSON round-trip normalises key order and collapses simple components, so re-serialising would not be byte-exact).
                w.WriteString(p.RawJson ?? ComponentJson.ToJsonString(p.Message, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), 262144);
                w.WriteByte(p.Position);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string raw = r.ReadString(262144);
                var msg = ComponentJson.Parse(raw, ComponentWireEra.Legacy);
                return new ClientboundLegacyChatPacket(msg, r.ReadByte(), Sender: null, RawJson: raw);
            });

    /// <summary>1.16-1.18.2 clientbound chat (protocols 735-758): JSON component + position byte + sender UUID. The sender UUID was added at 1.16 and removed at 1.19 when signed player_chat took over. Components are still JSON strings on this range.</summary>
    public static readonly PacketCodec<ClientboundLegacyChatPacket> ClientLegacyV1_16 =
        PacketCodec<ClientboundLegacyChatPacket>.Of(
            static (ref PacketWriter w, ClientboundLegacyChatPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.RawJson ?? ComponentJson.ToJsonString(p.Message, ComponentWireEra.Legacy, ComponentJsonLiteralForm.Object), 262144);
                w.WriteByte(p.Position);
                w.WriteUuid(p.Sender ?? Guid.Empty);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string raw = r.ReadString(262144);
                var msg = ComponentJson.Parse(raw, ComponentWireEra.Legacy);
                byte position = r.ReadByte();
                return new ClientboundLegacyChatPacket(msg, position, r.ReadUuid(), raw);
            });

    /// <summary>1.19 signed serverbound chat (protocol 759, signing v1): string message, instant timestamp, salt + VarInt-prefixed signature bytes (empty = unsigned), signed-preview flag.</summary>
    /// <remarks>
    /// <para><b>The preview flag is written false on purpose, and it is not an offline detail.</b> UMPK drives no chat-preview round trip on any version, so it has no previewed component to sign; writing false is the only truthful value and matches a client with chat preview disabled. The false preview arm is a fallback, never a refusal: the signature is verified over the plain text and the decoration travels unsigned. Decode consumes the flag frame-exactly without retaining it; the packet model carries no preview field because there is nothing on this client that could act on one.</para>
    /// <para>The limitation is not silent: <c>minecraft:set_display_chat_preview</c> IS decoded (see <c>Umpk.Protocol.Java.Codecs.ChatDisplayCodecs.SetDisplayChatPreviewV1_19</c>), so a session against a previewing server records it on <c>ClientState.Chat.ServerPreviewsChat</c>, raises <c>Umpk.Client.Events.ChatPreviewAnnounced</c> and logs a warning naming the protocol, and <c>ClientState.Chat.ServerPreviewsChat</c> says whether the live server is one of them.</para>
    /// </remarks>
    public static readonly PacketCodec<ServerboundSignedChatPacket> SignedV1_19 =
        PacketCodec<ServerboundSignedChatPacket>.Of(
            static (ref PacketWriter w, ServerboundSignedChatPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Message, MaxContentChars);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                w.WriteByteArray(p.Signature ?? []);
                w.WriteBool(false);
            },
            static (ref PacketReader r, PacketCodecContext ctx) =>
            {
                string message = r.ReadString(MaxContentChars);
                long timestamp = r.ReadLong();
                long salt = r.ReadLong();
                byte[] signature = r.ReadByteArray().ToArray();
                _ = r.ReadBool();
                return new ServerboundSignedChatPacket(
                    message, timestamp, salt, signature.Length == 0 ? null : signature,
                    new LastSeenMessagesUpdate(0, new byte[3], 0));
            });

    /// <summary>
    /// 1.19.1/1.19.2 signed serverbound chat (protocol 760, signing v2): the v1 fields plus the trailing last-seen acknowledgment (VarInt-counted entries of sender UUID + VarInt-prefixed signature, then an optional last-received entry). The entries are CARRIED, not discarded: on this era the acknowledged window is folded into the signed body, so a server that receives an empty list beside a signature computed over a non-empty one rejects the message.
    /// <para>The acknowledgement contains at most five sender UUID and signature entries, followed by an optional final entry.</para>
    /// </summary>
    /// <remarks>
    /// <para><b>The preview flag is written false on purpose</b>, for the same reason and with the same evidence as <see cref="SignedV1_19"/>. A fallback, not a refusal. An undecorated signed body writes no component between the <c>0x46</c> separator and the last-seen entries. That is exactly what <c>ChatSigningSession</c> signs for an undecorated message.</para>
    /// <para>A DECORATED send would need the preview round trip (<c>minecraft:chat_preview</c> both ways), which this client does not drive on any version, so this flag is false on every send it makes. <c>ClientState.Chat.ServerPreviewsChat</c> is where a consumer learns that the live server is one where that costs it something. The RECEIVE side of a decorated message is fully implemented and unaffected.</para>
    /// </remarks>
    public static readonly PacketCodec<ServerboundSignedChatPacket> SignedV1_19_1 =
        PacketCodec<ServerboundSignedChatPacket>.Of(
            static (ref PacketWriter w, ServerboundSignedChatPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Message, MaxContentChars);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                w.WriteByteArray(p.Signature ?? []);
                w.WriteBool(false);
                WriteLegacyAcknowledgment(ref w, p.LegacyLastSeen, p.LegacyLastReceived);
            },
            static (ref PacketReader r, PacketCodecContext ctx) =>
            {
                string message = r.ReadString(MaxContentChars);
                long timestamp = r.ReadLong();
                long salt = r.ReadLong();
                byte[] signature = r.ReadByteArray().ToArray();
                _ = r.ReadBool();
                (LastSeenMessageEntry[] lastSeen, LastSeenMessageEntry? lastReceived) = ReadLegacyAcknowledgment(ref r);

                return new ServerboundSignedChatPacket(
                    message, timestamp, salt, signature.Length == 0 ? null : signature,
                    new LastSeenMessagesUpdate(0, new byte[3], 0))
                {
                    LegacyLastSeen = lastSeen,
                    LegacyLastReceived = lastReceived,
                };
            });

    /// <summary>1.19.3-1.21.4 signed serverbound chat (protocols 761-769, signing v3 without the ack checksum): string message, instant timestamp, long salt, nullable fixed 256-byte signature, last-seen update (VarInt offset + 20-bit fixed bitset). The checksum byte only arrives at 1.21.5.</summary>
    public static readonly PacketCodec<ServerboundSignedChatPacket> SignedV1_19_3 = Signed(hasChecksum: false);

    /// <summary>1.21.5 signed serverbound chat (v3 signing: adds a checksum byte to the ack window, adds a checksum byte to the acknowledgement window.</summary>
    public static readonly PacketCodec<ServerboundSignedChatPacket> SignedV1_21_5 = Signed(hasChecksum: true);

    /// <summary>26.1 signed serverbound chat.</summary>
    public static readonly PacketCodec<ServerboundSignedChatPacket> SignedV26_1 = Signed(hasChecksum: true);

    private static PacketCodec<ServerboundSignedChatPacket> Signed(bool hasChecksum) =>
        PacketCodec<ServerboundSignedChatPacket>.Of(
            (ref PacketWriter w, ServerboundSignedChatPacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Message, 256);
                w.WriteLong(p.TimestampMillis);
                w.WriteLong(p.Salt);
                if (p.Signature is { } sig)
                {
                    w.WriteBool(true);
                    if (sig.Length != 256)
                        throw new ProtocolViolationException("A chat signature must be exactly 256 bytes.");

                    w.WriteBytes(sig);
                }
                else
                    w.WriteBool(false);

                w.WriteVarInt(p.LastSeen.Offset);
                if (p.LastSeen.Acknowledged.Length != 3)
                    throw new ProtocolViolationException("The last-seen ack bitset must be 3 bytes (20 bits).");

                w.WriteBytes(p.LastSeen.Acknowledged);
                if (hasChecksum)
                    w.WriteByte(p.LastSeen.Checksum);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string message = r.ReadString(256);
                long timestamp = r.ReadLong();
                long salt = r.ReadLong();
                byte[]? signature = r.ReadBool() ? r.ReadBytes(256).ToArray() : null;
                int offset = r.ReadVarInt();
                byte[] ack = r.ReadBytes(3).ToArray();
                byte checksum = hasChecksum ? r.ReadByte() : (byte)0;
                return new ServerboundSignedChatPacket(message, timestamp, salt, signature,
                    new LastSeenMessagesUpdate(offset, ack, checksum));
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChat(PacketBindings bindings)
    {
        // chat Legacy inbound chat: JSON component + position byte, gaining a sender UUID at 1.16 (removed at 1.19 when signed chat took over).
        bindings.Packet(PlayPackets.Clientbound.LegacyChat)
            .From(JavaProtocols.V1_8, ChatCodecs.ClientLegacyV1_8)
            .From(JavaProtocols.V1_16, ChatCodecs.ClientLegacyV1_16);

        // Serverbound chat uses a bare string through 1.18.2, then three signing generations: v1 at 1.19 (salt + VarInt signature + preview flag), v2 at 1.19.1 (v1 plus the last-seen entry list), v3 at 1.19.3 (nullable fixed 256-byte signature + offset/bitset ack, unchanged through 1.21.4), with the ack checksum byte added at 1.21.5.
        bindings.Packet(PlayPackets.Serverbound.LegacyChat)
            .From(JavaProtocols.V1_8, ChatCodecs.ServerLegacyV1_8)
            .FromAs(JavaProtocols.V1_19, PlayPackets.Serverbound.SignedChat, ChatCodecs.SignedV1_19)
            .FromAs(JavaProtocols.V1_19_1, PlayPackets.Serverbound.SignedChat, ChatCodecs.SignedV1_19_1)
            .FromAs(JavaProtocols.V1_19_3, PlayPackets.Serverbound.SignedChat, ChatCodecs.SignedV1_19_3)
            .FromAs(JavaProtocols.V1_21_5, PlayPackets.Serverbound.SignedChat, ChatCodecs.SignedV1_21_5)
            .FromAs(JavaProtocols.V26_1, PlayPackets.Serverbound.SignedChat, ChatCodecs.SignedV26_1);
    }
}
