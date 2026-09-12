using Umpk.Protocol.Java.Signing;
using Umpk.Text;

namespace Umpk.Client.Events;

/// <summary>The origin category of an inbound chat message.</summary>
public enum ChatCategory
{
    /// <summary>A legacy (pre-1.19) chat packet, position-tagged.</summary>
    Legacy,

    /// <summary>A player-authored, signed chat message.</summary>
    Player,

    /// <summary>A system message.</summary>
    System,

    /// <summary>A disguised chat message (server-voiced through a chat type).</summary>
    Disguised,
}

/// <summary>The signature standing of an inbound chat message. A message is always delivered regardless of this value: vanilla renders unverified and insecure chat rather than hiding it, and a consumer that wants to treat unsigned chat differently needs to see the message to do so. Only <see cref="ChatCategory.Player"/> messages carry a signature at all; every other category reports <see cref="NotApplicable"/>.</summary>
public enum ChatVerification
{
    /// <summary>Not a signed player message (legacy, system, or disguised chat).</summary>
    NotApplicable,

    /// <summary>Signed, and the signature verified against the sender's profile key.</summary>
    Verified,

    /// <summary>Signed, but the client could not check it: no session verifier was resolvable for the sender (no profile key seen, or per-peer verification is not configured on this session).</summary>
    Unverified,

    /// <summary>Signed, and verification ran and rejected the message (bad signature, broken chain).</summary>
    Failed,

    /// <summary>Carried no signature at all: insecure chat, as an offline-mode server sends.</summary>
    Insecure,
}

/// <summary>Raised for every inbound chat message, normalized across eras.</summary>
/// <remarks><paramref name="Message"/> is the line as a client renders it. Through 1.18.2 the server composes it; from 1.19 the server sends only the body and the client decorates, so on that band <paramref name="Message"/> is a translatable over <paramref name="SenderName"/>, the body and (for a whisper or a team message) <see cref="TargetName"/>, rather than the body alone. Which translatable comes from the server's own <c>minecraft:chat_type</c> registry, resolved through <see cref="ChatTypeId"/>; an id that does not resolve falls back to <c>chat.type.text</c> for player chat and is left undecorated for disguised chat.</remarks>
public sealed record ChatMessageReceived(
    Component Message,
    ChatCategory Category,
    Component? SenderName,
    Guid? SenderId,
    bool IsOverlay,
    ChatVerification Verification = ChatVerification.NotApplicable) : IClientEvent
{
    /// <summary>
    /// The chat-type REGISTRY id (1.19+ player and disguised chat), or -1 where the era carries none or the wire named no registry entry. <see cref="Message"/> is already composed through it whenever the server's <c>minecraft:chat_type</c> registry resolved it (see <see cref="Umpk.Game.Registries.RegistryAccess.ChatTypes"/>); the id stays published so a consumer can tell a whisper from a say without re-reading the composed line.
    /// <para>This is the registry id, not the raw wire value. From 1.21 (protocol 767) the bound chat-type holder encodes <c>id + 1</c> and reserves 0 for an inline chat type; through 1.20.6 it writes the bare id. Both are normalized here, so this value indexes <see cref="Umpk.Game.Registries.RegistryAccess.ChatTypes"/> on every era.</para>
    /// </summary>
    public int ChatTypeId { get; init; } = -1;

    /// <summary>The chat type's target-name parameter, used by the whisper and team-message decorations, or null. On protocol 759 this field instead carries the team name; the wire is identical and both feed the same decoration slot.</summary>
    public Component? TargetName { get; init; }

    /// <summary>The undecorated message body, before any chat-type decoration was applied. Equal to <see cref="Message"/> on every era that composes the line server-side.</summary>
    public Component Body { get; init; } = Message;

    /// <summary>The server's chat filter verdict for this message (1.19.1+ player chat; every other category and protocol 759 report <see cref="ChatFilterMaskType.PassThrough"/>).</summary>
    /// <remarks><see cref="ChatFilterMaskType.PartiallyFiltered"/> means <see cref="Body"/> and <see cref="Message"/> have ALREADY had the mask applied: the filtered characters are <c>#</c> runs styled dark grey with a <c>chat.filtered</c> tooltip, and the unfiltered original is not carried on this event in any field. <see cref="ChatFilterMaskType.FullyFiltered"/> never reaches this event at all; that case is <see cref="ChatMessageSuppressed"/>.</remarks>
    public ChatFilterMaskType Filter { get; init; } = ChatFilterMaskType.PassThrough;

    /// <summary>True when <see cref="Verification"/> is <see cref="ChatVerification.Failed"/> specifically because the session's shared chat signature cache is known to be desynced from the server's (see <c>Umpk.Protocol.Java.Signing.MessageSignatureCache.IsDesynced</c>), rather than because this message's signature was actually checked and rejected. Always false for every other <see cref="ChatVerification"/> value.</summary>
    /// <remarks>Lets a consumer distinguish two very different situations that both surface as <see cref="ChatVerification.Failed"/>: a genuine cryptographic rejection (a real signal about the SENDER, worth acting on) versus this session's view of the connection's signature cache being known to be wrong (says nothing about the sender, and per <c>MessageSignatureCache.IsDesynced</c> will not correct itself before a reconnect; once true here, expect it to stay true for essentially every later signed message on this connection).</remarks>
    public bool SignatureCacheDesynced { get; init; }
}

/// <summary>Raised when the server delivered a chat message and told the client to display none of it, which is a <see cref="ChatFilterMaskType.FullyFiltered"/> filter mask.</summary>
/// <remarks>The vanilla client renders nothing for such a message. The frame does still carry the full signed body, so no content is surfaced here on purpose. The event exists because publishing nothing at all would make a suppressed message indistinguishable from a message that was never sent.</remarks>
public sealed record ChatMessageSuppressed(Guid SenderId, ChatFilterMaskType Filter) : IClientEvent;

/// <summary>Raised when an inbound <c>player_chat</c> frame carried a global chat index other than the expected one, i.e. the server's chat stream is missing a message or delivered one out of order (1.21.5+ only).</summary>
/// <remarks>The vanilla client treats this as fatal, logs a missing or out-of-order message, and disconnects with <c>multiplayer.disconnect.bad_chat_index</c>. UMPK reports it and keeps the session; see <see cref="Umpk.Client.State.ChatState"/> for why. A consumer whose automation depends on seeing every chat line should treat this as the signal that it did not.</remarks>
/// <param name="ExpectedIndex">The global index the client expected next.</param>
/// <param name="ActualIndex">The global index the frame actually carried.</param>
/// <param name="TotalGaps">How many gaps have been observed since this game join, including this one. Carried on the event so a consumer can tell one dropped frame from a stream that is losing messages continuously without having to hold on to <see cref="Umpk.Client.State.ChatState"/> itself.</param>
public sealed record ChatStreamGap(int ExpectedIndex, int ActualIndex, int TotalGaps) : IClientEvent;

/// <summary>Raised when the server recalls a previously delivered signed message.</summary>
/// <param name="MessageId">The raw wire id from <c>ClientboundDeleteChatPacket</c>: -1 when the frame carried the deleted message's signature inline, otherwise a signature-cache index that is meaningless without the cache (see <see cref="ResolvedSignature"/>). Kept as-is for backward compatibility; a consumer wanting to identify the deleted message should prefer <see cref="ResolvedSignature"/>.</param>
public sealed record ChatMessageDeleted(int MessageId) : IClientEvent
{
    /// <summary>The deleted message's actual 256-byte signature, when it could be determined: either carried inline on the wire, or resolved through the session's shared signature cache (<c>Umpk.Protocol.Java.Signing.MessageSignatureCache</c>) when the frame carried a cache-id reference instead. Null when the frame referenced a cache index this session's cache has no entry for (an unresolvable reference, or no cache wired up at all; see <c>Umpk.Client.Internal.SignedChatVerification.RecordForCache</c>). Vanilla identifies the deleted message by this same resolved signature, not by any raw wire id.</summary>
    public byte[]? ResolvedSignature { get; init; }
}

/// <summary>Raised when the server resets chat signing state during reconfiguration.</summary>
public sealed record ChatReset : IClientEvent;

/// <summary>Raised on protocols 759 and 760 when the server announces its chat-preview setting through <c>minecraft:set_display_chat_preview</c>. Nothing else raises it, because no other protocol has the packet: the whole preview family was deleted at 1.19.3.</summary>
/// <remarks>
/// <para>This exists so a client that cannot preview does not silently look like one that can. With <paramref name="Enabled"/> true the server will decorate outgoing messages, and UMPK's signature covers the message's PLAIN text rather than that decoration, because it drives no preview round trip. The message is still delivered and still verifies: its signed form covers the plain content and carries the decorated content as an unsigned override when the preview flag is false. So this is a notice about what the signature COVERS, not a failure.</para>
/// <para>A consumer that needs the decoration to be covered has to act on this event or on <c>ClientState.Chat.ServerPreviewsChat</c>, because there is no capability to consult: this client cannot sign a preview on any version, so an "ask before you send" answer would be a constant.</para>
/// </remarks>
/// <param name="Enabled">What the server announced.</param>
/// <param name="Protocol">The negotiated wire protocol number, so the notice can name its version.</param>
public sealed record ChatPreviewAnnounced(bool Enabled, int Protocol) : IClientEvent;

/// <summary>Raised when the title text changes.</summary>
public sealed record TitleChanged(Component Text) : IClientEvent;

/// <summary>Raised when the subtitle text changes.</summary>
public sealed record SubtitleChanged(Component Text) : IClientEvent;

/// <summary>Raised when the action-bar text changes.</summary>
public sealed record ActionBarChanged(Component Text) : IClientEvent;

/// <summary>Raised when title timings change.</summary>
public sealed record TitleTimesChanged(int FadeIn, int Stay, int FadeOut) : IClientEvent;

/// <summary>Raised when the server clears (or resets) titles.</summary>
public sealed record TitlesCleared(bool ResetTimes) : IClientEvent;
