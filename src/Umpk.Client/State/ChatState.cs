namespace Umpk.Client.State;

/// <summary>The session's inbound chat-stream bookkeeping: the running global chat index (1.21.5+) and what the client has observed about the server's ordering of it.</summary>
/// <remarks>
/// <para>From 1.21.5 (protocol 770) every clientbound <c>player_chat</c> frame leads with a <c>globalIndex</c> VarInt. It is a PER-CONNECTION monotone counter that the server starts at 0 and increments once per player-chat packet it sends to that connection. It is not the sender's message index (that is the separate <c>index</c> field, which is per-sender and feeds the signature chain).</para>
/// <para>The standard client mirrors that counter and treats a mismatch as fatal. The counter resets only on login, so it spans respawns and dimension changes but restarts on a re-join.</para>
/// <para>UMPK mirrors the counter and the detection, and deliberately does NOT disconnect. Two reasons, and both are about this client's purpose rather than about disagreeing with vanilla. First, the whole point of the check for a headless long-lived session is that chat-driven automation must not silently operate on an incomplete stream; a host that learns a message was dropped can re-query, warn, or stop, whereas a killed session loses the very automation that would have reacted. Second, a wrong reset point turns this check into a session-fatal false positive, and killing a connection is the one outcome that cannot be walked back; reporting is safe under that uncertainty and still exposes the fact. The observed gap is therefore surfaced twice: as a warning through the session logger and as an <c>Umpk.Client.Events.ChatStreamGap</c> event, and <see cref="ObservedGaps"/> counts them for the life of the join.</para>
/// </remarks>
public sealed class ChatState
{
    /// <summary>The global index the next inbound <c>player_chat</c> is expected to carry. Always 0 on a protocol that carries no global index.</summary>
    public int NextGlobalIndex { get; private set; }

    /// <summary>How many inbound <c>player_chat</c> frames on this join carried a global index other than the expected one, i.e. how many times the server's chat stream was observed to be missing a message or out of order. Nonzero means at least one chat message this session was not delivered in order, and on a well-behaved vanilla server it is always 0.</summary>
    public int ObservedGaps { get; private set; }

    /// <summary>Whether this session's protocol carries a global index at all (1.21.5 / protocol 770 and up). When false, <see cref="NextGlobalIndex"/> and <see cref="ObservedGaps"/> stay at 0, because there is nothing on the wire to check.</summary>
    public bool TracksGlobalIndex { get; internal set; }

    /// <summary>
    /// Whether this session's protocol can tell the server about a profile key it was not given at login, i.e. whether a due profile-key refresh can ever be acted on. True from 1.19.3 (protocol 761), where <c>minecraft:chat_session_update</c> exists; false on 1.19 and 1.19.1, whose serverbound PLAY tables have no packet carrying a profile key, and false on every non-signing version.
    /// <para>When false and <see cref="ProfileKeyRefreshedAfter"/> has passed, the client deliberately keeps signing with the login-bound key rather than rotating to one the server can never verify. That is the honest reading of this pair: a refusal to rotate, not a successful rotation.</para>
    /// </summary>
    public bool ProfileKeyRotationSupported { get; internal set; }

    /// <summary>How many times this session replaced its profile key and announced the replacement with <c>chat_session_update</c>. Each rotation starts a new chat session id and restarts the signed message index at 0. Always 0 when <see cref="ProfileKeyRotationSupported"/> is false.</summary>
    public int ProfileKeyRotations { get; private set; }

    /// <summary>The instant after which the currently installed profile key is due for refresh. Null when the session holds no certificates. This is a refresh WINDOW opening, not an expiry: the key stays valid and verifiable until <c>expiresAt</c>.</summary>
    public DateTimeOffset? ProfileKeyRefreshedAfter { get; internal set; }

    /// <summary>
    /// Whether the server has told this session that it previews (and therefore may DECORATE) chat, through <c>minecraft:set_display_chat_preview</c>. Only protocols 759 and 760 carry that packet at all, so this is always false everywhere else.
    /// <para>When true, UMPK's outbound signature covers the message's PLAIN text and not the server's decoration, because UMPK drives no chat-preview round trip. The server accepts that: The signed-message model signs the plain form and carries the decorated one as unsigned content when the preview flag is false. It is exactly what a vanilla client configured with <c>options.chatPreview: OFF</c> puts on the wire, and vanilla describes the consequence in that option's own tooltip: modifications a server applies "will not be previewed and will be treated as insecure". This property is the only thing that varies, so it is the one to branch on: whether UMPK can sign a preview does not, and is no.</para>
    /// </summary>
    public bool ServerPreviewsChat { get; internal set; }

    /// <summary>Counts one announced profile-key rotation.</summary>
    internal void RecordProfileKeyRotation() => ProfileKeyRotations++;

    /// <summary>Clears the four connection-scoped chat facts at the end of a connection: the profile-key trio and the server's chat-preview setting. Each describes ONE session on ONE negotiated version, and the coordinator that owns them is rebuilt on every connect, so carrying them across a reconnect would report a rotation count, a refresh instant or a preview answer belonging to a connection the new session is not, on a version that may not even permit rotation or carry the preview packet.</summary>
    internal void ResetProfileKeyState()
    {
        ProfileKeyRotationSupported = false;
        ProfileKeyRotations = 0;
        ProfileKeyRefreshedAfter = null;

        // Same reason: the preview toggle belongs to ONE server on ONE negotiated version, and reporting a previous connection's answer after a reconnect is a stale claim about the live session.
        ServerPreviewsChat = false;
    }

    /// <summary>Records an inbound frame's global index and advances the expected counter, exactly as vanilla's <c>int expected = this.nextChatIndex++</c> does (the counter advances whether or not the value matched, so one dropped message produces one gap report and not one per message afterwards). Returns true when the value was the expected one.</summary>
    /// <param name="globalIndex">The index the frame carried.</param>
    /// <param name="expected">The index the client expected.</param>
    internal bool ObserveGlobalIndex(int globalIndex, out int expected)
    {
        expected = NextGlobalIndex++;
        if (globalIndex == expected)
            return true;

        // Resynchronise on what the server actually said. Vanilla has no need to: it has already dropped the connection. Continuing from the server's value means a single missing message costs one report rather than making every later message look out of order too.
        NextGlobalIndex = globalIndex + 1;
        ObservedGaps++;
        return false;
    }

    /// <summary>Resets the counter for a fresh game join, which is its only reset point.</summary>
    internal void ResetForJoin(bool tracksGlobalIndex)
    {
        NextGlobalIndex = 0;
        ObservedGaps = 0;
        TracksGlobalIndex = tracksGlobalIndex;
    }
}
