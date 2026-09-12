using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Text;

namespace Umpk.Client.Internal;

/// <summary>Bridges an inbound <see cref="ClientboundPlayerChatPacket"/> (signing era 759-763) to the session-side <see cref="SignedChatVerifier"/>. The verifier owns the per-peer chain state and the v1/v2/v3 payload reconstruction; this helper only maps the decoded packet fields onto its <see cref="SignedChatVerifier.Verify"/> call and picks the display component. Verification never throws: an unsigned or unverifiable message returns an outcome, it does not fault the session.</summary>
internal static class SignedChatVerification
{
    /// <summary>The component to display, with the server's chat filter mask applied. Null means the server told the client to show NOTHING (a fully filtered message).</summary>
    /// <remarks>
    /// <para>The mask has three observable outcomes:</para>
    /// <list type="bullet">
    /// <item><description>
    /// A fully filtered mask returns null. The caller must not fall back to the raw text still present in the frame.
    /// </description></item>
    /// <item><description>
    /// An empty mask returns the server's unsigned override when present, otherwise the signed component or signed text.
    /// </description></item>
    /// <item><description>
    /// A partial mask applies formatting to the signed content and ignores the unsigned override because mask indices address the signed characters.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static Component? DisplayContent(ClientboundPlayerChatPacket p, ChatFilterMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (mask.IsFullyFiltered)
            return null;

        if (mask.IsEmpty)
            return p.UnsignedContent
                ?? p.SignedComponent
                ?? Component.Text(p.SignedContent ?? string.Empty);

        return mask.ApplyWithFormatting(SignedText(p));
    }

    /// <summary>The plain-text signed message body used as the verifier input.</summary>
    public static string SignedText(ClientboundPlayerChatPacket p) =>
        p.SignedContent ?? p.SignedComponent?.ToPlainText() ?? string.Empty;

    /// <summary>Resolves <paramref name="p"/>'s last-seen window against the shared signature cache and, only on full success, records this message into the cache: pushes the resolved last-seen signatures plus the message's own signature (<see cref="MessageSignatureCache.Push"/>).</summary>
    /// <remarks>
    /// <para>Call this for every signed <c>player_chat</c> frame, even when no verifier can be resolved for the sender. The cache mirrors what the server broadcast, so its push runs before verifier lookup. If an unresolved sender does not advance it, a later cache-id reference can resolve to the wrong signature and permanently break the peer's verification chain. See <c>PeerChatVerificationTests.SignedChat_UnresolvableSenderMessage_StillAdvancesTheSharedCacheForALaterResolvableSender</c>.</para>
    /// <para>A v3 last-seen entry rides the wire either as a full 256-byte signature or, when the server's own per-connection <c>MessageSignatureCache</c> already holds that signature (routinely true from the second message onward), as a small cache-id reference. The sender signed over the REAL bytes, so a receiver that cannot resolve the reference back to those bytes reconstructs a payload the sender never signed. Dropping entries without inline bytes corrupts both the entry count and the content of the reconstructed last-seen list as soon as an entry is cache-packed.</para>
    /// <para>Each entry keeps the profile id from the wire because v2 hashes every entry's own UUID into the signed body. Substituting the current message's sender would produce the wrong body digest. On v3 the field is <c>Guid.Empty</c> and unread: that generation's last-seen structure has no profile id on the wire and its payload builder hashes only the entry count and each entry's signature bytes. v2 never carries a cache-id entry at all (see <c>WriteLastSeenV2</c>/<c>ReadLastSeenV2</c>), so <c>entry.FullSignature</c> is always populated there and the cache is never consulted, even though a non-null <paramref name="signatureCache"/> is wired up for v1 and v2 sessions too (see <see cref="Verify"/>'s remarks).</para>
    /// <para>A vanilla-produced unsigned <c>player_chat</c> frame carries an empty last-seen window, so the cache update is a no-op and the server skips its own push for an unsigned message. Returning the empty list without touching the cache is therefore equivalent. Only a non-vanilla server sending an unsigned frame with a non-empty window would diverge, including on the vanilla client.</para>
    /// </remarks>
    /// <returns>The resolved last-seen list; empty when the frame carried none, including every unsigned frame; or null when an entry could not be resolved at all (see <see cref="MessageSignatureCache.IsDesynced"/>, which this marks true before returning null).</returns>
    public static IReadOnlyList<AcknowledgedMessage>? RecordForCache(
        ClientboundPlayerChatPacket p, MessageSignatureCache? signatureCache)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Signature is not { Length: > 0 } signature)
            return [];

        var lastSeen = new List<AcknowledgedMessage>(p.LastSeen.Count);
        var resolvedSignatures = new List<byte[]>(p.LastSeen.Count);
        foreach (PackedMessageSignature entry in p.LastSeen)
        {
            byte[]? full = entry.FullSignature;
            if (full is null && entry.Id != PackedMessageSignature.FullSignatureId)
                full = signatureCache?.Unpack(entry.Id);

            if (full is not { Length: > 0 })
            {
                // A missing entry makes every later cache slot unreliable. Chat verification is advisory, so the session stays connected, but this frame cannot be verified. Mark the shared cache desynced to prevent another sender from resolving a later reference to the wrong bytes.
                signatureCache?.MarkDesynced("unresolvable last-seen entry");
                return null;
            }

            lastSeen.Add(new AcknowledgedMessage(entry.ProfileId, full));
            resolvedSignatures.Add(full);
        }

        // Cache every successfully resolved frame before chain validation. Later cache-id references must resolve even when this frame's chain check fails.
        signatureCache?.Push(resolvedSignatures, signature);
        return lastSeen;
    }

    /// <summary>Runs the sender's chain check against an ALREADY-RESOLVED last-seen list from <see cref="RecordForCache"/>. Split out from <see cref="Verify"/> so a caller (<c>ChatApplier</c>) can run the cache resolve-and-record step -- which must happen for every signed frame -- before deciding whether a verifier is even resolvable for the sender, instead of only inside one call that bundles both. Returns <see cref="ChatVerificationStatus.UnsignedChat"/> when the frame carries no signature, never throwing.</summary>
    public static ChatVerificationOutcome VerifyResolved(
        ClientboundPlayerChatPacket p, SignedChatVerifier verifier, DateTimeOffset now,
        IReadOnlyList<AcknowledgedMessage> resolvedLastSeen)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(resolvedLastSeen);
        if (p.Signature is not { Length: > 0 } signature)
            return new ChatVerificationOutcome(ChatVerificationStatus.UnsignedChat, false, "chat.disabled.missingProfileKey");

        // v2 signs its wire-provided preceding signature and decorated component. Both are absent in v1 and v3, so they can be passed through unconditionally.
        return verifier.Verify(
            SignedText(p),
            DateTimeOffset.FromUnixTimeMilliseconds(p.TimestampMillis),
            p.Salt,
            resolvedLastSeen,
            signature,
            p.PreviousSignature,
            now,
            p.SignedComponentJson);
    }

    /// <summary>Verifies the packet's signature against a peer verifier: <see cref="RecordForCache"/> then <see cref="VerifyResolved"/> in one call. Returns <see cref="ChatVerificationStatus.UnsignedChat"/> when the frame carries no signature (offline / system-routed chat) or when its last-seen window cannot be resolved, never throwing.</summary>
    /// <remarks>A convenience for a caller (or test) that is not choosing whether to look up a verifier first, e.g. because it already has one in hand. The live <c>ChatApplier</c> does NOT use this: it must call <see cref="RecordForCache"/> before resolving a verifier for the sender, so this convenience method cannot preserve cache order on that path.</remarks>
    /// <param name="p">The inbound packet.</param>
    /// <param name="verifier">The sender's chain verifier.</param>
    /// <param name="now">The current time, for the key-expiry check.</param>
    /// <param name="signatureCache">The session's shared, per-CONNECTION signature cache (vanilla <c>MessageSignatureCache</c>; see <see cref="MessageSignatureCache"/>), resolving v3 last-seen entries the wire sent as a compact cache-id reference instead of full bytes. One instance must be shared across every peer's <see cref="SignedChatVerifier"/> on the connection, not scoped per sender: the server's cache (and this mirror of it) is filled by every sender's traffic, not just this one's. The live client wires a non-null cache for EVERY signing era a session negotiates (v1, v2, v3 alike -- see <c>UmpkClient._chatSignatureCache</c>), even though v1 carries no last-seen window at all and v2 entries are always full by construction, so this parameter is functionally consulted only on v3. Passing null here (a caller that has not wired one up) means any v3 cache-id entry simply cannot be resolved; see <see cref="RecordForCache"/>.</param>
    public static ChatVerificationOutcome Verify(
        ClientboundPlayerChatPacket p, SignedChatVerifier verifier, DateTimeOffset now,
        MessageSignatureCache? signatureCache = null)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        if (p.Signature is not { Length: > 0 })
            return new ChatVerificationOutcome(ChatVerificationStatus.UnsignedChat, false, "chat.disabled.missingProfileKey");

        if (RecordForCache(p, signatureCache) is not { } lastSeen)
            return new ChatVerificationOutcome(ChatVerificationStatus.UnsignedChat, false, "chat.disabled.missingProfileKey");

        return VerifyResolved(p, verifier, now, lastSeen);
    }
}
