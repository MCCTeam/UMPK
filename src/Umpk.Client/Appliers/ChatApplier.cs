using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;

namespace Umpk.Client.Appliers;

/// <summary>Applies chat and title packets. Normalizes the legacy, system, and disguised chat variants into a single <see cref="ChatMessageReceived"/> event plus the title family events. The signed player-chat packet is not modeled in the implemented surface, so player chat surfaces as it is exposed by the version (legacy or system).</summary>
internal sealed class ChatApplier : IApplier
{
    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        switch (packet)
        {
            case ClientboundLegacyChatPacket legacy:
                await context.PublishAsync(new ChatMessageReceived(
                    legacy.Message, ChatCategory.Legacy, SenderName: null, SenderId: null,
                    IsOverlay: legacy.Position == 2)).ConfigureAwait(false);
                return true;

            case ClientboundPlayerChatPacket player:
                await ApplyPlayerChatAsync(player, context, ct).ConfigureAwait(false);
                return true;

            case ClientboundSystemChatPacket system:
                await context.PublishAsync(new ChatMessageReceived(
                    system.Content, ChatCategory.System, SenderName: null, SenderId: null,
                    IsOverlay: system.Overlay)).ConfigureAwait(false);
                return true;

            // Disguised chat is decorated through the SAME bound chat type vanilla's client uses: Disguised chat decorates the component through the same bound type player chat makes. It is decorated only when the server's chat-type registry resolves the id, because the default player-chat key would be a guess for the command-issued types this packet usually carries.
            case ClientboundDisguisedChatPacket disguised:
                int disguisedType = Internal.ChatTypeDecoration.ToRegistryId(
                    disguised.ChatTypeId, context.Version.Version.Protocol);
                await context.PublishAsync(new ChatMessageReceived(
                    Internal.ChatTypeDecoration.Decorate(
                        context.State.Registries?.ChatTypes,
                        disguisedType,
                        disguised.Message,
                        disguised.SenderName,
                        disguised.TargetName,
                        fallbackToPlayerChat: false),
                    ChatCategory.Disguised,
                    disguised.SenderName,
                    SenderId: null,
                    IsOverlay: false)
                {
                    ChatTypeId = disguisedType,
                    TargetName = disguised.TargetName,
                    Body = disguised.Message,
                }).ConfigureAwait(false);
                return true;

            case ClientboundDeleteChatPacket delete:
                // The delete target resolves through the same per-connection signature cache as a last-seen entry and identifies the message by its signature, not by a raw wire id. delete.Id follows the packed last-seen convention (-1 means FullSignature carries the 256 bytes inline; otherwise Id is a cache index), so it is resolved the same way here: a bare cache index is meaningless to a consumer without the cache, so ResolvedSignature is populated whenever possible while MessageId remains available for backward compatibility.
                //
                // Handled the same way as a last-seen miss rather than left as an asymmetry: an unresolvable delete_chat id is exactly as strong a desync signal as an unresolvable last-seen id -- a compliant server only ever packs a cache-id reference for a signature it has already sent down THIS connection, so either kind of miss means this side's mirror of the server's cache no longer matches it. Both misses therefore take the same invalid-packet path. There is no principled reason to treat one source of "unresolvable" as diagnostic of a dead cache and the other as unremarkable, when both draw from the identical shared per-connection cache and mean the identical thing.
                bool deleteWasDesyncedBefore = context.SignatureCache?.IsDesynced ?? false;
                byte[]? resolvedDeleteSignature = delete.FullSignature;
                if (resolvedDeleteSignature is null && delete.Id != PackedMessageSignature.FullSignatureId)
                {
                    resolvedDeleteSignature = context.SignatureCache?.Unpack(delete.Id);
                    if (resolvedDeleteSignature is null)
                        context.SignatureCache?.MarkDesynced("unresolvable delete_chat reference");

                }

                LogSignatureCacheDesyncOnce(context, deleteWasDesyncedBefore);
                await context.PublishAsync(new ChatMessageDeleted(delete.Id)
                {
                    ResolvedSignature = resolvedDeleteSignature,
                }).ConfigureAwait(false);
                return true;

            case ClientboundSetDisplayChatPreviewPacket preview:
                await ApplyChatPreviewToggleAsync(preview, context).ConfigureAwait(false);
                return true;

            case ClientboundSetTitleTextPacket title:
                await context.PublishAsync(new TitleChanged(title.Text)).ConfigureAwait(false);
                return true;

            case ClientboundSetSubtitleTextPacket subtitle:
                await context.PublishAsync(new SubtitleChanged(subtitle.Text)).ConfigureAwait(false);
                return true;

            case ClientboundSetActionBarTextPacket actionBar:
                await context.PublishAsync(new ActionBarChanged(actionBar.Text)).ConfigureAwait(false);
                return true;

            case ClientboundSetTitlesAnimationPacket times:
                await context.PublishAsync(new TitleTimesChanged(times.FadeIn, times.Stay, times.FadeOut)).ConfigureAwait(false);
                return true;

            case ClientboundClearTitlesPacket clear:
                await context.PublishAsync(new TitlesCleared(clear.ResetTimes)).ConfigureAwait(false);
                return true;

            case ClientboundLegacyTitlePacket legacyTitle:
                await ApplyLegacyTitleAsync(legacyTitle, context).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>The first protocol whose clientbound <c>player_chat</c> leads with a <c>globalIndex</c> VarInt: 770 (1.21.5). Before that, <c>ClientboundPlayerChatPacket</c> is a record over <c>(sender, index, signature, body, unsignedContent, filterMask, chatType)</c> with no such component; from 1.21.5 it declares <c>int globalIndex</c> as its FIRST component and writes it first. Below this protocol the field is not on the wire, the codec reports 0 for every frame, and tracking it would report a gap on the second message of every session.</summary>
    internal const int FirstGlobalChatIndexProtocol = 770;

    /// <summary>Records the server's chat-preview setting and, when it is ON, says once and plainly what this client will and will not do about it.</summary>
    /// <remarks>
    /// <para>This is the whole point of decoding the packet. UMPK drives no chat-preview round trip: it never sends <c>minecraft:chat_preview</c> and its serverbound chat writes <c>signedPreview = false</c> unconditionally on both eras that have the field (<c>ChatCodecs.SignedV1_19</c> and <c>ChatCodecs.SignedV1_19_1</c>). On a previewing server that means the signature covers the message's PLAIN text while the decoration the other players see rides along unsigned.</para>
    /// <para>It is a legitimate wire behaviour and not a broken send, which is why this is a notice rather than a refusal. When <c>signedPreview</c> is false, the plain text is signed and the decoration remains unsigned, so the message is delivered and verifies. Refusing to send here would reject a message the server accepts.</para>
    /// <para>Logged once per announcement rather than once per message: the toggle is a session-level fact and a per-send warning would be noise on a chatty session. A consumer that wants to branch before it sends has <c>ClientState.Chat.ServerPreviewsChat</c> and this event.</para>
    /// </remarks>
    private static async ValueTask ApplyChatPreviewToggleAsync(
        ClientboundSetDisplayChatPreviewPacket packet, ApplierContext context)
    {
        int protocol = context.Version.Version.Protocol;
        context.State.Chat.ServerPreviewsChat = packet.Enabled;

        if (packet.Enabled)
            context.Logger.LogWarning(
                "This protocol-{Protocol} server has chat preview enabled, and this client has no "
                + "chat-preview round trip on any version. Messages it sends are signed over their plain "
                + "text, so any formatting the server applies travels as unsigned content, exactly as a "
                + "vanilla client with chatPreview set to OFF does. The server accepts and delivers "
                + "them; only the decoration is outside the signature.",
                protocol);

        else
            context.Logger.LogDebug(
                "The protocol-{Protocol} server turned chat preview off; outbound signatures already "
                + "covered the plain text and are unaffected.",
                protocol);

        await context.PublishAsync(new ChatPreviewAnnounced(packet.Enabled, protocol)).ConfigureAwait(false);
    }

    /// <summary>Publishes an inbound signed <c>player_chat</c> as a player message and verifies its signature when the sender has a session verifier. Verification is advisory: every message is published with its <see cref="ChatVerification"/> result, failures are logged, and verification never faults the session.</summary>
    private static async ValueTask ApplyPlayerChatAsync(
        ClientboundPlayerChatPacket packet, ApplierContext context, CancellationToken ct)
    {
        bool wasDesyncedBeforeThisMessage = context.SignatureCache?.IsDesynced ?? false;

        // The global chat index: a per-connection monotone counter the server advances once per player_chat it sends. The reference client disconnects on a mismatch; UMPK reports it instead
        // for the reasons recorded on ChatState. Either way the value has to be read because a server that
        // drops a chat frame is otherwise indistinguishable from nobody having spoken.
        if (context.State.Chat.TracksGlobalIndex
            && !context.State.Chat.ObserveGlobalIndex(packet.GlobalIndex, out int expectedGlobalIndex))
        {
            int totalGaps = context.State.Chat.ObservedGaps;
            context.Logger.LogWarning(
                "Missing or out-of-order chat message from server, expected global index {Expected} but got {Actual} ({TotalGaps} gap(s) this join).",
                expectedGlobalIndex, packet.GlobalIndex, totalGaps);

            // A missed player_chat means the SERVER's cache advanced (it pushed this message's signatures into its own per-connection MessageSignatureCache when it sent the frame this client never received) while this side's mirror did not, so every push after this point can no longer be trusted to land in the same slots the server assigned. Mark the shared cache desynced so a later cache-id reference degrades to the non-latching "unresolvable" arm instead of silently resolving to a real-but-wrong signature. Detection exists only from FirstGlobalChatIndexProtocol up: 761-769 carry no global index at all, so a gap there cannot be detected this way.
            context.SignatureCache?.MarkDesynced("missed player_chat (chat-stream gap)");
            await context.PublishAsync(
                new ChatStreamGap(expectedGlobalIndex, packet.GlobalIndex, totalGaps)).ConfigureAwait(false);
        }

        // Resolve and record the signature cache for every signed player_chat before any sender or verifier lookup. A message from a sender this client cannot resolve still has to seed the cache, because the cache mirrors what the server broadcast, not only what this client can verify. Skipping that push would desynchronize later cache references. See SignedChatVerification.RecordForCache's remarks.
        IReadOnlyList<Protocol.Java.Signing.AcknowledgedMessage>? resolvedLastSeen =
            packet.Signature is { Length: > 0 }
                ? Internal.SignedChatVerification.RecordForCache(packet, context.SignatureCache)
                : null;

        // Warn about the cache going from trusted to desynced EXACTLY ONCE, whichever of the two triggers above (the chat-stream gap, or an unresolvable last-seen entry inside RecordForCache) caused it, naming the cause. Once desynced, essentially every later signed message fails verification for the rest of the session (the chain index a declined message should have advanced never does, so
        // the very next message's crypto check is reconstructed against the wrong index and fails too);
        // logging that on every single one of those later messages would be per-message spam.
        LogSignatureCacheDesyncOnce(context, wasDesyncedBeforeThisMessage);

        ChatVerification verification;
        if (packet.Signature is not { Length: > 0 })
        {
            // No signature on the wire at all: insecure chat, as an offline-mode server sends.
            verification = ChatVerification.Insecure;
        }
        else if (context.ChatVerifierResolver?.Invoke(packet.Sender) is not { } verifier)
        {
            // Signed, but this session has no verifier for the sender, so the signature cannot be checked. Deliver it labelled rather than discarding it. The cache resolve-and-record above already ran regardless of this branch.
            verification = ChatVerification.Unverified;
        }
        else if (resolvedLastSeen is null)
        {
            // The last-seen window could not be resolved at all (an unresolvable cache-id reference). RecordForCache has already marked the shared cache desynced (and the one-time warning above already reported it, if this was the first occurrence); report this message unverifiable without attempting a crypto check against a payload that cannot be faithfully reconstructed, and without touching the peer's chain state. Deliberately no per-message log here once the cache is known desynced (see the one-time warning above) -- only when there is no cache at all wired up for this session (should not happen live, but keeps a caller that skipped wiring one from going silently undiagnosed).
            verification = ChatVerification.Failed;
            if (context.SignatureCache is null)
                context.Logger.LogWarning(
                    "Inbound player_chat from {Sender} referenced a last-seen signature that cannot be " +
                    "resolved: no signature cache is wired up for this session.",
                    packet.Sender);

        }
        else
        {
            Protocol.Java.Signing.ChatVerificationOutcome outcome =
                Internal.SignedChatVerification.VerifyResolved(packet, verifier, DateTimeOffset.UtcNow, resolvedLastSeen);
            if (outcome.Status == Protocol.Java.Signing.ChatVerificationStatus.Ok)
                verification = ChatVerification.Verified;

            else
            {
                verification = ChatVerification.Failed;
                context.Logger.LogWarning(
                    "Inbound player_chat from {Sender} failed signature verification: {Status} ({Reason}).",
                    packet.Sender, outcome.Status, outcome.ReasonKey);
            }
        }

        // The server's chat filter verdict for this message. Present on protocols 760-776; protocol 759 carries no mask field at all, so FilterType is 0 there and this yields PassThrough.
        Protocol.Java.Signing.ChatFilterMask mask =
            Protocol.Java.Signing.ChatFilterMask.Read(packet.FilterType, packet.FilterBits);

        // The display body, with the mask applied. Null means the server said show NOTHING.
        Component? body = Internal.SignedChatVerification.DisplayContent(packet, mask);

        // Collect the inbound signature into the era's last-seen window so the next outbound signed chat acknowledges it. Exactly one of the two collectors is installed per session.
        //
        // v3 (1.19.3+): the offset/bitset ring. When the accumulated offset overflows, flush a standalone chat acknowledgement so the server's view of the last-seen count stays bounded.
        //
        // v2 (1.19.1/1.19.2): the 5-entry add-to-front window, which carries the SENDER uuid alongside the signature because the v2 signed body hashes both. It has no overflow flush: the v2 acknowledgement travels only with a chat or command send.
        //
        // A message the client did not DISPLAY still advances the offset but is not acknowledged in the bitset. Pending-signature tracking stores `displayed ? new LastSeenTrackedEntry(sig, true) : null`. Acknowledging a suppressed message would tell the server the client had seen text it deliberately withheld.
        if (packet.Signature is { Length: > 0 } signature)
        {
            if (context.LastSeenTracker is { } tracker
                && tracker.Add(signature, out int standaloneAckOffset, acknowledge: body is not null))
                await context.Sink.SendAsync(new ServerboundChatAckPacket(standaloneAckOffset), ct).ConfigureAwait(false);

            context.LegacyLastSeenCollector?.Add(
                new Protocol.Java.Signing.AcknowledgedMessage(packet.Sender, signature));
        }

        // Fully filtered: vanilla's showMessageToPlayer renders nothing and returns false. The frame still carries the whole signed body, so publishing ChatMessageReceived here in any form would hand a consumer text the server told the client to withhold. It is published as a distinct event instead, carrying the sender and the verdict but no content: a consumer that silently saw nothing could not tell a suppressed message from a message that never arrived, which is the exact ambiguity this client exists to remove.
        if (body is null)
        {
            await context.PublishAsync(new ChatMessageSuppressed(packet.Sender, mask.Type)).ConfigureAwait(false);
            return;
        }

        // From 1.19 the server sends the bare body and leaves the client to compose the line, so the sender name has to be applied here. Skipping it made every inbound player line render as the body alone on protocols 759-776, where 754 and older still showed "<name> body" because the pre-1.19 server sent an already-composed component. Which decoration to apply is the server's own chat_type registry when it resolves the id, and the default chat.type.text when it does not. See ChatTypeDecoration.
        int chatType = Internal.ChatTypeDecoration.ToRegistryId(packet.ChatTypeId, context.Version.Version.Protocol);
        await context.PublishAsync(new ChatMessageReceived(
            Internal.ChatTypeDecoration.Decorate(
                context.State.Registries?.ChatTypes,
                chatType,
                body,
                packet.SenderName,
                packet.TargetName,
                fallbackToPlayerChat: true),
            ChatCategory.Player,
            packet.SenderName,
            packet.Sender,
            IsOverlay: false,
            verification)
        {
            ChatTypeId = chatType,
            TargetName = packet.TargetName,
            Body = body,
            Filter = mask.Type,
            // Distinguishes "this sender's signature is genuinely bad" from "our cache is dead" for a consumer that only sees Failed. Scoped to Failed specifically (not Unverified/Insecure/NotApplicable, whose own verdict already fully explains itself) -- a coincidental desync alongside an Unverified verdict (no key at all) would not actually explain that verdict, so flagging it there would mislead rather than clarify.
            SignatureCacheDesynced = verification == ChatVerification.Failed
                && (context.SignatureCache?.IsDesynced ?? false),
        }).ConfigureAwait(false);
    }

    /// <summary>Warns exactly once, the moment <paramref name="context"/>'s shared signature cache transitions from trusted to desynced, naming the cause. Compares <paramref name="wasDesyncedBefore"/> (captured by the caller before whatever call might have triggered the transition) against the cache's CURRENT state, so it fires only on the call that actually flips <see cref="Protocol.Java.Signing.MessageSignatureCache.IsDesynced"/> from false to true -- every later invocation, from any trigger, is a silent no-op.</summary>
    private static void LogSignatureCacheDesyncOnce(ApplierContext context, bool wasDesyncedBefore)
    {
        if (wasDesyncedBefore || context.SignatureCache?.IsDesynced != true)
            return;

        context.Logger.LogWarning(
            "Chat signature cache desynced for this connection (cause: {Reason}). Chat signature " +
            "verification is now degraded for every sender for the rest of this session with no " +
            "in-session recovery: per Umpk.Protocol.Java.Signing.MessageSignatureCache.IsDesynced, the " +
            "very next message from the peer that triggered this -- even one with a fully valid inline " +
            "signature -- is expected to also fail, because the chain index a declined message should " +
            "have advanced never does. Messages are still delivered and there is no false accept.",
            context.SignatureCache.DesyncReason ?? "unknown");
    }

    private static async ValueTask ApplyLegacyTitleAsync(ClientboundLegacyTitlePacket packet, ApplierContext context)
    {
        switch (packet.Action)
        {
            case LegacyTitleAction.Title when packet.Text is not null:
                await context.PublishAsync(new TitleChanged(packet.Text)).ConfigureAwait(false);
                break;
            case LegacyTitleAction.Subtitle when packet.Text is not null:
                await context.PublishAsync(new SubtitleChanged(packet.Text)).ConfigureAwait(false);
                break;
            case LegacyTitleAction.Times:
                await context.PublishAsync(new TitleTimesChanged(packet.FadeIn, packet.Stay, packet.FadeOut)).ConfigureAwait(false);
                break;
            case LegacyTitleAction.Clear:
            case LegacyTitleAction.Reset:
                await context.PublishAsync(new TitlesCleared(packet.Action == LegacyTitleAction.Reset)).ConfigureAwait(false);
                break;
        }
    }
}
