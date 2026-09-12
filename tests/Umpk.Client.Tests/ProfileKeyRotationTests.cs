using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Commands;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Commands;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The profile-key refresh path: Mojang's <c>refreshedAfter</c> now drives a real rotation, and the rotation is refused outright on every era that cannot announce it.</summary>
/// <remarks>
/// <para>Key rotation checks <c>refreshedAfter</c> once per tick, throttles refresh attempts to once per hour, fetches off the session thread, and retains the previous key on failure. A successful change creates a fresh session id, re-roots the chain at index 0, and sends <c>ServerboundChatSessionUpdatePacket</c>.</para>
/// <para>The gate these tests exist for: <c>ServerboundChatSessionUpdatePacket</c> does not exist below 1.19.3. Protocol 760 has no such packet, and the only serverbound packet on 759/760 carrying a profile key is the login-phase <c>ServerboundHelloPacket</c>. A rotation there would leave the client signing with material the server has never seen, which is a signature failure and a disconnect, from a session that was healthy a moment earlier. Every pre-1.19.3 test below asserts that this cannot happen.</para>
/// </remarks>
public sealed class ProfileKeyRotationTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
    private static readonly Guid SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>On 1.19 and 1.19.1 the certificates are past <c>refreshedAfter</c> AND expired, and the provider is standing by with a fresh key. The coordinator must keep the login-bound key anyway, must not consult the provider again, and must not put anything on the wire.</summary>
    [Theory]
    [InlineData(ChatSignatureEra.V1_19)]
    [InlineData(ChatSignatureEra.V1_19_1)]
    public async Task PreV3WireLayouts_NeverReplaceTheLoginBoundKey(ChatSignatureEra era)
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates login = Certificates("login", clock.GetUtcNow().AddMinutes(30));
        PlayerCertificates fresh = Certificates("fresh", clock.GetUtcNow().AddDays(2));
        var sink = new RecordingSink();
        var provider = new CountingProvider(_ => fresh);
        var state = new ChatState();

        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, era, provider, clock, NullLogger.Instance, sink, state, login);

        Assert.False(coordinator.CanAnnounceRotation);
        Assert.False(state.ProfileKeyRotationSupported);

        // Past refreshedAfter but still valid: vanilla's dueRefresh() is true here.
        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Null(coordinator.BeginRefreshIfDue(default));
        Assert.Same(login, (await coordinator.EnsureAsync(default))!.Certificates);

        // And past expiry, which is where the old code refetched and swapped.
        clock.Advance(TimeSpan.FromHours(2));
        Assert.True(login.IsExpired(clock.GetUtcNow()));
        Assert.Null(coordinator.BeginRefreshIfDue(default));
        Assert.Same(login, (await coordinator.EnsureAsync(default))!.Certificates);

        Assert.Equal(0, provider.Calls);
        Assert.Empty(sink.Packets);
        Assert.Equal(0, state.ProfileKeyRotations);
    }

    /// <summary>With NO login-seeded key, a pre-1.19.3 session must not acquire one at all: it sends unsigned.</summary>
    /// <remarks>On 1.19 and 1.19.1 the profile key reaches the server in the login hello and nowhere else. A null seed therefore means the hello went out WITHOUT a key, so the server holds none for this player, and a key fetched afterwards is material the server was never given. Signing with it would produce an unverifiable session. The correct result is the unsigned path.</remarks>
    [Theory]
    [InlineData(ChatSignatureEra.V1_19)]
    [InlineData(ChatSignatureEra.V1_19_1)]
    public async Task PreV3WireLayouts_WithNoLoginKey_NeverAcquireOne(ChatSignatureEra era)
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates offered = Certificates("offered", clock.GetUtcNow().AddDays(2));
        var sink = new RecordingSink();
        var provider = new CountingProvider(_ => offered);

        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, era, provider, clock, NullLogger.Instance, sink);

        Assert.Null(await coordinator.EnsureAsync(default));
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await coordinator.EnsureAsync(default));

        Assert.Equal(0, provider.Calls);
        Assert.Empty(sink.Packets);
    }

    /// <summary>The 1.19.3+ counterpart, to show the refusal above is about the ERA and not about a missing seed: with the same empty start, a v3 session does acquire a key and announces it.</summary>
    [Fact]
    public async Task V3_WithNoLoginKey_AcquiresAndAnnouncesOne()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates offered = Certificates("offered", clock.GetUtcNow().AddDays(2));
        var sink = new RecordingSink();
        var provider = new CountingProvider(_ => offered);

        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        Assert.Same(offered, (await coordinator.EnsureAsync(default))!.Certificates);
        await coordinator.SendSessionUpdateAsync(default);

        Assert.Equal(1, provider.Calls);
        var announced = Assert.IsType<ServerboundChatSessionUpdatePacket>(Assert.Single(sink.Packets));
        Assert.Equal(SessionId, announced.SessionId);
    }

    /// <summary>The whole point of the item. Past <c>refreshedAfter</c> the tick starts a fetch; the send path then installs it, which means a NEW chat session id, a message index restarted at 0, and a <c>chat_session_update</c> carrying the NEW public key. The announcement is on the wire before the state that signs with the new key is ever handed out.</summary>
    [Fact]
    public async Task V3_DueRefresh_RotatesTheSessionAndAnnouncesTheNewKey()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates first = Certificates("first", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates second = Certificates("second", clock.GetUtcNow().AddDays(4), refreshIn: TimeSpan.FromDays(1));
        var sink = new RecordingSink();
        var provider = new CountingProvider(call => call == 0 ? first : second);
        var state = new ChatState();

        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink, state);

        Assert.True(coordinator.CanAnnounceRotation);
        Assert.True(state.ProfileKeyRotationSupported);

        ChatSigningState original = (await coordinator.EnsureAsync(default))!;
        await coordinator.SendSessionUpdateAsync(default);
        Assert.Equal(SessionId, original.Session.SessionId);
        Assert.Equal(first.RefreshedAfter, state.ProfileKeyRefreshedAfter);

        // Burn a message on the original chain so a reset index is distinguishable from a stalled one.
        original.Session.Sign(first, ChatSignatureEra.V1_19_3, "hello", clock.GetUtcNow(), 1, []);
        Assert.Equal(1, original.Session.MessageIndex);

        clock.Advance(TimeSpan.FromHours(7));
        Task? refresh = coordinator.BeginRefreshIfDue(default);
        Assert.NotNull(refresh);
        await refresh!;

        // The fetch alone puts NOTHING on the wire and swaps nothing. That is what keeps the announcement from being overtaken by a message signed with the outgoing key: only the send path, below, can commit the swap, and it writes the announcement first.
        Assert.Single(sink.Packets);

        ChatSigningState rotated = (await coordinator.EnsureAsync(default))!;
        Assert.Same(second, rotated.Certificates);
        Assert.NotEqual(original.Session.SessionId, rotated.Session.SessionId);
        Assert.Equal(0, rotated.Session.MessageIndex);
        Assert.Equal(Sender, rotated.Session.Sender);
        Assert.Equal(1, state.ProfileKeyRotations);
        Assert.Equal(second.RefreshedAfter, state.ProfileKeyRefreshedAfter);

        // Two frames, in order: the join announcement and the rotation announcement. The second carries the NEW session id and the NEW key, so the server can verify what is signed next.
        Assert.Equal(2, sink.Packets.Count);
        var join = Assert.IsType<ServerboundChatSessionUpdatePacket>(sink.Packets[0]);
        var announce = Assert.IsType<ServerboundChatSessionUpdatePacket>(sink.Packets[1]);
        Assert.Equal(SessionId, join.SessionId);
        Assert.Equal(rotated.Session.SessionId, announce.SessionId);
        Assert.Equal(second.ExpiresAt.ToUnixTimeMilliseconds(), announce.Key.ExpiresAtMillis);
        Assert.NotEqual(join.Key.KeyDer, announce.Key.KeyDer);
    }

    /// <summary>A rotation cannot overtake a send that already resolved its signing state; otherwise a message signed with the retired key can follow the new-key announcement and terminate the session.</summary>
    /// <remarks>
    /// <para>The interleaving this reproduces, which is available to any host that sends chat from two threads (<c>JavaConnection.SendAsync</c> documents itself as safe from any thread, so that is supported usage):</para>
    /// <list type="number">
    /// <item><description>A begins a chat send and parks between resolving and reaching the wire.</description></item>
    /// <item><description>The tick's fetch completes and offers a replacement key.</description></item>
    /// <item><description>B rotates, announces <c>chat_session_update</c>, signs at index 0 and sends.</description></item>
    /// <item><description>A resumes and puts a message signed with the RETIRED key on the wire.</description></item>
    /// </list>
    /// <para>Without serialization, wire order becomes <c>update, "b", "a"</c>, placing a retired-key message after the new-key announcement. That ordering is session-fatal rather than a dropped-message case.</para>
    /// <para>The assertion is the final frame order, which needs no timing to be meaningful: with the send serialised the order is <c>"a", update, "b"</c>, and without it the order is <c>update, "b", "a"</c>. The bounded wait below only decides how long to let B try; it can make this test miss a failure, but cannot make it fail spuriously, because no arrangement of correct code lets B reach the wire while A holds the scope.</para>
    /// </remarks>
    [Fact]
    public async Task V3_ARotationCannotOvertakeASendThatAlreadyResolved()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates first = Certificates("first", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates second = Certificates("second", clock.GetUtcNow().AddDays(4), refreshIn: TimeSpan.FromDays(1));
        var sink = new GatedSink();
        var provider = new CountingProvider(call => call == 0 ? first : second);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);
        sink.Reset();

        ChatActions chat = BuildChat(sink, coordinator);

        // A parks after resolving and signing, before its frame is recorded.
        Task a = chat.SendChatAsync("a");
        await sink.FirstChatEntered;

        // The tick's fetch lands while A is in flight. This alone must change nothing.
        clock.Advance(TimeSpan.FromHours(7));
        await coordinator.BeginRefreshIfDue(default)!;
        Assert.Empty(sink.Packets);

        // B now tries to send. If the scope did not span the send, B rotates, announces and sends here, ahead of A. Wait for evidence of that, bounded; correct code produces none.
        Task b = chat.SendChatAsync("b");
        await Task.WhenAny(sink.SawSessionUpdate, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.False(sink.SawSessionUpdate.IsCompleted, "B rotated while A was still in flight.");
        Assert.False(b.IsCompleted);

        sink.Release();
        await a;
        await b;

        // A's message reached the wire BEFORE the announcement of the key that replaced its own.
        Assert.Collection(
            sink.Packets,
            p => Assert.Equal("a", Assert.IsType<ServerboundSignedChatPacket>(p).Message),
            p => Assert.IsType<ServerboundChatSessionUpdatePacket>(p),
            p => Assert.Equal("b", Assert.IsType<ServerboundSignedChatPacket>(p).Message));

        // And each message verifies under the key that was current when it was sent: "a" under the retired key on the original session, "b" under the announced one at index 0.
        var sentA = (ServerboundSignedChatPacket)sink.Packets[0];
        var announced = (ServerboundChatSessionUpdatePacket)sink.Packets[1];
        var sentB = (ServerboundSignedChatPacket)sink.Packets[2];

        Assert.True(ChatSigningSession.Verify(
            first.PublicKeyPem, sentA.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 0, "a",
                DateTimeOffset.FromUnixTimeMilliseconds(sentA.TimestampMillis), sentA.Salt, [], null)));

        Assert.NotEqual(SessionId, announced.SessionId);
        Assert.True(ChatSigningSession.Verify(
            second.PublicKeyPem, sentB.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, announced.SessionId, MessageIndex: 0, "b",
                DateTimeOffset.FromUnixTimeMilliseconds(sentB.TimestampMillis), sentB.Salt, [], null)));
    }

    /// <summary>The rotation's key identity: after a rotation the next message verifies under the ANNOUNCED key and not under the retired one.</summary>
    /// <remarks>This drives the coordinator directly rather than the send path, so it says nothing about frame ordering under concurrency; <see cref="V3_ARotationCannotOvertakeASendThatAlreadyResolved"/> is what covers that. Its two <c>Verify</c> calls are the load-bearing assertions.</remarks>
    [Fact]
    public async Task V3_AfterARotation_TheNextMessageVerifiesUnderTheAnnouncedKey()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates first = Certificates("first", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates second = Certificates("second", clock.GetUtcNow().AddDays(4), refreshIn: TimeSpan.FromDays(1));
        var sink = new RecordingSink();
        var provider = new CountingProvider(call => call == 0 ? first : second);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await coordinator.EnsureAsync(default);
        sink.Packets.Clear();

        clock.Advance(TimeSpan.FromHours(7));
        await coordinator.BeginRefreshIfDue(default)!;

        // The send path: resolve, then sign, then send. Exactly what ChatActions.SendMessageAsync does.
        ChatSigningState signing = (await coordinator.EnsureAsync(default))!;
        byte[] signature = signing.Session.Sign(
            signing.Certificates, ChatSignatureEra.V1_19_3, "after", clock.GetUtcNow(), 7, []);
        await sink.SendAsync(
            new ServerboundSignedChatPacket("after", clock.GetUtcNow().ToUnixTimeMilliseconds(), 7, signature, EmptyAck()),
            default);

        Assert.Collection(
            sink.Packets,
            p => Assert.IsType<ServerboundChatSessionUpdatePacket>(p),
            p => Assert.IsType<ServerboundSignedChatPacket>(p));

        // And the message really is signed by the announced key, not the retired one.
        var announced = (ServerboundChatSessionUpdatePacket)sink.Packets[0];
        Assert.Equal(signing.Session.SessionId, announced.SessionId);
        Assert.True(ChatSigningSession.Verify(
            second.PublicKeyPem, signature, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, signing.Session.SessionId, MessageIndex: 0, "after", clock.GetUtcNow(), 7, [], null)));
        Assert.False(ChatSigningSession.Verify(
            first.PublicKeyPem, signature, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, signing.Session.SessionId, MessageIndex: 0, "after", clock.GetUtcNow(), 7, [], null)));
    }

    /// <summary>Expiry on 1.19.3+ rotates both the certificates and session identity, then announces the new session before signed chat resumes. Swapping certificates without an announcement would leave the server verifying against the prior key.</summary>
    [Fact]
    public async Task V3_ExpiredKey_RotatesAndAnnounces_RatherThanSwappingSilently()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates first = Certificates("first", clock.GetUtcNow().AddMinutes(30));
        PlayerCertificates second = Certificates("second", clock.GetUtcNow().AddDays(2));
        var sink = new RecordingSink();
        var provider = new CountingProvider(call => call == 0 ? first : second);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        ChatSigningState original = (await coordinator.EnsureAsync(default))!;
        clock.Advance(TimeSpan.FromHours(1));
        ChatSigningState refreshed = (await coordinator.EnsureAsync(default))!;

        Assert.Equal(2, provider.Calls);
        Assert.Same(second, refreshed.Certificates);
        Assert.NotSame(original.Session, refreshed.Session);
        Assert.NotEqual(original.Session.SessionId, refreshed.Session.SessionId);

        // Two announcements: the join's, then the rotation's. Both are preconditions of their install.
        Assert.Equal(2, sink.Packets.Count);
        Assert.Equal(SessionId, Assert.IsType<ServerboundChatSessionUpdatePacket>(sink.Packets[0]).SessionId);
        var announce = Assert.IsType<ServerboundChatSessionUpdatePacket>(sink.Packets[1]);
        Assert.Equal(refreshed.Session.SessionId, announce.SessionId);
    }

    /// <summary>A JOIN announcement that fails installs nothing, so no frame is ever signed with a key the server was not told about. The session recovers by itself once the announcement can go out.</summary>
    /// <remarks>
    /// <para>The rotation path already obeyed this precondition (see <see cref="V3_FailedAnnouncement_LeavesThePreviousKeyInstalled"/>); the connect path did not. It installed the key and only then announced it, from outside the lock, so a throwing announcement left a fully armed signing session behind while <c>UmpkClient</c> logged that chat would not be signed. Every later message then went out signed with material no server could verify, which on an enforce-secure-profile server is <c>multiplayer.disconnect.unsigned_chat</c>.</para>
    /// <para>The send FAILS rather than falling back to unsigned, and that is deliberate: the same enforce-secure-profile server that rejects an unverifiable signature also rejects unsigned chat, so emitting nothing is the only outcome that cannot make things worse. The assertion that matters is that the sink saw no signed frame at all while the announcement was failing.</para>
    /// </remarks>
    [Fact]
    public async Task V3_FailedJoinAnnouncement_InstallsNothingAndSignsNothing()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates certificates = Certificates("join", clock.GetUtcNow().AddDays(2));
        var sink = new ThrowingSink { Fail = true };
        var provider = new CountingProvider(_ => certificates);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await Assert.ThrowsAsync<IOException>(async () => await coordinator.EnsureAsync(default));

        // Nothing installed, so the send path cannot sign. It fails on the retried announcement rather than emitting anything, and the sink is still empty.
        ChatActions chat = BuildChat(sink, coordinator);
        await Assert.ThrowsAsync<IOException>(() => chat.SendChatAsync("while broken"));
        Assert.Empty(sink.Packets);

        // Once the announcement can go out, the same coordinator recovers: announce first, then install, then a signed frame.
        sink.Fail = false;
        await chat.SendChatAsync("after recovery");

        Assert.Collection(
            sink.Packets,
            p => Assert.Equal(SessionId, Assert.IsType<ServerboundChatSessionUpdatePacket>(p).SessionId),
            p =>
            {
                var sent = Assert.IsType<ServerboundSignedChatPacket>(p);
                Assert.Equal("after recovery", sent.Message);
                Assert.NotNull(sent.Signature);
            });
    }

    /// <summary>Signed chat can never precede the join announcement, even when the send races the connect path.</summary>
    /// <remarks>
    /// <para>A 1.19.3+ server with <c>enforce-secure-profile</c> installs a reject-everything chat decoder until <c>chat_session_update</c> arrives, so a signed message that gets there first causes <c>multiplayer.disconnect.unsigned_chat</c>. The announcement and key installation must therefore share one serialized operation.</para>
    /// <para>This drives the extreme case, a send that arrives before the connect sequence has run at all, and asserts the frame order. The announcement is emitted while the key-install lock is held, so it cannot be outrun.</para>
    /// </remarks>
    [Fact]
    public async Task V3_SignedChatCannotPrecedeTheJoinAnnouncement()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates certificates = Certificates("join", clock.GetUtcNow().AddDays(2));
        var sink = new RecordingSink();
        var provider = new CountingProvider(_ => certificates);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        // No EnsureAsync, no SendSessionUpdateAsync: the host got in first.
        ChatActions chat = BuildChat(sink, coordinator);
        await chat.SendChatAsync("early");

        Assert.Collection(
            sink.Packets,
            p => Assert.Equal(SessionId, Assert.IsType<ServerboundChatSessionUpdatePacket>(p).SessionId),
            p =>
            {
                var sent = Assert.IsType<ServerboundSignedChatPacket>(p);
                Assert.Equal("early", sent.Message);
                Assert.NotNull(sent.Signature);
            });

        // And the connect sequence that follows does not announce a second time.
        await coordinator.SendSessionUpdateAsync(default);
        Assert.Equal(2, sink.Packets.Count);
    }

    /// <summary>An announcement that fails leaves the previous key installed, so the client keeps signing with the material the server still holds instead of switching to one it was never told about.</summary>
    [Fact]
    public async Task V3_FailedAnnouncement_LeavesThePreviousKeyInstalled()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates first = Certificates("first", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates second = Certificates("second", clock.GetUtcNow().AddDays(4));
        var sink = new ThrowingSink();
        var provider = new CountingProvider(call => call == 0 ? first : second);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        ChatSigningState original = (await coordinator.EnsureAsync(default))!;
        clock.Advance(TimeSpan.FromHours(7));
        await coordinator.BeginRefreshIfDue(default)!;

        sink.Fail = true;
        await Assert.ThrowsAsync<IOException>(async () => await coordinator.EnsureAsync(default));

        sink.Fail = false;
        ChatSigningState kept = (await coordinator.EnsureAsync(default))!;
        Assert.Same(original, kept);
        Assert.Same(first, kept.Certificates);
    }

    /// <summary>Unchanged certificates are not a rotation, mirroring vanilla's key-pair equality guard: the session id and the message index have to survive a provider that hands back the same key.</summary>
    /// <remarks>
    /// <para>The provider deliberately returns a DIFFERENT INSTANCE carrying the same values on the second call. Returning the same reference would pass under reference equality and would not exercise the announcement comparison the guard actually relies on, which is the realistic case: a provider that re-reads its cache file, or re-exports the PEM, hands back an equal-but-distinct object.</para>
    /// <para>"Does not rotate" applies to the session id, public key, and message index held by <see cref="ChatSigningSession"/>, which is asserted here by reference. It does not require the <see cref="ChatSigningState"/> wrapper to retain identity: a same-key answer must still be stored so its later <c>refreshedAfter</c> replaces the old one. Without that the refresh stays permanently due and the tick asks the provider once an hour for the life of the session. The wrapper is a local record over (session, certificates, era) and nothing the server verifies depends on its identity.</para>
    /// </remarks>
    [Fact]
    public async Task V3_UnchangedCertificates_DoNotRotate()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates same = Certificates("same", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates copy = same with { RefreshedAfter = same.RefreshedAfter.AddHours(12) };
        Assert.NotSame(same, copy);
        var sink = new RecordingSink();
        var provider = new CountingProvider(call => call == 0 ? same : copy);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        ChatSigningState original = (await coordinator.EnsureAsync(default))!;
        original.Session.Sign(same, ChatSignatureEra.V1_19_3, "hello", clock.GetUtcNow(), 1, []);

        clock.Advance(TimeSpan.FromHours(7));
        await coordinator.BeginRefreshIfDue(default)!;
        ChatSigningState after = (await coordinator.EnsureAsync(default))!;

        // The chain is the same object, so the session id and the running index are the same values the server is still expecting.
        Assert.Same(original.Session, after.Session);
        Assert.Equal(SessionId, after.Session.SessionId);
        Assert.Equal(1, after.Session.MessageIndex);

        // And the replacement was taken up, so the refresh window moves forward.
        Assert.Same(copy, after.Certificates);

        // Exactly one frame, the join announcement. A rotation would have added a second.
        Assert.Equal(SessionId, Assert.IsType<ServerboundChatSessionUpdatePacket>(Assert.Single(sink.Packets)).SessionId);
    }

    /// <summary>A due or missing key must query the provider at most once per hour instead of once per tick. Across six simulated hours at 20 TPS, 432,000 tick calls must produce six provider calls.</summary>
    /// <remarks>
    /// <para><b>The loop AWAITS each started fetch before advancing the clock again, and that is a correctness requirement of the driver, not a convenience.</b> <c>BeginRefreshIfDue</c> will not start a second fetch until <c>_refreshing</c> is released in <c>FetchForRotationAsync</c>'s <c>finally</c>, which runs on a thread-pool thread reached through <c>Task.Run</c>. The old driver burned six virtual hours in 432,000 tight-loop iterations in tens of milliseconds of REAL time, so under pool pressure whole virtual hours elapsed with <c>_refreshing</c> still 1 and the fetches for them were simply lost. It failed 6 of 6 concurrent copies, always undercounting (5, 4 or 3, never over) - a test that could report a false throttle failure or mask a real one. Awaiting makes the driver measure the coordinator instead of the scheduler.</para>
    /// <para>The clock does NOT advance while awaiting, so no virtual time passes inside the wait and the count still means "one fetch per virtual hour". What awaiting gives up is discrimination against a throttle armed on COMPLETION rather than on start, because by the time the loop resumes a completion-armed throttle would look the same. <see cref="V3_Throttle_IsArmedBeforeTheFetch_NotAfterIt"/> is the test that pins the arming point, and it does so directly rather than by inference from a count.</para>
    /// </remarks>
    [Fact]
    public async Task V3_Throttle_AllowsOneFetchPerHour_EvenWhenTheProviderKeepsFailing()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var provider = new CountingProvider(_ => null);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        int started = 0;
        for (int tick = 0; tick < 20 * 60 * 60 * 6; tick++)
        {
            if (coordinator.BeginRefreshIfDue(default) is { } fetch)
            {
                started++;

                // Wait for the fetch to be OBSERVABLE before letting virtual time move on. Without this the next iteration races the thread pool for the single-flight release.
                await fetch;
            }

            clock.Advance(TimeSpan.FromMilliseconds(50));
        }

        Assert.Equal(6, started);
        Assert.Equal(6, provider.Calls);
    }

    /// <summary>The throttle is armed when the fetch STARTS, not when it finishes, which is what makes a failing endpoint cost one call an hour rather than one per fetch duration. The refresh floor is armed before the composed fetch is scheduled.</summary>
    /// <remarks>
    /// <para>THE FETCH HAS TO TAKE TIME ON THE SESSION CLOCK for this to be checkable at all. An earlier version of this test held the clock frozen across the fetch, which makes arm-at-start and arm-at-completion compute the identical instant: it named the property but could not fail for it, and a mutant that re-armed in <c>FetchForRotationAsync</c>'s finally passed it.</para>
    /// <para>So the provider advances the clock by ten minutes while it runs. Arming at the start puts the next opening at T+60m; arming at completion would put it at T+70m. The probe below at T+61m separates them, and it is the assertion the mutant fails.</para>
    /// </remarks>
    [Fact]
    public async Task V3_Throttle_IsArmedBeforeTheFetch_NotAfterIt()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var provider = new ClockAdvancingProvider(clock, TimeSpan.FromMinutes(10));
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        await coordinator.BeginRefreshIfDue(default)!;      // starts at T+0, completes at T+10m, failed
        Assert.Equal(1, provider.Calls);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(10), clock.GetUtcNow());

        // The very next tick, and every tick up to the hour FROM THE START, must start nothing.
        Assert.Null(coordinator.BeginRefreshIfDue(default));
        clock.Advance(TimeSpan.FromMinutes(49));            // T+59m
        Assert.Null(coordinator.BeginRefreshIfDue(default));
        Assert.Equal(1, provider.Calls);

        // T+61m. Armed at the start, the window is open. Armed at completion it would still be shut until T+70m, and this is the line that fails on that mutant.
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(61), clock.GetUtcNow());
        await coordinator.BeginRefreshIfDue(default)!;
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>The observable signing trio does not survive a session. Rotate on 1.19.3, end the session, reconnect on 1.19.2, and the reported state must describe the NEW connection.</summary>
    /// <remarks><c>ClientState</c> outlives a connection, so without an explicit reset a session that rotated once on 761 and then reconnected to 760 reported <c>ProfileKeyRotationSupported == false</c> beside <c>ProfileKeyRotations == 1</c>, plus a refresh instant belonging to a key the new session never held. <c>ResetForSessionEnd</c> is reached from <c>TeardownSessionAsync</c>, which is the single exit for a normal run, an explicit disconnect and disposal alike.</remarks>
    [Fact]
    public async Task ProfileKeyState_DoesNotSurviveTheSession()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates first = Certificates("first", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates second = Certificates("second", clock.GetUtcNow().AddDays(4), refreshIn: TimeSpan.FromDays(1));
        var state = new ClientState(new ClientFeatures().Normalized());
        var sink = new RecordingSink();
        var provider = new CountingProvider(call => call == 0 ? first : second);

        var v3 = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink, state.Chat);
        await v3.EnsureAsync(default);
        await v3.SendSessionUpdateAsync(default);
        clock.Advance(TimeSpan.FromHours(7));
        await v3.BeginRefreshIfDue(default)!;
        await v3.EnsureAsync(default);

        Assert.True(state.Chat.ProfileKeyRotationSupported);
        Assert.Equal(1, state.Chat.ProfileKeyRotations);
        Assert.Equal(second.RefreshedAfter, state.Chat.ProfileKeyRefreshedAfter);

        state.ResetForSessionEnd();

        Assert.False(state.Chat.ProfileKeyRotationSupported);
        Assert.Equal(0, state.Chat.ProfileKeyRotations);
        Assert.Null(state.Chat.ProfileKeyRefreshedAfter);

        // Reconnecting on an era that cannot rotate reports exactly that, with no residue.
        PlayerCertificates legacy = Certificates("legacy", clock.GetUtcNow().AddDays(2));
        _ = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_1, provider, clock, NullLogger.Instance,
            sink, state.Chat, legacy);

        Assert.False(state.Chat.ProfileKeyRotationSupported);
        Assert.Equal(0, state.Chat.ProfileKeyRotations);
        Assert.Equal(legacy.RefreshedAfter, state.Chat.ProfileKeyRefreshedAfter);
    }

    /// <summary>A provider that never answers must not stall the session. The send-path fetch runs while the coordinator's ordering lock is held, so before the deadline a hung provider queued every later chat and command behind it with no bound at all.</summary>
    /// <remarks>
    /// <para>The assertion is the EFFECT, not the field: after the deadline fires, the message that goes out carries a signature that verifies against the public key of the <c>chat_session_update</c> the server was actually given. So the timeout provably cannot leave a key installed that the server was never told about, which is the one outcome that matters here (the <c>decoder</c> lambda: a failed <c>verify</c> throws <c>DecodeException(multiplayer.disconnect.unsigned_chat, shouldDisconnect: true)</c>).</para>
    /// <para>The clock is virtual, including its timers, so the 30-second deadline costs no real time.</para>
    /// </remarks>
    [Fact]
    public async Task V3_SendFetchThatNeverAnswers_IsAbandonedOnTheDeadline_AndSignsWithTheAnnouncedKey()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates announced = Certificates("announced", clock.GetUtcNow().AddDays(2));
        var entered = new TaskCompletionSource();
        var never = new TaskCompletionSource();
        var provider = new BlockingProvider(announced, never.Task, entered);
        var sink = new RecordingSink();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        // Join: the first provider call answers, so the key is announced and installed.
        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);
        var announcement = Assert.IsType<ServerboundChatSessionUpdatePacket>(Assert.Single(sink.Packets));

        // Push past expiry AND past the throttle, so the next send is forced to consult the provider, which from here on never answers.
        clock.Advance(TimeSpan.FromDays(3));

        ChatActions chat = BuildChat(sink, coordinator);
        Task send = chat.SendChatAsync("after the provider hung");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Nothing has been abandoned yet, so nothing has been sent yet either.
        Assert.False(send.IsCompleted);
        Assert.Single(sink.Packets);

        // Fire the deadline on the session clock.
        clock.Advance(TimeSpan.FromSeconds(31));
        await send.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(provider.Cancelled);

        // No second announcement, and no rotation: the hung answer reached no Install.
        Assert.Equal(2, sink.Packets.Count);
        var sent = Assert.IsType<ServerboundSignedChatPacket>(sink.Packets[1]);
        Assert.NotNull(sent.Signature);

        // THE EFFECT: the signature on the wire verifies against the key on the wire.
        Assert.Equal(
            ProfileKeyMaterial.Build(announced, useV2Signature: true).KeyDer,
            announcement.Key.KeyDer);
        Assert.True(ChatSigningSession.Verify(
            announced.PublicKeyPem,
            sent.Signature!,
            ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender,
                announcement.SessionId,
                MessageIndex: 0,
                sent.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis),
                sent.Salt,
                [])));
    }

    /// <summary>A caller's own cancellation is NOT the deadline and must not be swallowed into "keep the current key". Only the deadline arm is absorbed.</summary>
    [Fact]
    public async Task V3_SendFetch_PropagatesTheCallersOwnCancellation()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var entered = new TaskCompletionSource();
        var never = new TaskCompletionSource();
        var provider = new BlockingProvider(Certificates("unused", clock.GetUtcNow().AddDays(2)), never.Task, entered);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        // Burn the first (answering) provider call so the next one blocks.
        await coordinator.EnsureAsync(default);
        clock.Advance(TimeSpan.FromDays(3));

        using var cts = new CancellationTokenSource();
        Task resolve = coordinator.EnsureAsync(cts.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolve);
    }

    /// <summary>The send-path fetch had no throttle, so every send made while no certificates were held re-entered the provider under the ordering lock. The one-hour throttle is armed before the fetch and covers the no-key case, so a provider that keeps answering "nothing" costs one call an hour whatever the answer was.</summary>
    [Fact]
    public async Task V3_SendFetch_AsksTheProviderOncePerHour_NotOncePerSend()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var provider = new CountingProvider(_ => null);
        var sink = new RecordingSink();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);
        ChatActions chat = BuildChat(sink, coordinator);

        for (int i = 0; i < 20; i++)
        {
            await chat.SendChatAsync($"unsigned {i}");
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(1, provider.Calls);

        // Past the hour, exactly one more call.
        clock.Advance(TimeSpan.FromHours(1));
        await chat.SendChatAsync("after the hour");
        await chat.SendChatAsync("and again");
        Assert.Equal(2, provider.Calls);

        // Every one of those 22 messages still went out, unsigned, which is the documented fallback.
        Assert.Equal(22, sink.Packets.Count);
        Assert.All(sink.Packets, p => Assert.Null(Assert.IsType<ServerboundSignedChatPacket>(p).Signature));
    }

    /// <summary>The throttle must not cost the session its recovery path. A join whose <c>chat_session_update</c> throws installs nothing and leaves the connect window open, and every later send retries the announcement; that retry needs the CERTIFICATES, not a second provider call, because a provider asked again a moment later returns the same key.</summary>
    /// <remarks>A transient announcement failure must retain the fetched certificates for retry without waiting for the provider throttle.</remarks>
    [Fact]
    public async Task V3_FailedJoinAnnouncement_RetriesWithoutAskingTheProviderAgain()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates certificates = Certificates("join", clock.GetUtcNow().AddDays(2));
        var sink = new ThrowingSink { Fail = true };
        var provider = new CountingProvider(_ => certificates);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await Assert.ThrowsAsync<IOException>(async () => await coordinator.EnsureAsync(default));
        Assert.Equal(1, provider.Calls);

        ChatActions chat = BuildChat(sink, coordinator);
        await Assert.ThrowsAsync<IOException>(() => chat.SendChatAsync("while broken"));
        Assert.Empty(sink.Packets);

        sink.Fail = false;
        await chat.SendChatAsync("after recovery");

        // One provider call for the whole sequence, and the recovery still announced before it signed.
        Assert.Equal(1, provider.Calls);
        Assert.Collection(
            sink.Packets,
            p => Assert.Equal(SessionId, Assert.IsType<ServerboundChatSessionUpdatePacket>(p).SessionId),
            p => Assert.NotNull(Assert.IsType<ServerboundSignedChatPacket>(p).Signature));
    }

    /// <summary>An expired key bypasses the refresh throttle.</summary>
    /// <remarks>
    /// <para>With the throttle applied to every path, a session whose key expired inside the refresh window asked the provider nothing, kept the expired key, and signed with it. The server rejects that message without disconnecting the session. The client must refresh before reporting a successful signed send.</para>
    /// <para>The assertion is the EFFECT. The clock is moved one minute past expiry, which is fifty-nine minutes INSIDE the refresh window the join armed, and the very next send must rotate: a second <c>chat_session_update</c> on the wire and a signature that verifies under the NEW key and the new session id at index 0.</para>
    /// </remarks>
    [Fact]
    public async Task V3_AnExpiredKeyInsideTheThrottleWindow_IsReplacedOnTheVeryNextSend()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates shortLived = Certificates("short", clock.GetUtcNow().AddMinutes(10));
        PlayerCertificates replacement = Certificates("fresh", clock.GetUtcNow().AddDays(2));
        var sink = new RecordingSink();
        var state = new ChatState();
        var provider = new CountingProvider(call => call == 0 ? shortLived : replacement);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink, state);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);
        Assert.Single(sink.Packets);

        // One minute past expiry, fifty-nine minutes inside the hourly refresh window.
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.True(shortLived.IsExpired(clock.GetUtcNow()));
        Assert.Null(coordinator.BeginRefreshIfDue(default));

        ChatActions chat = BuildChat(sink, coordinator);
        await chat.SendChatAsync("after expiry");

        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, state.ProfileKeyRotations);

        List<ServerboundChatSessionUpdatePacket> announcements =
            [.. sink.Packets.OfType<ServerboundChatSessionUpdatePacket>()];
        Assert.Equal(2, announcements.Count);

        var sent = Assert.IsType<ServerboundSignedChatPacket>(sink.Packets[^1]);
        Assert.True(ChatSigningSession.Verify(
            replacement.PublicKeyPem, sent.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, announcements[1].SessionId, MessageIndex: 0, sent.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt, [])));

        // And the dead key is gone: the same payload does not verify under it.
        Assert.False(ChatSigningSession.Verify(
            shortLived.PublicKeyPem, sent.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, announcements[1].SessionId, MessageIndex: 0, sent.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt, [])));
    }

    /// <summary>An expired certificate must never be handed back as a resolution by the THROTTLED arm either. The no-certificates case is still throttled, so a session whose join announcement failed and whose cached certificate then expired must fall to the unsigned path rather than sign with the dead one.</summary>
    [Fact]
    public async Task V3_ThrottledResolve_NeverHandsBackAnExpiredCertificate()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates shortLived = Certificates("short", clock.GetUtcNow().AddMinutes(10));
        var sink = new ThrowingSink { Fail = true };
        var provider = new CountingProvider(_ => shortLived);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        // The join announcement throws, so nothing is installed and the certificates are only cached.
        await Assert.ThrowsAsync<IOException>(async () => await coordinator.EnsureAsync(default));
        Assert.Equal(1, provider.Calls);

        // Past expiry but inside the refresh window: the cached answer is now worthless.
        clock.Advance(TimeSpan.FromMinutes(11));
        sink.Fail = false;

        ChatActions chat = BuildChat(sink, coordinator);
        await chat.SendChatAsync("no usable key");

        // No announcement, because nothing was installed, and the message went out UNSIGNED rather than signed with a certificate the server would refuse.
        Assert.Empty(sink.Packets.OfType<ServerboundChatSessionUpdatePacket>());
        var sent = Assert.IsType<ServerboundSignedChatPacket>(Assert.Single(sink.Packets));
        Assert.Null(sent.Signature);
    }

    /// <summary>The deadline must bound a provider that does NOT observe its token, because <see cref="IChatSigningProvider"/> does not require one to.</summary>
    /// <remarks>The provider deliberately ignores cancellation, which is allowed by the interface. The deadline must still abandon an HTTP-style call that never returns and release the pending send.</remarks>
    [Fact]
    public async Task V3_AProviderThatIgnoresItsToken_IsAbandonedAtTheDeadline()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates announced = Certificates("announced", clock.GetUtcNow().AddDays(2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new UncooperativeProvider(announced, never.Task, entered);
        var sink = new RecordingSink();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);
        Assert.Single(sink.Packets);

        // Expire the installed key so the next send is forced to consult the provider, which from here on neither answers nor observes its token.
        clock.Advance(TimeSpan.FromDays(3));

        ChatActions chat = BuildChat(sink, coordinator);
        Task send = chat.SendChatAsync("after the provider hung");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(send.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(31));
        await send.WaitAsync(TimeSpan.FromSeconds(30));

        // The provider is still stuck, and the send finished anyway.
        Assert.False(never.Task.IsCompleted);
        Assert.Equal(2, sink.Packets.Count);
        var sent = Assert.IsType<ServerboundSignedChatPacket>(sink.Packets[1]);

        // Signed with the announced key, at the announced session id: nothing the abandoned call could later produce was installed.
        var announcement = Assert.IsType<ServerboundChatSessionUpdatePacket>(sink.Packets[0]);
        Assert.True(ChatSigningSession.Verify(
            announced.PublicKeyPem, sent.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, announcement.SessionId, MessageIndex: 0, sent.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt, [])));

        never.TrySetResult();
    }

    /// <summary>An expired-key episode allows one immediate provider retry. Subsequent sends remain throttled so they cannot trigger an authenticated Mojang request per message.</summary>
    [Fact]
    public async Task V3_ExpiredKey_FetchesAreBoundedToOnePerEpisode_NotOnePerSend()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates dead = Certificates("dead", clock.GetUtcNow().AddMinutes(10));
        var provider = new CountingProvider(_ => dead);
        var sink = new RecordingSink();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);

        clock.Advance(TimeSpan.FromMinutes(11));
        ChatActions chat = BuildChat(sink, coordinator);
        for (int i = 0; i < 5; i++)
            await chat.SendChatAsync($"after expiry {i}");

        // The join (call 0) plus exactly ONE bonus attempt for the WHOLE episode (call 1), never one per send: a provider that keeps failing costs two calls across five sends, not six.
        Assert.Equal(2, provider.Calls);

        // And every send still reached the wire (signed with whatever is installed, per the documented degrade choice), rather than being throttled into silence.
        Assert.Equal(5, sink.Packets.OfType<ServerboundSignedChatPacket>().Count());
    }

    /// <summary>A provider that throws must never fault the send. Once the key expires, provider exceptions must not propagate out of <c>SendChatAsync</c> on every message. The "more dangerous" state (an expired key actively being signed with) was made permanently fatal by a network blip, while the no-key state degraded gracefully. That asymmetry is backwards.</summary>
    [Fact]
    public async Task V3_ExpiredKey_AThrowingProvider_NeverFaultsTheSend()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates initial = Certificates("initial", clock.GetUtcNow().AddMinutes(10));
        var provider = new ThrowingAfterFirstProvider(initial, () => new HttpRequestException("simulated"));
        var sink = new RecordingSink();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);

        clock.Advance(TimeSpan.FromMinutes(11));
        ChatActions chat = BuildChat(sink, coordinator);

        // Must not throw: every send still reaches the wire.
        await chat.SendChatAsync("one");
        await chat.SendChatAsync("two");
        await chat.SendChatAsync("three");

        Assert.Equal(2, provider.Calls);
        Assert.Equal(3, sink.Packets.OfType<ServerboundSignedChatPacket>().Count());
    }

    /// <summary>A provider that re-hands the same (or another) STILL-EXPIRED certificate must warn, not silently install it and claim a refresh. A non-null-but-expired answer must not reach <c>ReplaceCertificatesAsync</c> or log "refreshed... without a rotation" once per send: every local signal claimed success while the session kept signing with a dead key.</summary>
    [Fact]
    public async Task V3_ProviderRehandsTheSameExpiredCertificate_WarnsOncePerEpisode_NotSilently()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates dead = Certificates("dead", clock.GetUtcNow().AddMinutes(10));
        var provider = new CountingProvider(_ => dead);
        var sink = new RecordingSink();
        var logger = new RecordingLogger();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, logger, sink);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);

        clock.Advance(TimeSpan.FromMinutes(11));
        ChatActions chat = BuildChat(sink, coordinator);
        for (int i = 0; i < 6; i++)
            await chat.SendChatAsync($"dead key send {i}");

        int warnings = logger.Entries.Count(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("expired", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, warnings);

        // The "refreshed... without a rotation" line is the SILENT-SUCCESS claim this guards against; it must never fire for a certificate that never stopped being dead.
        Assert.DoesNotContain(
            logger.Entries, e => e.Message.Contains("without a rotation", StringComparison.Ordinal));
    }

    /// <summary>The SEND path's fetch was bounded with a real deadline first; the TICK path (<see cref="ChatSigningCoordinator.FetchForRotationAsync"/>) still awaited the provider bare. A provider that ignores its token wedges <c>_refreshing</c> permanently, so <see cref="ChatSigningCoordinator.BeginRefreshIfDue"/> silently returns null every tick forever afterward.</summary>
    [Fact]
    public async Task V3_TickFetch_IgnoringItsToken_IsAbandonedAtTheDeadline_AndDoesNotWedgeTheRefreshLatch()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlayerCertificates initial = Certificates(
            "initial", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromMinutes(30));
        var provider = new UncooperativeProvider(initial, never.Task, entered);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        await coordinator.EnsureAsync(default);

        // The JOIN fetch itself arms the shared hourly floor to T+60m (it goes through the ordinary no-key throttle branch, which - like vanilla's - is unconditional), so the tick's OWN due instant (RefreshedAfter = T+30m) is not what gates it here: the shared floor is later and wins.
        clock.Advance(TimeSpan.FromMinutes(61));
        Task? tickFetch = coordinator.BeginRefreshIfDue(default);
        Assert.NotNull(tickFetch);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(31));
        await tickFetch!.WaitAsync(TimeSpan.FromSeconds(30));

        // The latch must be released so a fresh fetch can start one hour after this fetch began.
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.NotNull(coordinator.BeginRefreshIfDue(default));

        never.TrySetResult();
    }

    /// <summary>Nothing serialises the tick fetch against the send fetch, and an HTTP source with retries or a cache can legitimately return an OLDER certificate from a call that simply took longer. A tick fetch that STARTED before a send-path recovery but COMPLETES after it must not clobber the already-installed, fresher certificate with its stale answer.</summary>
    [Fact]
    public async Task V3_ALateTickAnswer_DoesNotClobberAFresherSendPathInstall()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates stale = Certificates(
            "stale", clock.GetUtcNow().AddMinutes(80), refreshIn: TimeSpan.FromMinutes(65));
        PlayerCertificates fresh = Certificates("fresh", clock.GetUtcNow().AddDays(2));
        var slowSecondAnswer = new TaskCompletionSource<PlayerCertificates?>();
        var provider = new OrderedProvider(stale, slowSecondAnswer.Task, fresh);
        var sink = new RecordingSink();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink);

        await coordinator.EnsureAsync(default);          // call 0: installs `stale`
        await coordinator.SendSessionUpdateAsync(default);

        clock.Advance(TimeSpan.FromMinutes(66));          // past refreshedAfter, before expiry
        Task? tickFetch = coordinator.BeginRefreshIfDue(default);   // call 1: starts, blocks on the gate
        Assert.NotNull(tickFetch);

        // Wait for the tick's call to genuinely claim slot 1 before triggering what must be slot 2: the tick's fetch is dispatched via a real Task.Run with no ordering guarantee against a directly awaited call below it, so without this, the send's own fetch can occasionally race into slot 1 instead and deadlock on `slowSecondAnswer` (which only this test resolves, later, after a send that would then never return).
        await provider.SecondCallEntered.WaitAsync(TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromMinutes(15));          // T+81m: `stale` has expired
        ChatActions chat = BuildChat(sink, coordinator);
        await chat.SendChatAsync("recovers onto fresh");  // call 2 (the episode's bonus): installs `fresh`

        // Now let the SLOW tick answer, started long before the recovery, finally land. Bounded, like every other real-Task.Run wait in this file: BeginRefreshIfDue dispatches via a genuine Task.Run, so under real thread-pool contention this could otherwise wait a long time instead of failing fast and clearly.
        slowSecondAnswer.SetResult(stale);
        await tickFetch!.WaitAsync(TimeSpan.FromSeconds(30));

        await chat.SendChatAsync("after the late tick answer");
        var last = Assert.IsType<ServerboundSignedChatPacket>(sink.Packets[^1]);
        var freshAnnouncement = Assert.IsType<ServerboundChatSessionUpdatePacket>(
            sink.Packets.OfType<ServerboundChatSessionUpdatePacket>().Last());

        // THE EFFECT: still signed under `fresh`, at index 1 under the SAME session the recovery announced. A clobber would have rotated backwards onto `stale` (already expired) and forward again, producing extra frames and, in the window between them, a message signed with a dead key.
        Assert.True(ChatSigningSession.Verify(
            fresh.PublicKeyPem, last.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, freshAnnouncement.SessionId, MessageIndex: 1, last.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(last.TimestampMillis), last.Salt, [])));
        Assert.False(ChatSigningSession.Verify(
            stale.PublicKeyPem, last.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, freshAnnouncement.SessionId, MessageIndex: 1, last.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(last.TimestampMillis), last.Salt, [])));

        // And the late answer produced no extra chat_session_update: exactly two total (the join, and the one recovery rotation), not a clobber-then-recover pair.
        Assert.Equal(2, sink.Packets.OfType<ServerboundChatSessionUpdatePacket>().Count());
    }

    /// <summary><c>AbandonAsync</c> must attach fault observation before propagating caller cancellation. An uncooperative provider may fault later, and that exception must still be observed.</summary>
    [Fact]
    public async Task V3_SendFetch_ObservesTheProvidersFaultEvenOnCallerCancellation()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlayerCertificates initial = Certificates("initial", clock.GetUtcNow().AddDays(2));
        var provider = new UncooperativeProvider(initial, gate.Task, entered);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        await coordinator.EnsureAsync(default);
        clock.Advance(TimeSpan.FromDays(3));

        var unobserved = new TaskCompletionSource<Exception>();
        void Handler(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            unobserved.TrySetResult(e.Exception);
            e.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            using var cts = new CancellationTokenSource();
            Task resolve = coordinator.EnsureAsync(cts.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolve);

            // The provider's gate faults NOW, well after the caller cancelled. Its exception must already be observed by the continuation AbandonAsync attaches before testing the caller's own cancellation; if that ordering regresses, this surfaces as an unobserved task exception once the runtime finalizes it.
            gate.SetException(new InvalidOperationException("late provider failure"));

            for (int i = 0; i < 5 && !unobserved.Task.IsCompleted; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(50);
            }

            Assert.False(
                unobserved.Task.IsCompleted,
                "The provider's fault leaked as an unobserved task exception after caller cancellation.");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }

    /// <summary>The choke point (<c>ChatSigningCoordinator.ReplaceCertificatesAsync</c>) must refuse an already-expired certificate regardless of which path produced it - including the TICK's prefetch, which the earlier fix never touched because it only guarded the send path's own fetch RETURN VALUE. An idle-but-connected session lets a certificate the tick already fetched expire in the field; the next send must not announce or install it. It must fall back to the documented degrade (keep signing with whatever is already installed), the same as any other "nothing usable arrived" outcome.</summary>
    [Fact]
    public async Task V3_APrefetchedCertificateThatExpiresWhileIdle_IsNeverInstalledOrAnnounced()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates a = Certificates(
            "a", clock.GetUtcNow().AddMinutes(80), refreshIn: TimeSpan.FromMinutes(65));
        PlayerCertificates b = Certificates("b", clock.GetUtcNow().AddMinutes(90));
        var sink = new RecordingSink();
        var state = new ChatState();
        // Every call after the join answers `b`, so this exercises BOTH offers of the dead certificate in one send: the tick's prefetch, then (once that is refused) the send's own bonus fetch.
        var provider = new CountingProvider(call => call == 0 ? a : b);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink, state);

        await coordinator.EnsureAsync(default);          // call 0: installs `a`
        await coordinator.SendSessionUpdateAsync(default);
        Assert.Single(sink.Packets);

        clock.Advance(TimeSpan.FromMinutes(66));          // past `a`'s refreshedAfter, before its expiry
        Task? tickFetch = coordinator.BeginRefreshIfDue(default);   // call 1: fetches `b`
        Assert.NotNull(tickFetch);
        await tickFetch!.WaitAsync(TimeSpan.FromSeconds(30));

        // The session is idle for a long time: BOTH `a` (80m) and `b` (90m) die in the field.
        clock.Advance(TimeSpan.FromMinutes(29));          // T+95m
        Assert.True(a.IsExpired(clock.GetUtcNow()));
        Assert.True(b.IsExpired(clock.GetUtcNow()));

        ChatActions chat = BuildChat(sink, coordinator);
        await chat.SendChatAsync("after the idle period");

        // THE EFFECT: no second chat_session_update, no rotation counted. The message still goes out SIGNED, under `a` and the session already announced - the documented degrade, not the "announce and install a certificate already known to be dead" shape.
        Assert.Single(sink.Packets.OfType<ServerboundChatSessionUpdatePacket>());
        Assert.Equal(0, state.ProfileKeyRotations);
        var sent = Assert.IsType<ServerboundSignedChatPacket>(sink.Packets[^1]);
        Assert.NotNull(sent.Signature);
        Assert.True(ChatSigningSession.Verify(
            a.PublicKeyPem, sent.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 0, sent.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(sent.TimestampMillis), sent.Salt, [])));
    }

    /// <summary>Bounding the tick's fetch routed it through the same <c>AbandonAsync</c> the send path uses, which falls back to the last-known-good answer on failure - exactly right for a send (keep signing with what is installed), exactly wrong for a prefetch, which treats ANY non-null answer as fresh. A failed tick refresh must not enqueue the ALREADY-INSTALLED certificate as though it were a new one, and the next send must not report a refresh that never happened.</summary>
    [Fact]
    public async Task V3_AFailedTickRefresh_DoesNotClaimARefreshInTheNextSendsLog()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates installed = Certificates(
            "installed", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromMinutes(30));
        var provider = new ThrowingAfterFirstProvider(installed, () => new InvalidOperationException("refresh failed"));
        var sink = new RecordingSink();
        var logger = new RecordingLogger();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, logger, sink);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);

        clock.Advance(TimeSpan.FromMinutes(61));          // past the join's own hourly floor
        Task? tickFetch = coordinator.BeginRefreshIfDue(default);   // call 1: throws
        Assert.NotNull(tickFetch);
        await tickFetch!.WaitAsync(TimeSpan.FromSeconds(30));

        ChatActions chat = BuildChat(sink, coordinator);
        await chat.SendChatAsync("after the failed refresh");

        // No false "refreshed" claim, and no second chat_session_update - the failed fetch produced nothing to install, so nothing was.
        Assert.DoesNotContain(
            logger.Entries, e => e.Message.Contains("without a rotation", StringComparison.Ordinal));
        Assert.Single(sink.Packets.OfType<ServerboundChatSessionUpdatePacket>());
    }

    /// <summary>The companion effect: a bogus prefetch left behind by a failed refresh blocks <c>BeginRefreshIfDue</c> for up to an hour on a session that is connected but not chatting, because that method checks <c>_prefetched is not null</c> before anything else. A failed refresh must not cost the NEXT proactive refresh attempt.</summary>
    [Fact]
    public async Task V3_AFailedTickRefresh_DoesNotBlockTheNextProactiveRefresh()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates installed = Certificates(
            "installed", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromMinutes(30));
        var provider = new ThrowingAfterFirstProvider(installed, () => new InvalidOperationException("refresh failed"));
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        await coordinator.EnsureAsync(default);

        clock.Advance(TimeSpan.FromMinutes(61));
        Task? firstTick = coordinator.BeginRefreshIfDue(default);   // throws, armed the floor to T+121m
        Assert.NotNull(firstTick);
        await firstTick!.WaitAsync(TimeSpan.FromSeconds(30));

        // Past the floor the failed fetch itself re-armed, with NO send in between to consume a leftover prefetch: a bogus one (the bug) would still block this, because BeginRefreshIfDue checks `_prefetched is not null` before it ever looks at the floor or the due instant.
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.NotNull(coordinator.BeginRefreshIfDue(default));
    }

    /// <summary>The existing coverage for the fetch-won cancellation guard (see the test above this section) cannot fail for the bug it guards against, because <c>BlockingProvider</c> registers on the linked token BEFORE <c>AbandonAsync</c> creates its <c>Task.Delay</c>, and <c>CancellationTokenSource</c> runs registered callbacks LIFO - so the delay's callback, registered second, always runs FIRST on caller cancellation, and `expiry` always wins <c>Task.WhenAny</c>, taking the OTHER catch clause (the one the existing test's own remarks note is unguarded by construction and therefore always safe). This test drives the interleaving the FETCH-WON catch clause actually exists for: a provider whose own cancellation registration happens LAST, so it is the first LIFO callback to run and `fetch` - not `expiry` - is the task <c>Task.WhenAny</c> reports.</summary>
    /// <remarks>This does not reuse the sibling test's unobserved-task-exception mechanism: that mechanism needs a provider that does NOT observe cancellation (so its task stays gate-able after the caller cancels), which is the opposite of what winning the WhenAny race against caller cancellation needs (a provider that DOES observe the token, so its own completion is what the caller's cancellation drives). Combining the two on one provider looked plausible but is not: as soon as the provider observes the token, its task completes via CANCELLATION the moment the caller cancels, so there is nothing left to fault "later" - a `TaskCompletionSource` set after that point is simply an orphaned, permanently-unread task, which is a bug in the TEST, not a demonstration of anything about the product. What this test actually needs to prove - that the caller's own cancellation reaches the caller as <see cref="OperationCanceledException"/> rather than being swallowed into "provider failed, return the fallback" - is exactly <see cref="Assert.ThrowsAnyAsync{T}(Func{Task})"/> below, with no GC or unobserved-exception machinery required: the mutation this guards against (a bare <c>catch (Exception ex)</c> on the fetch-won branch) makes `resolve` complete NORMALLY instead of throwing, which this assertion alone already catches.</remarks>
    [Fact]
    public async Task V3_SendFetch_PropagatesTheCallersOwnCancellation_WhenFetchWinsTheRace()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PlayerCertificates initial = Certificates("initial", clock.GetUtcNow().AddDays(2));
        var provider = new LateRegisteringProvider(initial, gate.Task, entered);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, new RecordingSink());

        await coordinator.EnsureAsync(default);
        clock.Advance(TimeSpan.FromDays(3));

        using var cts = new CancellationTokenSource();
        Task resolve = coordinator.EnsureAsync(cts.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cts.CancelAsync();

        // THE EFFECT: the caller's own cancellation must reach the caller, not be swallowed into "provider failed, return the fallback" - regardless of whether `fetch` or `expiry` won the
        // internal race, which `LateRegisteringProvider` biases towards `fetch` without controlling it
        // outright (real callback-ordering timing, not a stub). The provider observes the same token, so its own await resolves via cancellation on its own; `gate` is never otherwise completed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolve);
    }

    /// <summary>Certificates that re-export the SAME key material must not start a rotation, because the server will not follow one.</summary>
    /// <remarks>
    /// <para>The server compares the announced key data with its current data and keeps the existing chat chain when they match. Equality covers expiry, encoded DER key, and signature bytes. PEM text is not on the wire and is not compared.</para>
    /// <para>Record equality is not a valid rotation test because a host can re-wrap identical key material in different PEM text. Announcing that equivalent key would re-root the client chain while the server keeps its existing link, producing an unsigned-chat disconnect.</para>
    /// <para>The assertion is the EFFECT: the chain does not re-root. The second signed message still carries message index 1 under the ORIGINAL session id, which is exactly what the server's own next chain link will expect.</para>
    /// </remarks>
    [Fact]
    public async Task V3_ReExportedPemForTheSameKey_IsNotARotation()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates original = Certificates("same", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates reExported = original with
        {
            // Same DER, different PEM text: unwrapped body, CRLF line endings, no trailing newline.
            PublicKeyPem = Rewrap(original.PublicKeyPem),
            PrivateKeyPem = Rewrap(original.PrivateKeyPem),

            // And a refresh window the host recomputed. The server never sees this field.
            RefreshedAfter = original.RefreshedAfter.AddHours(12),
        };
        Assert.NotEqual(original, reExported);

        var sink = new RecordingSink();
        var state = new ChatState();
        var provider = new CountingProvider(call => call == 0 ? original : reExported);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink, state);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);
        ChatActions chat = BuildChat(sink, coordinator);
        await chat.SendChatAsync("first");

        // Past refreshedAfter and past the throttle: the tick fetches the re-exported certificates and the next send installs whatever it decides to install.
        clock.Advance(TimeSpan.FromHours(7));
        await coordinator.BeginRefreshIfDue(default)!;
        await chat.SendChatAsync("second");

        // No second chat_session_update, and no rotation counted.
        Assert.Equal(0, state.ProfileKeyRotations);
        Assert.Single(sink.Packets.OfType<ServerboundChatSessionUpdatePacket>());
        var announcement = Assert.IsType<ServerboundChatSessionUpdatePacket>(sink.Packets[0]);
        Assert.Equal(SessionId, announcement.SessionId);

        // THE EFFECT: the chain kept going. Index 1 under the original session id verifies; index 0 under a fresh session id (what a rotation would have produced) does not.
        var second = Assert.IsType<ServerboundSignedChatPacket>(sink.Packets[2]);
        Assert.True(ChatSigningSession.Verify(
            original.PublicKeyPem, second.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 1, second.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(second.TimestampMillis), second.Salt, [])));
        Assert.False(ChatSigningSession.Verify(
            original.PublicKeyPem, second.Signature!, ChatSignatureEra.V1_19_3,
            new ChatVerificationContext(
                Sender, SessionId, MessageIndex: 0, second.Message,
                DateTimeOffset.FromUnixTimeMilliseconds(second.TimestampMillis), second.Salt, [])));

        // The replacement is still stored. Not rotating is not the same as ignoring: the host recomputed refreshedAfter twelve hours later, and if that instant is not taken up the refresh stays permanently due, so BeginRefreshIfDue fires once an hour for the life of the session and ChatState would otherwise report an instant that has already passed.
        Assert.Equal(reExported.RefreshedAfter, state.ProfileKeyRefreshedAfter);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Null(coordinator.BeginRefreshIfDue(default));
        Assert.Equal(2, provider.Calls);
    }

    /// <summary>Changing any wire-visible field independently constitutes a rotation: public key, expiry, or Mojang signature.</summary>
    [Theory]
    [InlineData("key")]
    [InlineData("expiry")]
    [InlineData("signature")]
    public async Task V3_EachFieldTheServerCompares_IsARotationOnItsOwn(string changed)
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        PlayerCertificates original = Certificates("orig", clock.GetUtcNow().AddDays(2), refreshIn: TimeSpan.FromHours(6));
        PlayerCertificates replacement = changed switch
        {
            // A genuinely new key pair, everything else held constant.
            "key" => Certificates("orig", original.ExpiresAt, refreshIn: TimeSpan.FromHours(6)) with
            {
                RefreshedAfter = original.RefreshedAfter,
            },

            // Same key bytes, same signature, one millisecond later expiry: on the wire expiresAt is epoch milliseconds, so this is the smallest difference the server can see.
            "expiry" => original with { ExpiresAt = original.ExpiresAt.AddMilliseconds(1) },

            // Same key, same expiry, a re-issued Mojang signature.
            "signature" => original with
            {
                PublicKeySignatureV2 = Convert.ToBase64String("reissued"u8.ToArray()),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(changed)),
        };

        var sink = new RecordingSink();
        var state = new ChatState();
        var provider = new CountingProvider(call => call == 0 ? original : replacement);
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3, provider, clock, NullLogger.Instance, sink, state);

        await coordinator.EnsureAsync(default);
        await coordinator.SendSessionUpdateAsync(default);
        clock.Advance(TimeSpan.FromHours(7));
        await coordinator.BeginRefreshIfDue(default)!;
        await coordinator.EnsureAsync(default);

        Assert.Equal(1, state.ProfileKeyRotations);
        List<ServerboundChatSessionUpdatePacket> announcements =
            [.. sink.Packets.OfType<ServerboundChatSessionUpdatePacket>()];
        Assert.Equal(2, announcements.Count);
        Assert.NotEqual(announcements[0].SessionId, announcements[1].SessionId);
    }

    /// <summary>The scope <c>UmpkClient</c> installs must read its coordinator field ONCE per send. <c>TeardownSessionAsync</c> sets that field to null, so the inline <c>_chatSigning is null ? send(null, ct) : _chatSigning.RunOrderedSendAsync(...)</c> it replaced read it twice and a teardown between the two reads produced a <see cref="NullReferenceException"/> instead of the unsigned path the null was meant to select.</summary>
    /// <remarks>The reader here returns the coordinator once and then null, which IS that interleaving, made deterministic. Against the two-read shape this test throws; against a single read the second answer is never asked for, so the count is exactly one.</remarks>
    [Fact]
    public async Task SendScope_ReadsTheCoordinatorExactlyOncePerSend()
    {
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);
        var sink = new RecordingSink();
        var coordinator = new ChatSigningCoordinator(
            Sender, SessionId, ChatSignatureEra.V1_19_3,
            new CountingProvider(_ => null), clock, NullLogger.Instance, sink);

        int reads = 0;
        ChatSigningScope scope = ChatSigningScopes.Create(() => ++reads == 1 ? coordinator : null);

        ChatSigningState? observed = new ChatSigningState(
            new ChatSigningSession(Sender, SessionId),
            Certificates("unused", clock.GetUtcNow().AddDays(1)),
            ChatSignatureEra.V1_19_3);
        await scope(
            (state, _) =>
            {
                observed = state;
                return ValueTask.CompletedTask;
            },
            default);

        Assert.Equal(1, reads);
        Assert.Null(observed);
    }

    /// <summary>A null coordinator is the ordinary unsigned path, not an error.</summary>
    [Fact]
    public async Task SendScope_WithNoCoordinator_RunsTheUnsignedPath()
    {
        int reads = 0;
        ChatSigningScope scope = ChatSigningScopes.Create(() =>
        {
            reads++;
            return null;
        });

        bool ran = false;
        await scope(
            (state, _) =>
            {
                ran = true;
                Assert.Null(state);
                return ValueTask.CompletedTask;
            },
            default);

        Assert.True(ran);
        Assert.Equal(1, reads);
    }

    /// <summary>Re-armors a PEM: same base64 body, different text. Strips the line breaks inside the body, uses CRLF between the armor lines, and drops the trailing newline. This is what a host provider that round trips key material through its own encoder produces, and it is invisible on the wire.</summary>
    private static string Rewrap(string pem)
    {
        string[] lines = pem.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string body = string.Concat(lines[1..^1]);
        return $"{lines[0]}\r\n{body}\r\n{lines[^1]}";
    }

    private static LastSeenMessagesUpdate EmptyAck() => new(0, new byte[3], 0);

    /// <summary>Real key material: the announcement path decodes the PEM to the DER SubjectPublicKeyInfo bytes Mojang signed and base64-decodes the signature, so a placeholder string would not survive it. <paramref name="refreshIn"/> is measured from the Unix epoch, which is where every clock in this suite starts.</summary>
    private static PlayerCertificates Certificates(
        string tag, DateTimeOffset expiresAt, TimeSpan? refreshIn = null)
    {
        using RSA rsa = RSA.Create(2048);
        byte[] signature = System.Text.Encoding.UTF8.GetBytes(tag.PadRight(8, '.'));
        return new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(),
            rsa.ExportPkcs8PrivateKeyPem(),
            Convert.ToBase64String(signature),
            Convert.ToBase64String(signature),
            expiresAt,
            refreshIn is { } window ? DateTimeOffset.UnixEpoch + window : expiresAt.AddHours(-1));
    }

    private sealed class CountingProvider(Func<int, PlayerCertificates?> factory) : IChatSigningProvider
    {
        public int Calls { get; private set; }

        public ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(factory(Calls++));
    }

    /// <summary>A provider that always fails AND consumes session time while it does, so the difference between arming the throttle at the start of a fetch and arming it at the end is observable on the clock.</summary>
    private sealed class ClockAdvancingProvider(MutableClock clock, TimeSpan cost) : IChatSigningProvider
    {
        public int Calls { get; private set; }

        public ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
        {
            Calls++;
            clock.Advance(cost);
            return ValueTask.FromResult<PlayerCertificates?>(null);
        }
    }

    /// <summary>A provider that answers once and then blocks forever WITHOUT observing its cancellation token, which <see cref="IChatSigningProvider"/> permits. <see cref="BlockingProvider"/> is the cooperative twin and cannot exercise the case the deadline exists for: cancelling its token is what unblocks it, so a token-only "deadline" passes against it and hangs against this one.</summary>
    private sealed class UncooperativeProvider(PlayerCertificates first, Task gate, TaskCompletionSource entered)
        : IChatSigningProvider
    {
        public int Calls { get; private set; }

        public async ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
        {
            if (Calls++ == 0)
                return first;

            entered.TrySetResult();

            // Deliberately NOT WaitAsync(cancellationToken): this is a host awaiting an I/O call that never returns, which is in contract and unbounded by any token.
            await gate.ConfigureAwait(false);
            return null;
        }
    }

    private sealed class BlockingProvider(PlayerCertificates first, Task gate, TaskCompletionSource entered)
        : IChatSigningProvider
    {
        public int Calls { get; private set; }

        public bool Cancelled { get; private set; }

        public async ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
        {
            if (Calls++ == 0)
                return first;

            entered.TrySetResult();
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            return null;
        }
    }

    /// <summary>Deliberately delays registering on its cancellation token until AFTER yielding once, so that <c>AbandonAsync</c>'s own <c>Task.Delay(FetchTimeout, _time, linked.Token)</c> - created on the very next line after this provider's call starts - registers on the SAME linked token first. <see cref="CancellationTokenSource"/> runs its registered callbacks in LIFO order, so the LAST one registered is the FIRST to run: with a provider that registers immediately (<see cref="BlockingProvider"/>), the delay's callback is always registered second and therefore always runs FIRST on caller cancellation, so `expiry` always wins <see cref="Task.WhenAny(Task, Task)"/> and the "fetch won" branch of <c>AbandonAsync</c> is never reached that way. Registering here, after the yield, makes THIS provider's own cancellation-driven completion the one <c>Task.WhenAny</c> reports first instead, which is the interleaving the guard in that branch actually exists for.</summary>
    private sealed class LateRegisteringProvider(PlayerCertificates first, Task gate, TaskCompletionSource entered)
        : IChatSigningProvider
    {
        public int Calls { get; private set; }

        public async ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
        {
            if (Calls++ == 0)
                return first;

            await Task.Yield();
            entered.TrySetResult();
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>Answers once, then throws whatever <paramref name="toThrow"/> produces on every later call - after an <see cref="Task.Yield"/>, so the exception surfaces from an AWAITED task (matching an HTTP call that faults) rather than synchronously at the call site. Used for the containment tests.</summary>
    private sealed class ThrowingAfterFirstProvider(PlayerCertificates first, Func<Exception> toThrow)
        : IChatSigningProvider
    {
        public int Calls { get; private set; }

        public async ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
        {
            if (Calls++ == 0)
                return first;

            await Task.Yield();
            throw toThrow();
        }
    }

    /// <summary>Answers calls in a fixed, INDEX-based order rather than by call count alone: the second call awaits an externally-controlled task, so a test can start it, let a LATER call complete first, and then resolve the second call's answer - reproducing a tick fetch that started before a send-path recovery but completes after it. Used for the staleness-guard test.</summary>
    private sealed class OrderedProvider(
        PlayerCertificates first, Task<PlayerCertificates?> slowSecondAnswer, PlayerCertificates third)
        : IChatSigningProvider
    {
        private readonly TaskCompletionSource _secondCallEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Completes the instant call index 1 (the tick's fetch) has claimed its slot and is about to block on <c>slowSecondAnswer</c>. A caller MUST await this before triggering whatever it wants to be call index 2, or the two calls race on wall-clock timing alone (see remarks below) with no guarantee the tick's real, Task.Run-dispatched call reaches this method before a directly awaited later call does.</summary>
        public Task SecondCallEntered => _secondCallEntered.Task;

        public async ValueTask<PlayerCertificates?> GetCertificatesAsync(CancellationToken cancellationToken)
        {
            // MUST be Interlocked, not `Calls++`: call 1 (the tick's fetch) genuinely runs concurrently with whatever the test triggers as call 2, by this provider's own design - that overlap is the whole point of the stale-answer scenario. A plain `Calls++` is a data race between those two threads: on the interleaving where both read the same pre-increment value, BOTH calls resolve to the same switch arm, and if that arm is case 1, the LATER call ends up awaiting `slowSecondAnswer` too - which only the test resolves, and only AFTER that same call returns. That is a self-deadlock, and by itself was not sufficient to explain every hang: even with the race on the COUNTER fixed, nothing forced the tick's call to reach this method BEFORE the test's own directly-awaited later call, since the tick's call arrives via a real Task.Run with no ordering guarantee against a plain await. `SecondCallEntered` lets a caller wait until call index 1 has claimed its slot before triggering what must be call index 2, replacing an assumption about relative thread-scheduling speed with an explicit signal.
            int call = Interlocked.Increment(ref _calls) - 1;
            if (call == 1)
                _secondCallEntered.TrySetResult();

            return call switch
            {
                0 => first,
                1 => await slowSecondAnswer.ConfigureAwait(false),
                _ => third,
            };
        }
    }

    /// <summary>An <see cref="ILogger"/> spy: records every entry's level and formatted message.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A real <see cref="ChatActions"/> wired to the coordinator through the production seam, so a test exercises the send path rather than a hand-rolled imitation of it. This is the same lambda <c>UmpkClient</c> installs.</summary>
    private static ChatActions BuildChat(IPacketSink sink, ChatSigningCoordinator coordinator)
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        var services = new ClientSessionServices
        {
            Version = JavaVersions.V1_21_5,
            Options = new ClientOptions { ChatCooldown = TimeSpan.Zero },
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(JavaVersions.V1_21_5),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };

        return new ChatActions(
            sink,
            services,
            new CommandCompletionService(sink),
            new CommandService<ClientCommandSource>(),
            () => null!,
            (send, ct) => coordinator.RunOrderedSendAsync(send, ct));
    }

    /// <summary>A sink that parks the FIRST signed-chat frame before recording it, so a second sender gets a real chance to overtake it. Recording after the gate is what makes the frame ORDER, not the entry order, the thing under test.</summary>
    private sealed class GatedSink : IPacketSink
    {
        private readonly TaskCompletionSource _gate = new();
        private readonly TaskCompletionSource _firstChatEntered = new();
        private TaskCompletionSource _sawSessionUpdate = new();
        private int _chatSends;

        public List<object> Packets { get; } = [];

        /// <summary>Completes once the first signed-chat send is parked inside the sink.</summary>
        public Task FirstChatEntered => _firstChatEntered.Task;

        /// <summary>Completes if a <c>chat_session_update</c> is written after the last <see cref="Reset"/>.</summary>
        public Task SawSessionUpdate => _sawSessionUpdate.Task;

        /// <summary>Forgets everything recorded so far, INCLUDING the connect-time announcement. Without resetting the session-update signal the join announcement would satisfy it and the ordering test would report a rotation that never happened.</summary>
        public void Reset()
        {
            Packets.Clear();
            _sawSessionUpdate = new TaskCompletionSource();
        }

        public void Release() => _gate.TrySetResult();

        public async ValueTask SendAsync(object packet, CancellationToken ct)
        {
            if (packet is ServerboundSignedChatPacket && Interlocked.Increment(ref _chatSends) == 1)
            {
                _firstChatEntered.TrySetResult();
                await _gate.Task.ConfigureAwait(false);
            }

            Packets.Add(packet);
            if (packet is ServerboundChatSessionUpdatePacket)
                _sawSessionUpdate.TrySetResult();

        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
            => ValueTask.CompletedTask;
    }

    private sealed class ThrowingSink : IPacketSink
    {
        public bool Fail { get; set; }

        public List<object> Packets { get; } = [];

        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            if (Fail)
                throw new IOException("send failed");

            Packets.Add(packet);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
            => ValueTask.CompletedTask;
    }

    /// <summary>Virtual session time. <see cref="CreateTimer"/> is overridden as well as <see cref="GetUtcNow"/> because the coordinator's fetch deadline is a <see cref="CancellationTokenSource"/> built on THIS provider; without it the base class would hand out a real 30-second wall-clock timer and the deadline would be unreachable in a test.</summary>
    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        private readonly List<VirtualTimer> _timers = [];
        private readonly Lock _gate = new();
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
                return _now;

        }

        public override long GetTimestamp() => GetUtcNow().Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            VirtualTimer[] due;
            lock (_gate)
            {
                _now += by;
                due = [.. _timers];
            }

            foreach (VirtualTimer timer in due)
                timer.MaybeFire(GetUtcNow());

        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new VirtualTimer(this, callback, state, GetUtcNow() + dueTime);
            lock (_gate)
                _timers.Add(timer);

            return timer;
        }

        private void Remove(VirtualTimer timer)
        {
            lock (_gate)
                _timers.Remove(timer);

        }

        private sealed class VirtualTimer(MutableClock owner, TimerCallback callback, object? state, DateTimeOffset dueAt)
            : ITimer
        {
            private int _fired;
            private DateTimeOffset _dueAt = dueAt;

            public void MaybeFire(DateTimeOffset now)
            {
                if (now >= _dueAt && Interlocked.Exchange(ref _fired, 1) == 0)
                    callback(state);

            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _dueAt = owner.GetUtcNow() + dueTime;
                Interlocked.Exchange(ref _fired, 0);
                return true;
            }

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                owner.Remove(this);
                return ValueTask.CompletedTask;
            }
        }
    }
}
