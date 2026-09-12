using Microsoft.Extensions.Logging;
using Umpk.Client.Actions;
using Umpk.Client.State;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;

namespace Umpk.Client.Internal;

/// <summary>Owns a live session's chat-signing state. Created once per session after login with the sender profile id, a fresh chat session id, and the version's signature era; it constructs the <see cref="ChatSigningSession"/> and resolves the player certificates from the host-supplied <see cref="IChatSigningProvider"/>, caching the resulting <see cref="ChatSigningState"/>.</summary>
/// <remarks>
/// <para><see cref="EnsureAsync"/> is the seam the send path awaits: it returns the cached state when the certificates are still current, installs a prefetched replacement when one is waiting, and returns null when no certificates are available so the caller falls back to the unsigned path.</para>
/// <para><see cref="BeginRefreshIfDue"/> is the tick seam. It only FETCHES, off the session loop, and changes no signing state. Installing the fetched key and announcing it happen on the send path under this type's lock, so the <c>chat_session_update</c> frame can never land after a message that was already signed with the outgoing key. See <see cref="ReplaceCertificatesAsync"/> for why every replacement is refused outright on the pre-1.19.3 eras.</para>
/// </remarks>
internal sealed class ChatSigningCoordinator
{
    /// <summary>The minimum interval between two certificate fetches, whatever the answer was. It is armed before the fetch starts, so a failing endpoint is retried once an hour rather than once a tick.</summary>
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromHours(1);

    /// <summary>The ceiling on one certificate fetch, on either path. UMPK's provider is host-supplied and the send path awaits it while holding <see cref="_lock"/>, so the fetch gets a real deadline as well as a real cancellation token. On the deadline the session keeps the certificates it already has, which is what vanilla's failure arm does (<c>readOrFetchProfileKeyPair</c> returns the previous optional).</summary>
    /// <remarks>Uses the session <see cref="TimeProvider"/> so both fetch paths share one clock and virtual-time tests can reach the deadline without wall-clock delay.</remarks>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);

    private readonly ChatSignatureEra _era;
    private readonly Guid _sender;
    private readonly LastSeenMessagesTracker? _tracker;
    private readonly LastSeenMessagesCollector? _legacyCollector;
    private readonly IChatSigningProvider _provider;
    private readonly IPacketSink? _sink;
    private readonly ChatState? _chatState;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private ChatSigningSession _session;
    private ChatSigningState? _current;
    private PlayerCertificates? _prefetched;

    /// <summary>
    /// The most recent answer the provider gave on either fetch path, whether or not it was announced and installed.
    /// <para>This preserves the certificates when <c>chat_session_update</c> fails, allowing a later send to retry the announcement without another provider call or an hour of unsigned chat.</para>
    /// </summary>
    private PlayerCertificates? _resolved;

    /// <summary>
    /// The single fetch throttle, as <see cref="DateTimeOffset.UtcTicks"/>, shared by the tick path (<see cref="BeginRefreshIfDue"/>) and the send path (<see cref="ResolveAsync"/>).
    /// <para>A <see cref="long"/> rather than a <see cref="DateTimeOffset"/> because the two writers are on different threads: the tick path runs on the session loop and the send path runs under <see cref="_lock"/> on the caller's thread. A <see cref="DateTimeOffset"/> is two fields and cannot be read or written atomically, so a torn read would give an instant that is neither the old nor the new one, which on this field means either "never fetch again" or "fetch on every send". Ticks are one <see cref="long"/> and <see cref="Volatile"/> makes both ends exact.</para>
    /// </summary>
    private long _nextFetchAtUtcTicks;

    private int _refreshing;
    private bool _pastConnect;
    private bool _loggedRotationRefusal;
    private bool _loggedExpiredKeyInUse;

    /// <summary>Whether the current expiry episode has already spent its one out-of-band recovery attempt. 0 (not yet spent) until the first <see cref="FetchOnSendAsync"/> call while holding an expired certificate claims it via compare-exchange; reset to 0 only when <see cref="Install"/> installs a certificate that is not itself already expired, which is what ends an episode. See <see cref="FetchOnSendAsync"/>'s remarks for why one attempt, not one per send, is the bound.</summary>
    private int _expiryBonusUsed;

    /// <summary>Creates the session's signing coordinator.</summary>
    /// <param name="sender">The local profile id every signature is rooted at.</param>
    /// <param name="sessionId">The chat session id announced for this join.</param>
    /// <param name="era">The negotiated version's signature era.</param>
    /// <param name="provider">The host's certificate source.</param>
    /// <param name="time">The session clock (expiry, refresh window, throttle).</param>
    /// <param name="logger">The session logger.</param>
    /// <param name="sink">The outbound sink for rotation announcements. Null disables rotation entirely: a key that cannot be announced must not be swapped in.</param>
    /// <param name="chatState">Optional state object receiving the observable signing counters.</param>
    /// <param name="loginCertificates">The certificates the login hello already announced, on the eras that announce one there (1.19 and 1.19.1). Seeding them makes the key this coordinator signs with the SAME object the server was given, rather than a second, independently fetched answer from the provider.</param>
    public ChatSigningCoordinator(
        Guid sender,
        Guid sessionId,
        ChatSignatureEra era,
        IChatSigningProvider provider,
        TimeProvider time,
        ILogger logger,
        IPacketSink? sink = null,
        ChatState? chatState = null,
        PlayerCertificates? loginCertificates = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _sender = sender;
        _session = new ChatSigningSession(sender, sessionId);
        _era = era;
        // Only the 1.19.3+ (v3) era uses the offset/bitset last-seen acknowledgement window.
        _tracker = era == ChatSignatureEra.V1_19_3 ? new LastSeenMessagesTracker() : null;
        // 1.19.1/1.19.2 (v2) uses the older 5-entry add-to-front window instead: its signed body folds the acknowledged (uuid, signature) pairs in, and the same pairs ride the packet. 1.19 (v1) acknowledges nothing at all, so it gets neither.
        _legacyCollector = era == ChatSignatureEra.V1_19_1
            ? new LastSeenMessagesCollector(LastSeenMessagesCollector.Window1_19)
            : null;
        _provider = provider;
        _sink = sink;
        _chatState = chatState;
        _time = time;
        _logger = logger;
        if (_chatState is not null)
            _chatState.ProfileKeyRotationSupported = CanAnnounceRotation;

        if (loginCertificates is not null)
        {
            // These came from the provider too (UmpkClient.ConnectAsync fetches them for the login hello), so they are the newest provider answer this coordinator knows about.
            _resolved = loginCertificates;
            Install(_session, loginCertificates);
        }
    }

    /// <summary>The 1.19.3+ last-seen acknowledgement tracker, shared between the inbound collection point (the chat applier) and the outbound signed-chat send. Null on the 1.19/1.19.1 eras, which do not use it.</summary>
    public LastSeenMessagesTracker? Tracker => _tracker;

    /// <summary>The 1.19.1/1.19.2 last-seen collector, shared between the inbound collection point (the chat applier) and the outbound signed chat and command sends. Null on every other era.</summary>
    public LastSeenMessagesCollector? LegacyCollector => _legacyCollector;

    /// <summary>Whether this era has any way to tell a server about a profile key it was not given at login, and therefore whether the client may ever replace its key mid-session.</summary>
    /// <remarks>True only on 1.19.3+ (protocol 761 and up), where <c>minecraft:chat_session_update</c> exists. Protocols 759 and 760 have no play-phase session-update packet; their only serverbound profile key is carried during login. The verified key is therefore fixed for the connection lifetime.</remarks>
    public bool CanAnnounceRotation => _era == ChatSignatureEra.V1_19_3;

    /// <summary>Resolves the join's certificates and announces them with <c>chat_session_update</c>, so an enforce-secure-profile server binds the session before any signed chat reaches it. No-op unless the era is 1.19.3+ and certificates are available.</summary>
    /// <remarks>
    /// <para>The announcement happens inside <see cref="ResolveAsync"/>, under the same lock as installation and before the key becomes available to senders. A host thread therefore cannot put a signed message on the wire before a 1.19.3+ <c>enforce-secure-profile</c> server receives the session update. If the announcement fails, the unannounced key is not installed.</para>
    /// </remarks>
    public async ValueTask SendSessionUpdateAsync(CancellationToken ct)
    {
        try
        {
            if (!CanAnnounceRotation)
                return;

            await EnsureAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // The connect-time announcement opportunity is now over, whether or not it produced a frame, so every later install has to announce itself as a rotation. Written under the lock: it is read under the lock in ResolveAsync, and a stale read of false there would take the connect-install arm, which is the one arm that does not rotate the session id.
            await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                _pastConnect = true;
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <summary>Vanilla's per-tick profile-key refresh check, reduced to the part that is safe to run off the session loop: it decides whether a fetch is due and starts one, and it does nothing else. Returns the running fetch task when one was started (tests await it) or null when nothing was due. The session loop must NOT await the result.</summary>
    /// <remarks>
    /// <list type="number">
    /// <item><description>
    /// <b>Era gate first.</b> Nothing is fetched unless the era can announce the replacement, so the pre-1.19.3 eras never even acquire a key they could accidentally start signing with.
    /// </description></item>
    /// <item><description>
    /// <b>Off the loop.</b> The provider is host code doing network I/O; it runs on the thread pool, with a linked token and a deadline, so a hung endpoint cannot stall physics, the position cadence or inbound packet application.
    /// </description></item>
    /// <item><description>
    /// <b>Throttle before the fetch.</b> <see cref="_nextFetchAtUtcTicks"/> covers both missing and due certificates, so a failing provider costs at most one scheduled call per hour instead of one per tick. <see cref="TryArmThrottle"/> uses compare-exchange because the send path shares this window. <see cref="FetchOnSendAsync"/> documents its one bounded expiry bonus.
    /// </description></item>
    /// </list>
    /// </remarks>
    public Task? BeginRefreshIfDue(CancellationToken ct)
    {
        if (!CanAnnounceRotation || _sink is null)
            return null;

        DateTimeOffset now = _time.GetUtcNow();
        if (now.UtcTicks < Volatile.Read(ref _nextFetchAtUtcTicks))
            return null;

        // Vanilla should refresh key pair behavior: due when the held key pair is past its refreshedAfter instant, and due unconditionally when there is no key pair at all (the .orElse(true) arm).
        ChatSigningState? currentState = Volatile.Read(ref _current);
        bool isDue = currentState is null || currentState.Certificates.RefreshedAfter < now;
        if (!isDue || Volatile.Read(ref _prefetched) is not null)
            return null;

        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            return null;

        // Armed BEFORE the fetch, and with a compare-exchange rather than a plain write, so a send resolving on another thread cannot read the window open in the gap and take a second call in the same window.
        if (!TryArmThrottle(now))
        {
            Volatile.Write(ref _refreshing, 0);
            return null;
        }

        return Task.Run(() => FetchForRotationAsync(ct), CancellationToken.None);
    }

    /// <summary>Runs one chat or command send with the signing state resolved, holding this coordinator's lock across the WHOLE of resolve, sign and send. This is the seam the send path must use.</summary>
    /// <remarks>
    /// <para>The lock has to span the send, not just the resolve, and that is the entire point of this method. A rotation replaces the chat session id, the key and the message index together, and announces the replacement with <c>chat_session_update</c>. If a sender were allowed to resolve the state, drop the lock, and only then sign and send, this interleaving is available on any host that sends chat from two threads (which <c>JavaConnection.SendAsync</c> explicitly supports):</para>
    /// <list type="number">
    /// <item><description>A resolves S1 = (session1, K1) and is descheduled.</description></item>
    /// <item><description>The tick's fetch completes and offers K2.</description></item>
    /// <item><description>B resolves, rotates, announces (session2, K2), signs at index 0, sends.</description></item>
    /// <item><description>A resumes and sends a message signed with (session1, K1).</description></item>
    /// </list>
    /// <para>A's message then arrives after the announcement, and the 1.19.2 server disconnects on a signature it cannot verify. Serialising resolve-sign-send removes the window by construction: the rotation cannot commit while any sender holds the lock, and no sender can hold a state older than the announcement it follows.</para>
    /// </remarks>
    public async ValueTask RunOrderedSendAsync(
        Func<ChatSigningState?, CancellationToken, ValueTask> send, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(send);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ChatSigningState? state = await ResolveAsync(ct).ConfigureAwait(false);
            await send(state, ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Returns the current signing state, installing a prefetched replacement key first when one is waiting and refetching when the certificates have expired. Null means no certificates are available and the message must be sent unsigned.</summary>
    /// <remarks>This deliberately has NO lock-free fast path. A caller that reads the state outside the lock can sign with a key that has since been retired and put that message on the wire behind the announcement of its replacement, which is a disconnect (see <see cref="RunOrderedSendAsync"/>). The send path must go through <see cref="RunOrderedSendAsync"/>; this entry point exists for the connect-time announcement, which runs before the tick loop starts.</remarks>
    public async ValueTask<ChatSigningState?> EnsureAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ResolveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Resolves the current signing state. THE CALLER MUST HOLD <see cref="_lock"/>: this installs a prefetched rotation, which sends a frame, and refetches on expiry.</summary>
    /// <remarks>This method no longer decides on its own whether a candidate certificate is USABLE - that question, and the fresh clock read it needs, belongs entirely to <see cref="ReplaceCertificatesAsync"/> now (see that method's remarks: it is the single choke point). What remains here is: which candidate to try (the prefetch, or a fresh fetch), whether it is even worth trying (a staler prefetch than what is live, or nothing fetched at all), and which session identity a successful install should use (the connect-time join keeps <see cref="_session"/>; everything else lets <see cref="ReplaceCertificatesAsync"/> choose).</remarks>
    private async ValueTask<ChatSigningState?> ResolveAsync(CancellationToken ct)
    {
        // A rotation prefetched by the tick is installed HERE, under the same lock the caller then signs and sends inside, so the announcement frame cannot be overtaken by a message signed with the outgoing key. Doing the swap on the tick instead would leave exactly that window open.
        if (_prefetched is { } prefetched)
        {
            _prefetched = null;
            if (IsStalerThanCurrent(prefetched))
            {
                // A tick fetch that STARTED before a send-path recovery can still COMPLETE after it, because nothing serialises the two fetches against each other. Taking this answer anyway would install and announce a certificate older than the one already live, rotating the session id backwards onto (in the case that motivated this guard) an already-expired key. The candidate is simply dropped: whatever it would have offered is moot now that something newer is already installed, and the tick will fetch again on its own schedule if a refresh is still genuinely due.
                _logger.LogDebug(
                    "Dropped a stale prefetched profile key (expires {PrefetchedExpiresAt:O}); the "
                    + "installed key already expires no earlier ({CurrentExpiresAt:O}).",
                    prefetched.ExpiresAt,
                    _current!.Certificates.ExpiresAt);
            }
            else
            {
                // ReplaceCertificatesAsync is the choke point every install passes through: it refuses an already-expired candidate outright, before announcing or installing anything, so a prefetched certificate that died in the field while this session sent no chat is silently dropped there - no separate expiry check is duplicated here for it.
                await ReplaceCertificatesAsync(prefetched, ct).ConfigureAwait(false);
            }
        }

        ChatSigningState? current = _current;
        if (current is not null && !current.Certificates.IsExpired(_time.GetUtcNow()))
            return current;

        // Past this point any new certificates would REPLACE what the server already knows about, so on an era that cannot announce a replacement there is nothing legitimate left to do. Two sub-cases, both refusals, and both fall out of just returning `current` (null in the second):
        //
        // Either the key expired (current is not null here), and keeping it is the strictly better of the two dead options. The 1.19.2 server drops a message carrying an expired key but keeps the session alive; an invalid signature disconnects. So an expired key costs the messages, while an unannounced replacement costs the session.
        //
        // Or nothing was ever resolved for this session (current is null): on 1.19/1.19.1 the profile key reaches the server in the LOGIN HELLO and nowhere else, so a key first acquired here was never announced, so signing with it would create an unverifiable session. UmpkClient seeds the login certificates through the constructor precisely so this arm is only reached when the login-time fetch failed or returned nothing, and there is no fetch to retry here: this era never calls the provider again once the login-time attempt is spent.
        if (!CanAnnounceRotation)
        {
            LogRotationRefusalOnce();
            return current;
        }

        // Which of the two unusable states we are in decides how the fetch is bounded. See FetchOnSendAsync.
        bool holdingExpired = current is not null;
        PlayerCertificates? certificates = await FetchOnSendAsync(holdingExpired, ct).ConfigureAwait(false);
        if (certificates is null)
        {
            // Nothing came back at all (throttled with nothing cached, or the provider genuinely answered with nothing) - there is no candidate to hand ReplaceCertificatesAsync, so this method still owns warning about it directly.
            if (holdingExpired)
                WarnExpiredKeyInUse(current!.Certificates.ExpiresAt);

            return current;
        }

        // The connect-time join keeps the session the client was CONSTRUCTED with; every other case lets ReplaceCertificatesAsync choose (a same-key refresh keeps the installed session, a genuine rotation gets a fresh one). Either way, ReplaceCertificatesAsync is the ONLY place that decides whether `certificates` is usable - see its remarks - and it warns internally when it refuses.
        bool installed = current is null && !_pastConnect
            ? await ReplaceCertificatesAsync(certificates, ct, connectSession: _session).ConfigureAwait(false)
            : await ReplaceCertificatesAsync(certificates, ct).ConfigureAwait(false);
        return installed ? _current : current;
    }

    /// <summary>The send path's certificate fetch. Returns usable certificates, or null when there is no usable answer, in which case the caller keeps whatever is already installed. THE CALLER MUST HOLD <see cref="_lock"/>.</summary>
    /// <param name="holdingExpired">True when the caller already holds certificates and they have EXPIRED; false when it holds none at all. This decides whether the FIRST call of an episode may jump the shared floor; every later call is bounded by the SAME floor regardless.</param>
    /// <param name="ct">The caller's token. Its cancellation propagates; the deadline's does not.</param>
    /// <remarks>
    /// <para>This call is only ever reached from the two states in which nothing usable is installed: no certificates at all, or expired ones (<see cref="ResolveAsync"/> returns before here whenever what it holds still verifies).</para>
    /// <para><b>Both states share one hourly floor.</b> The floor is armed before the fetch whether the held key is missing or expired. This prevents a failed certificate endpoint from turning each chat send into another authenticated network request. The server may drop messages signed with an expired key without disconnecting; that response does not change the client's fetch-rate bound.</para>
    /// <para><b>One bonus attempt per expiry episode.</b> The first send after expiry may fetch even while the hourly floor is closed. <see cref="_expiryBonusUsed"/> is claimed atomically, and claiming it also consumes the shared floor so later sends in the episode remain throttled.</para>
    /// <para>A scheduled tick and the expiry bonus can both fetch in one window. The durable bound is at most one fetch per successful installation of a non-expired certificate, plus one per hour while none exists. Short-lived certificates can legitimately cause more frequent fetches because each result is installed and then expires before the next send.</para>
    /// <para><b>A provider failure must never fault the send.</b> <see cref="AbandonAsync"/> contains every exception the provider can produce, not only cancellation, and degrades to <see cref="UsableResolved(DateTimeOffset)"/> instead of letting it propagate. A transient provider failure therefore cannot make every <c>SendChatAsync</c> call fail while an expired key remains installed.</para>
    /// <para><b>A still-dead answer is not success.</b> <see cref="ReplaceCertificatesAsync"/> - the choke point every announcing path passes through - refuses a non-null-but-still-expired answer outright and treats it exactly like no answer: warned, never installed. Keeping this check at the shared choke point ensures every announcing path applies it. A permanently unhelpful provider therefore costs the one bonus call plus one call per hour thereafter while the key stays expired, each bounded by <see cref="FetchTimeout"/> and never able to fault the send, and the session's operator is told about it once per episode via <see cref="WarnExpiredKeyInUse"/> rather than reading a "refreshed" log line for a key that was never alive.</para>
    /// <para><b>What a fired deadline leaves behind.</b> <see cref="_current"/> remains unchanged because only <see cref="Install"/> writes it, and <see cref="AbandonAsync"/> discards the late provider result. <see cref="ReplaceCertificatesAsync"/> sends <c>chat_session_update</c> before installing a new key under the same lock. It may install an equivalent key without another announcement only after <see cref="ProfileKeyMaterial.AnnouncesSameKey"/> proves the server already holds that key. The constructor accepts <c>loginCertificates</c> only for 1.19 and 1.19.1, where the login hello has already announced the key and the protocol cannot announce a replacement.</para>
    /// </remarks>
    private async ValueTask<PlayerCertificates?> FetchOnSendAsync(bool holdingExpired, CancellationToken ct)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool allowed = holdingExpired ? TryUseExpiryBonusOrThrottle(now) : TryArmThrottle(now);
        if (!allowed)
        {
            // Throttled, on either path. Hand back the last answer the provider gave so a join whose announcement threw can retry without a second call (see _resolved), or so an expired key whose bonus is spent waits out the same floor vanilla does - but only when it is still usable. An expired certificate handed back here would be signed with, and the server drops every such message while this side reports success.
            return UsableResolved(now);
        }

        // AbandonAsync now owns ALL of the exception handling below this point, INCLUDING a cooperative provider observing the deadline's token: it degrades to UsableResolved internally rather than letting anything but the caller's OWN cancellation propagate. A bare try/catch here would be dead code.
        PlayerCertificates? certificates = await AbandonAsync(ct).ConfigureAwait(false);
        if (certificates is null)
        {
            // Either the deadline was reached, the provider answered with nothing, or the provider FAILED (AbandonAsync contains the exception and returns null too). Vanilla's failure arm keeps the key it already had rather than dropping to unsigned (readOrFetchProfileKeyPair returns the previous optional), and so does this: the caller signs with whatever is installed, or sends unsigned when nothing is - never faulted.
            return UsableResolved(now);
        }

        Volatile.Write(ref _resolved, certificates);
        return certificates;
    }

    /// <summary>Whether an expired-key send may fetch right now: either it is the FIRST send of this expiry episode - the one-shot recovery attempt the whole redesign in <see cref="FetchOnSendAsync"/> exists to grant - or the shared hourly floor has genuinely reopened since. Claiming the bonus also consumes the floor, but with a plain write rather than a compare-exchange (see the remarks below), so this does NOT guarantee the tick path's own fetch cannot ALSO land inside the same window, only that this bonus itself is claimed at most once. The true, honestly-stated bound lives on <see cref="FetchOnSendAsync"/>'s remarks: at most one fetch per successful install of a non-expired certificate, plus one per hour otherwise.</summary>
    /// <remarks>The <see cref="Volatile.Write(ref long, long)"/> that arms <see cref="_nextFetchAtUtcTicks"/> here is deliberately NOT a compare-exchange against the tick path's own arming in <see cref="TryArmThrottle"/>: whichever of the two runs second simply overwrites the other's armed instant with its own, and if the tick happens to arm the floor first while this bonus is still unspent, both can fetch inside that one window. The compare-exchange on <see cref="_expiryBonusUsed"/> still prevents one episode from spending its bonus twice. The shared-floor write is therefore best-effort rather than exclusive.</remarks>
    private bool TryUseExpiryBonusOrThrottle(DateTimeOffset now)
    {
        if (Interlocked.CompareExchange(ref _expiryBonusUsed, 1, 0) == 0)
        {
            // Only the caller that WON the compare-exchange reaches this line, so a plain write cannot race a concurrent claim of the same bonus. It still shares the field the throttle itself uses (Volatile, read by TryArmThrottle and BeginRefreshIfDue on other threads).
            Volatile.Write(ref _nextFetchAtUtcTicks, (now + MinimumRefreshInterval).UtcTicks);
            return true;
        }

        return TryArmThrottle(now);
    }

    /// <summary>Whether <paramref name="candidate"/> is older than the installed certificate: it expires strictly earlier than the current value. This rejects a prefetched tick answer that lands after a send-path fetch has already moved <see cref="_current"/> forward. Nothing serialises the two fetches against each other, so an HTTP source with retries or a cache can legitimately return an OLDER certificate from a call that simply took longer. False when nothing is installed yet, since there is nothing to be staler than.</summary>
    /// <remarks>Strictly earlier, not "no later than": a candidate whose <c>ExpiresAt</c> exactly matches what is installed is the ordinary same-key refresh <see cref="ReplaceCertificatesAsync"/>'s own guard exists for: re-exported PEM text, an unchanged key, or a recomputed <c>RefreshedAfter</c>. Such a candidate must still reach that guard so <c>RefreshedAfter</c> can advance and <c>BeginRefreshIfDue</c> does not keep scheduling hourly refreshes.</remarks>
    private bool IsStalerThanCurrent(PlayerCertificates candidate)
    {
        ChatSigningState? current = _current;
        return current is not null && candidate.ExpiresAt < current.Certificates.ExpiresAt;
    }

    /// <summary>Awaits the provider with a real deadline and returns even when the provider does not. Null means no answer arrived in time, the provider returned none, or fallback is disabled after a failure. Only the caller's cancellation propagates; provider failures and timeouts are contained here.</summary>
    /// <param name="ct">The caller's token. Its cancellation propagates; nothing else does.</param>
    /// <param name="fallbackToLastKnownGood">Whether a failed fetch, as opposed to one that exceeds <see cref="FetchTimeout"/>, should return <see cref="UsableResolved(DateTimeOffset)"/> instead of null. True (the send path's need: keep signing with whatever is already installed) for every caller except <see cref="FetchForRotationAsync"/>, which passes false: that path writes any non-null answer into <see cref="_prefetched"/> as fresh. Returning the installed key there would block the next scheduled refresh behind a prefetch containing nothing new.</param>
    /// <remarks>
    /// <para>A <see cref="CancellationToken"/> alone does not bound anything. <see cref="IChatSigningProvider"/> does not require an implementation to observe its token, and a valid host that awaits an HTTP call which never returns leaves <c>await GetCertificatesAsync(token)</c> pending forever. The token is still passed so a cooperative provider can stop promptly, while the bound comes from <see cref="Task.WhenAny(Task, Task)"/>: when the delay wins, this method stops waiting and the provider's task is left to finish into nothing.</para>
    /// <para>The abandoned task is not merely dropped: its result is discarded and its fault is observed, so an uncooperative provider that eventually throws cannot surface later as an unobserved task exception on a finalizer thread. Its certificates are discarded too, which is what makes the "a timeout installs nothing" claim true by construction rather than by timing.</para>
    /// <para><b>Every provider exception is caught here, not just cancellation.</b> Certificate HTTP calls can fail with <c>HttpRequestException</c>, <c>TaskCanceledException</c>, or <c>JsonException</c>. These failures degrade to the selected fallback; only the caller's own cancellation propagates.</para>
    /// </remarks>
    private async ValueTask<PlayerCertificates?> AbandonAsync(
        CancellationToken ct, bool fallbackToLastKnownGood = true)
    {
        using var deadline = new CancellationTokenSource(FetchTimeout, _time);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        Task<PlayerCertificates?> fetch;
        try
        {
            // A provider is permitted to throw synchronously, before ever returning a task (rare, but IChatSigningProvider does not forbid it). Contained the same as every other failure.
            fetch = _provider.GetCertificatesAsync(linked.Token).AsTask();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFetchFailed(ex);
            return fallbackToLastKnownGood ? UsableResolved(_time.GetUtcNow()) : null;
        }

        Task expiry = Task.Delay(FetchTimeout, _time, linked.Token);
        Task first = await Task.WhenAny(fetch, expiry).ConfigureAwait(false);
        if (first == fetch)
        {
            // Cancel the delay's timer rather than leaving it to fire, then surface the provider's own result - or contain its exception, whether that is a cooperative provider observing the deadline's token (an OperationCanceledException that is NOT the caller's own) or any other failure the provider can produce.
            await deadline.CancelAsync().ConfigureAwait(false);
            try
            {
                return await fetch.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                LogFetchAbandoned();
                return fallbackToLastKnownGood ? UsableResolved(_time.GetUtcNow()) : null;
            }
            // Both tasks are linked to ct, so either can win the race after caller cancellation. Exclude that cancellation here so it propagates instead of becoming a provider-failure fallback.
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                LogFetchFailed(ex);
                return fallbackToLastKnownGood ? UsableResolved(_time.GetUtcNow()) : null;
            }
        }

        // The deadline won (or the caller cancelled - both make `expiry` finish first; the two are told apart below). Attach fault observation before testing caller cancellation so a provider that faults later cannot surface as an unobserved task exception. A late certificate is also discarded because it has no announcement behind it.
        _ = fetch.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        ct.ThrowIfCancellationRequested();

        LogFetchAbandoned();
        return null;
    }

    /// <summary>Arms the tick path's hourly floor if it is open, atomically. Returns false when it was already closed or when another path won the race to arm it, so exactly one caller per window proceeds.</summary>
    private bool TryArmThrottle(DateTimeOffset now)
    {
        long next = Volatile.Read(ref _nextFetchAtUtcTicks);
        if (now.UtcTicks < next)
            return false;

        long armed = (now + MinimumRefreshInterval).UtcTicks;
        return Interlocked.CompareExchange(ref _nextFetchAtUtcTicks, armed, next) == next;
    }

    /// <summary>The last provider answer, but only when it would still verify. Null otherwise.</summary>
    private PlayerCertificates? UsableResolved(DateTimeOffset now)
    {
        PlayerCertificates? held = Volatile.Read(ref _resolved);
        return held is not null && !held.IsExpired(now) ? held : null;
    }

    private void LogFetchAbandoned() =>
        _logger.LogWarning(
            "The chat-signing provider did not answer within {Timeout}; the request was abandoned and "
            + "the session keeps whatever key it already holds.",
            FetchTimeout);

    /// <summary>Records a provider EXCEPTION, at Debug rather than Warning: the actionable, once-per-episode signal for "this session cannot get a usable key" is <see cref="WarnExpiredKeyInUse"/> (or the ordinary unsigned fallback when nothing was ever held), which fires from every path that reaches here including this one. This line exists only so the actual exception - which <see cref="AbandonAsync"/> never lets fault the send - is still discoverable when debug logging is on, without making every failed send its own Warning-level line.</summary>
    private void LogFetchFailed(Exception ex) =>
        _logger.LogDebug(
            ex, "The chat-signing provider failed; the session keeps whatever key it already holds.");

    /// <summary>Says, once per expiry episode, that this session is signing with a key the server will refuse. Reset by <see cref="Install"/> ONLY when it installs a certificate that is not itself already expired. Installing another dead one, which <see cref="ResolveAsync"/>'s own guard now prevents on every reachable path, must not silently re-arm this for a "recovery" that never happened.</summary>
    /// <remarks>This is the signal whose absence made the throttled-expired path a silent failure. The outcome it names is the signing <c>decoder</c>: <c>chat.disabled.expiredProfileKey</c> with <c>shouldDisconnect</c> false, so the session survives and the messages do not arrive.</remarks>
    private void WarnExpiredKeyInUse(DateTimeOffset expiredAt)
    {
        if (_loggedExpiredKeyInUse)
            return;

        _loggedExpiredKeyInUse = true;

        // Two genuinely different situations reach this warning, and an operator acts on them differently, so do not describe one in the other's words. If a key IS installed we keep signing with it and the server drops those messages; if the choke point refused every candidate we have no key at all and the sends go out UNSIGNED.
        if (Volatile.Read(ref _current) is null)
        {
            _logger.LogWarning(
                "The chat-signing provider offered a profile key that had already expired at "
                + "{ExpiredAt:O}, so it was refused and NO key is installed. Messages are being sent "
                + "UNSIGNED: a server that enforces secure profiles will reject them, and one that does "
                + "not will show them to other players tagged as insecure. The provider was asked once "
                + "outside its normal hourly window to try to recover immediately; having not answered "
                + "with anything usable, it is now asked at most once an hour, the same floor a fresh "
                + "key gets, until it does.",
                expiredAt);
            return;
        }

        _logger.LogWarning(
            "The profile key expired at {ExpiredAt:O} and the chat-signing provider has not replaced it. "
            + "Messages are still being signed with it, and a server will DROP them and show the player "
            + "chat.disabled.expiredProfileKey rather than closing the session. The provider was asked "
            + "once outside its normal hourly window to try to recover immediately; having not answered "
            + "with anything usable, it is now asked at most once an hour, the same floor a fresh key "
            + "gets, until it does.",
            expiredAt);
    }

    /// <summary>THE CHOKE POINT. Every path that can ever put a certificate into <see cref="_current"/> - the send path's own fetch, the tick's prefetched answer, the expiry-episode bonus fetch, and the connect-time join - funnels through this one method, and it refuses an already-expired candidate OUTRIGHT, before announcing OR installing anything. On 1.19.3+ a genuine replacement means a whole new chat session: a fresh session id, a signature chain re-rooted at message index 0, and a <c>chat_session_update</c> on the wire BEFORE anything is signed with the new key. On every earlier era it means doing nothing at all. Returns whether it actually installed something.</summary>
    /// <param name="certificates">The candidate. Refused outright when it is already expired.</param>
    /// <param name="ct">The caller's token.</param>
    /// <param name="connectSession">Non-null ONLY for the connect-time join: the session id the client was CONSTRUCTED with, kept rather than rotated, and no rotation is counted. Every other caller passes null and gets either the session already installed (a same-key refresh) or a freshly randomised one (a genuine rotation).</param>
    /// <remarks>
    /// <para><b>Why the check lives here and not at a caller's return value.</b> A caller-specific guard cannot see candidates arriving through <see cref="_prefetched"/>. Checking expiry here, before either the wire write or <see cref="Install"/>, closes the send path, the tick/prefetch path, the bonus path and the connect-time join by construction: a fifth path added later cannot reintroduce this by forgetting a call-site check that was never its job to remember, because there is no other way into <see cref="Install"/> that carries an announcement.</para>
    /// <para>The clock is sampled FRESH here, not reused from whatever instant the caller fetched at: a certificate that was alive when <see cref="FetchOnSendAsync"/> or <see cref="FetchForRotationAsync"/> returned it can still die during the time between that return and this call actually running and the announcement this method is about to decide on is the one thing that must never go out for a key already known to be dead.</para>
    /// <para>A key rotation requires a new random session id, a signature chain restarted at message index 0, and a <c>chat_session_update</c>. Reusing the old session id or index after a key swap is not an option because the server binds the session id, public key, and index together.</para>
    /// <para>Protocols 1.19 and 1.19.1 have no serverbound packet that can carry a new profile key (see <see cref="CanAnnounceRotation"/>), so a swap there replaces a session the server CAN verify with one it can never verify, while every local signal still says the message was signed. The announcement is therefore not a step after the swap; it is the swap's precondition, and a send that throws leaves the previous state in place because <see cref="Install"/> never runs.</para>
    /// <para><b>The unchanged-key guard uses the server's equality rule.</b> The server compares the decoded expiry, public-key bytes, and key signature. When all three match, it retains the existing chat session id and message index. Those are exactly the fields <see cref="ProfilePublicKeyData"/> puts on the wire.</para>
    /// <para>Record equality is unsuitable because it also includes PEM text and the private key. Semantically identical key material can differ in line wrapping, PKCS#8 encoding, or a trailing newline. Comparing the announcement bytes makes the client's precondition identical to the server's postcondition and prevents the two ends from disagreeing about the session id and message index.</para>
    /// </remarks>
    private async ValueTask<bool> ReplaceCertificatesAsync(
        PlayerCertificates certificates, CancellationToken ct, ChatSigningSession? connectSession = null)
    {
        if (certificates.IsExpired(_time.GetUtcNow()))
        {
            // Every caller reaches this before any wire write or install. Warn once for the expiry episode and leave the current key unchanged.
            WarnExpiredKeyInUse(certificates.ExpiresAt);
            return false;
        }

        ChatSigningState? current = _current;
        if (!CanAnnounceRotation || _sink is not { } sink)
        {
            LogRotationRefusalOnce();
            return false;
        }

        ProfilePublicKeyData announcement = ProfileKeyMaterial.Build(certificates, useV2Signature: true);
        if (current is not null
            && ProfileKeyMaterial.AnnouncesSameKey(current.Certificates, announcement))
        {
            // An announcement the server would treat as a no-op is not a rotation, so neither the session id nor the message index moves.
            //
            // But the REPLACEMENT is still stored, against the SAME ChatSigningSession object, so the chain state and the session id are untouched while refreshedAfter advances. Returning without storing left the old instant in place, which made BeginRefreshIfDue's `RefreshedAfter < now` true forever - a provider call an hour for the life of the session - and left ChatState.ProfileKeyRefreshedAfter reporting an instant that had already passed. Safe by construction: AnnouncesSameKey has just established that the public key, the Mojang signature and expiresAt (at the wire's millisecond resolution) are identical, so nothing the server verifies against changes, and a re-exported private key imports to the same RSA key and produces the same signatures.
            Install(current.Session, certificates);
            _logger.LogDebug(
                "Refreshed the profile key material without a rotation (the server already holds this "
                + "key): session {SessionId} unchanged, now due for refresh after {RefreshedAfter:O} "
                + "(expires {ExpiresAt:O}).",
                current.Session.SessionId,
                certificates.RefreshedAfter,
                certificates.ExpiresAt);
            return true;
        }

        // The connect-time join keeps the session the client was CONSTRUCTED with; every other caller that reaches here (the tick's prefetch, the send path's own fetch, the episode bonus) is a genuine rotation and gets a freshly randomised one.
        ChatSigningSession target = connectSession ?? new ChatSigningSession(_sender, Guid.NewGuid());
        await sink.SendAsync(new ServerboundChatSessionUpdatePacket(target.SessionId, announcement), ct)
            .ConfigureAwait(false);
        Install(target, certificates);
        if (connectSession is null)
        {
            _chatState?.RecordProfileKeyRotation();
            _logger.LogInformation(
                "Rotated the profile key and announced chat session {SessionId}; the replacement expires "
                + "at {ExpiresAt:O} and is due for refresh after {RefreshedAfter:O}.",
                target.SessionId,
                certificates.ExpiresAt,
                certificates.RefreshedAfter);
        }
        else
        {
            // RefreshedAfter and ExpiresAt are logged HERE, at the only point they are known for the whole life of a session that never rotates, precisely so "no rotation happened" and "no rotation was due yet" can be told apart from the outside. Before this, the only place either timestamp reached a log at all was the "Rotated the profile key" message above, which by definition never fires on a session that holds one key end to end.
            _logger.LogDebug(
                "Sent chat_session_update for session {SessionId}; the installed key expires at "
                + "{ExpiresAt:O} and is due for refresh after {RefreshedAfter:O}.",
                target.SessionId,
                certificates.ExpiresAt,
                certificates.RefreshedAfter);
        }

        return true;
    }

    private void Install(ChatSigningSession session, PlayerCertificates certificates)
    {
        _session = session;
        var state = new ChatSigningState(session, certificates, _era)
        {
            Tracker = _tracker,
            LegacyCollector = _legacyCollector,
        };
        Volatile.Write(ref _current, state);

        // A new, genuinely USABLE key ends the previous expiry episode, so the next one gets its own warning and its own bonus attempt rather than inheriting a latch and a spent bonus from hours earlier. Guarded on the certificate NOT being expired: ResolveAsync's own checks mean this method should never be reached with a still-dead certificate on any live path, but Install itself should not have to trust every caller to have filtered that - an expired "refresh" must not look like a recovery.
        if (!certificates.IsExpired(_time.GetUtcNow()))
        {
            _loggedExpiredKeyInUse = false;
            _expiryBonusUsed = 0;
        }

        if (_chatState is not null)
            _chatState.ProfileKeyRefreshedAfter = certificates.RefreshedAfter;

    }

    /// <summary>The tick path's fetch. Goes through <see cref="AbandonAsync"/> - the SAME bounded, exception- contained fetch the send path uses - rather than awaiting the provider bare.</summary>
    /// <remarks>
    /// <para><see cref="AbandonAsync"/> is called with <c>fallbackToLastKnownGood: false</c>. This method treats any non-null answer as a fresh prefetch, so returning the installed certificate after a failed request would falsely block proactive refresh for up to an hour. A failed rotation fetch must therefore return null.</para>
    /// </remarks>
    private async Task FetchForRotationAsync(CancellationToken ct)
    {
        try
        {
            // AbandonAsync contains every provider exception except caller cancellation. The catches below preserve this method's broader contract that no refresh exception escapes the tick.
            PlayerCertificates? fresh =
                await AbandonAsync(ct, fallbackToLastKnownGood: false).ConfigureAwait(false);
            if (fresh is null)
            {
                // Either nothing arrived in time, or the provider FAILED - AbandonAsync does not tell the two apart here on purpose (see this method's remarks on fallbackToLastKnownGood), because both mean the same thing to a prefetch: there is nothing new to offer.
                _logger.LogDebug("Profile-key refresh returned no certificates; keeping the current key.");
                return;
            }

            Volatile.Write(ref _resolved, fresh);
            Volatile.Write(ref _prefetched, fresh);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Profile-key refresh was cancelled or timed out; keeping the current key.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Profile-key refresh failed; keeping the current key.");
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private void LogRotationRefusalOnce()
    {
        if (_loggedRotationRefusal)
            return;

        _loggedRotationRefusal = true;
        _logger.LogWarning(
            "Refusing to replace the profile key on signature era {Era}: this protocol has no way to "
            + "announce a new key mid-session, so the login-bound key is kept for the life of the "
            + "connection and chat stays verifiable for exactly as long as that key does.",
            _era);
    }
}
