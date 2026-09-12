using System.Security.Cryptography;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Proves the signed-chat decode-path wiring: an inbound <see cref="ClientboundPlayerChatPacket"/> is mapped onto the session-side <see cref="SignedChatVerifier"/> by <see cref="SignedChatVerification"/>. Uses a real generated profile key and the v3 signing session to produce a valid signature, so the association is verified end-to-end rather than by a hand-forged frame.</summary>
public class SignedChatVerificationTests
{
    private static readonly Guid Sender = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
    private static readonly Guid Session = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid PeerA = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PeerB = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly DateTimeOffset Ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private const long Salt = 0x0102030405060708L;

    /// <summary>1.19.1/1.19.2, the only protocol carrying the v2 signed-chat generation.</summary>
    private const int V2Protocol = 760;

    [Fact]
    public void PlayerChat_V3_ValidSignature_VerifiesThroughSession()
    {
        using RSA rsa = RSA.Create(2048);
        var certs = new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), "", "",
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
        var signer = new ChatSigningSession(Sender, Session);
        byte[] signature = signer.Sign(certs, ChatSignatureEra.V1_19_3, "hello", Ts, Salt, []);

        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: signature, SignedContent: "hello",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Steve"), TargetName: null);

        var verifier = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
        ChatVerificationOutcome outcome = SignedChatVerification.Verify(packet, verifier, Ts);

        Assert.Equal(ChatVerificationStatus.Ok, outcome.Status);
        Assert.False(outcome.ShouldDisconnect);
    }

    [Fact]
    public void PlayerChat_NoSignature_ReturnsUnsignedWithoutThrowing()
    {
        using RSA rsa = RSA.Create(2048);
        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: null, SignedContent: "hi",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Steve"), TargetName: null);
        var verifier = new SignedChatVerifier(
            ChatSignatureEra.V1_19_3, rsa.ExportSubjectPublicKeyInfoPem(),
            DateTimeOffset.UtcNow.AddHours(1), Sender, Session);

        ChatVerificationOutcome outcome = SignedChatVerification.Verify(packet, verifier, Ts);
        Assert.Equal(ChatVerificationStatus.UnsignedChat, outcome.Status);
    }

    /// <summary>A stand-in for the SERVER's own per-connection <see cref="MessageSignatureCache"/>, used only to decide whether a given signature should ride the wire as a cache-id reference or as full bytes. Building the multi-message tests' wire shape through this (rather than hand-picking ids that happen to make the client pass) keeps the cases independent of the verification path under test.</summary>
    private sealed class ServerSignatureCacheView
    {
        private readonly MessageSignatureCache _cache = new();

        public PackedMessageSignature Pack(byte[] signature)
        {
            for (int i = 0; i < MessageSignatureCache.DefaultCapacity; i++)
                if (_cache.Unpack(i) is { } existing && existing.AsSpan().SequenceEqual(signature))
                    return PackedMessageSignature.Cached(i);

            return PackedMessageSignature.Full(signature);
        }

        public void Push(IReadOnlyList<byte[]> lastSeenSignatures, byte[] ownSignature) =>
            _cache.Push(lastSeenSignatures, ownSignature);
    }

    /// <summary>The exact mechanism traced from an authenticated soak log. Message #1 has an empty last-seen window and verifies regardless of any cache. By message #2 the sender has acked #1 (its own echo), and because the server had JUST sent #1's signature down this same connection, its own <c>MessageSignatureCache</c> already holds it -- so the wire carries a cache-id reference, not the 256 raw bytes for that entry. The UMPK codec reads and writes the same packed shape. The signature was produced over the REAL last-seen list (with #1's actual bytes, from <see cref="ChatSigningSession.Sign"/>), so the receiver must resolve the cache-id back to those same bytes to reconstruct the identical payload; dropping it reconstructs a payload the sender never signed.</summary>
    [Fact]
    public void PlayerChat_V3_SecondMessageAcksTheFirstAsACacheIdReference_ResolvesThroughTheSharedCacheAndVerifies()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);
        var signer = new ChatSigningSession(Sender, Session);
        var verifier = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
        var cache = new MessageSignatureCache();

        // Message #1, "packetsizeprobe-1" in the log: nothing acked yet.
        byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "packetsizeprobe-1", Ts, Salt, []);
        var packet1 = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: sig1, SignedContent: "packetsizeprobe-1",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null);
        Assert.Equal(ChatVerificationStatus.Ok, SignedChatVerification.Verify(packet1, verifier, Ts, cache).Status);

        // Message #2, "packetsizeprobe-2": really signed with #1's signature in its last-seen window.
        DateTimeOffset ts2 = Ts.AddSeconds(1);
        var signWindow = new List<AcknowledgedMessage> { new(Guid.Empty, sig1) };
        byte[] sig2 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "packetsizeprobe-2", ts2, Salt, signWindow);

        // But the WIRE, as a real server would send it: slot 0 is where message #1's own push landed it (pinned separately by MessageSignatureCacheTests.Push_ThenUnpackTheSamePushedSignature_RoundTrips), so a compliant server packs a reference instead of resending the 256 bytes.
        var packet2 = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: sig2, SignedContent: "packetsizeprobe-2",
            TimestampMillis: ts2.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Cached(0)],
        };

        ChatVerificationOutcome outcome = SignedChatVerification.Verify(packet2, verifier, ts2, cache);

        Assert.Equal(ChatVerificationStatus.Ok, outcome.Status);
        Assert.False(verifier.IsBroken);
    }

    /// <summary>Messages one through three verify in sequence, with every last-seen entry's wire shape (cache-id vs. full) decided by <see cref="ServerSignatureCacheView"/> rather than hand-picked, so the packing matches what a real 1.21.8 server would actually put on the wire at each step, not merely whatever makes this test pass.</summary>
    [Fact]
    public void PlayerChat_V3_ThreeMessageChain_WithServerDerivedLastSeenPacking_AllVerifyInSequence()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);
        var signer = new ChatSigningSession(Sender, Session);
        var verifier = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
        var clientCache = new MessageSignatureCache();
        var server = new ServerSignatureCacheView();

        // #1: nothing acked.
        byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "packetsizeprobe-1", Ts, Salt, []);
        var packet1 = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: sig1, SignedContent: "packetsizeprobe-1",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null);
        Assert.Equal(ChatVerificationStatus.Ok, SignedChatVerification.Verify(packet1, verifier, Ts, clientCache).Status);
        server.Push([], sig1);

        // #2: acks #1. The server just sent #1's signature down this connection, so it packs a cache-id.
        DateTimeOffset ts2 = Ts.AddSeconds(1);
        var window2 = new List<AcknowledgedMessage> { new(Guid.Empty, sig1) };
        byte[] sig2 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "packetsizeprobe-2", ts2, Salt, window2);
        PackedMessageSignature wire2Entry = server.Pack(sig1);
        Assert.NotEqual(PackedMessageSignature.FullSignatureId, wire2Entry.Id); // really compressed, not full bytes
        var packet2 = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: sig2, SignedContent: "packetsizeprobe-2",
            TimestampMillis: ts2.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null)
        {
            LastSeen = [wire2Entry],
        };
        Assert.Equal(ChatVerificationStatus.Ok, SignedChatVerification.Verify(packet2, verifier, ts2, clientCache).Status);
        server.Push([sig1], sig2);

        // #3: acks both #1 and #2, oldest to newest, the shape LastSeenMessagesTracker.Generate produces.
        // Both are cache hits by now.
        DateTimeOffset ts3 = Ts.AddSeconds(2);
        var window3 = new List<AcknowledgedMessage> { new(Guid.Empty, sig1), new(Guid.Empty, sig2) };
        byte[] sig3 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "packetsizeprobe-3", ts3, Salt, window3);
        PackedMessageSignature wire3Entry1 = server.Pack(sig1);
        PackedMessageSignature wire3Entry2 = server.Pack(sig2);
        Assert.NotEqual(PackedMessageSignature.FullSignatureId, wire3Entry1.Id);
        Assert.NotEqual(PackedMessageSignature.FullSignatureId, wire3Entry2.Id);
        var packet3 = new ClientboundPlayerChatPacket(
            Sender, Index: 2, Signature: sig3, SignedContent: "packetsizeprobe-3",
            TimestampMillis: ts3.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null)
        {
            LastSeen = [wire3Entry1, wire3Entry2],
        };

        ChatVerificationOutcome outcome3 = SignedChatVerification.Verify(packet3, verifier, ts3, clientCache);

        Assert.Equal(ChatVerificationStatus.Ok, outcome3.Status);
        Assert.False(verifier.IsBroken);
        Assert.Equal(3, verifier.NextIndex);
    }

    /// <summary>Chain state is per sender, but the signature cache is shared by the connection. Peer B's first message acknowledges peer A's message by a cache id the server assigned when it broadcast A's message on this same connection, so resolving it requires the cache A's own push populated, not a cache scoped to B's verifier.</summary>
    [Fact]
    public void PlayerChat_V3_CrossSenderLastSeen_ResolvesOnlyThroughASharedCache_NotAPerVerifierOne()
    {
        using RSA rsaA = RSA.Create(2048);
        using RSA rsaB = RSA.Create(2048);
        PlayerCertificates certsA = Certificates(rsaA);
        PlayerCertificates certsB = Certificates(rsaB);
        Guid sessionA = Guid.NewGuid();
        Guid sessionB = Guid.NewGuid();

        var signerA = new ChatSigningSession(PeerA, sessionA);
        var verifierA = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certsA.PublicKeyPem, certsA.ExpiresAt, PeerA, sessionA);
        var signerB = new ChatSigningSession(PeerB, sessionB);
        var verifierB = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certsB.PublicKeyPem, certsB.ExpiresAt, PeerB, sessionB);

        var sharedCache = new MessageSignatureCache();

        // A speaks first.
        byte[] sigA1 = signerA.Sign(certsA, ChatSignatureEra.V1_19_3, "hello from A", Ts, Salt, []);
        var packetA1 = new ClientboundPlayerChatPacket(
            PeerA, Index: 0, Signature: sigA1, SignedContent: "hello from A",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("A"), TargetName: null);
        Assert.Equal(ChatVerificationStatus.Ok, SignedChatVerification.Verify(packetA1, verifierA, Ts, sharedCache).Status);

        // B has now seen A's message on this same connection and acks it in B's own first message. The server already holds sigA1 in its cache (it just broadcast it) and packs a reference.
        DateTimeOffset tsB = Ts.AddSeconds(1);
        var windowB = new List<AcknowledgedMessage> { new(PeerA, sigA1) };
        byte[] sigB1 = signerB.Sign(certsB, ChatSignatureEra.V1_19_3, "hello from B", tsB, Salt, windowB);
        var packetB1 = new ClientboundPlayerChatPacket(
            PeerB, Index: 0, Signature: sigB1, SignedContent: "hello from B",
            TimestampMillis: tsB.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("B"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Cached(0)],
        };

        ChatVerificationOutcome outcomeSharedCache = SignedChatVerification.Verify(packetB1, verifierB, tsB, sharedCache);
        Assert.Equal(ChatVerificationStatus.Ok, outcomeSharedCache.Status);

        // Negative control: the same frame against a verifier whose cache never saw A's push (the wrong, per-verifier scope) cannot resolve the reference and must not silently drop it into a corrupted last-seen list either -- exactly the same bug shape, just from a different cause.
        var isolatedCache = new MessageSignatureCache();
        var verifierBIsolated = new SignedChatVerifier(
            ChatSignatureEra.V1_19_3, certsB.PublicKeyPem, certsB.ExpiresAt, PeerB, sessionB);
        ChatVerificationOutcome outcomeIsolatedCache = SignedChatVerification.Verify(packetB1, verifierBIsolated, tsB, isolatedCache);
        Assert.NotEqual(ChatVerificationStatus.Ok, outcomeIsolatedCache.Status);
        Assert.False(verifierBIsolated.IsBroken); // unresolvable, but must not corrupt/latch the chain either.
    }

    /// <summary>Pins the push-BEFORE-validate ORDERING specifically, not merely that both happen somewhere. A message whose own signature the chain check goes on to REJECT (tampered bytes, so <c>UnsignedChat</c>) must still have been pushed into the cache, exactly as vanilla's <c>handlePlayerChat</c> pushes unconditionally before calling <c>updateAndValidate</c> -- the push depends only on a successful last-seen RESOLVE, never on what the chain check decides afterwards. Proven by reading the cache slot directly rather than through a second message, so this cannot be satisfied by a build that pushes only on success and coincidentally still resolves a later reference some other way.</summary>
    [Fact]
    public void PlayerChat_V3_MessageThatFailsItsOwnChainCheck_IsStillPushedIntoTheCache()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);
        var signer = new ChatSigningSession(Sender, Session);
        var verifier = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
        var cache = new MessageSignatureCache();

        byte[] realSig = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
        byte[] tamperedSig = [.. realSig];
        tamperedSig[0] ^= 0xFF;
        var packet = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: tamperedSig, SignedContent: "one",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null);

        ChatVerificationOutcome outcome = SignedChatVerification.Verify(packet, verifier, Ts, cache);

        Assert.Equal(ChatVerificationStatus.UnsignedChat, outcome.Status);
        Assert.True(verifier.IsBroken);

        // The load-bearing assertion: the (tampered) signature the wire actually carried is resolvable at slot 0 regardless of the chain check's verdict. This fails the moment the push is ever moved to run only after a successful chain check.
        Assert.Equal(tamperedSig, cache.Unpack(0));
    }

    /// <summary>An unresolvable last-seen entry is reported unverifiable WITHOUT touching THIS message's own chain state (already covered by other tests), AND marks the shared cache desynced, so a LATER cache-id reference -- even one to a slot the cache genuinely still holds -- also takes the non-latching unresolvable arm rather than risking a silent resolve to a real-but-wrong signature once this side's view of the cache can no longer be trusted to match the server's.</summary>
    [Fact]
    public void PlayerChat_V3_UnresolvableLastSeenEntry_MarksTheCacheDesyncedForEveryLaterMessage()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);
        var signer = new ChatSigningSession(Sender, Session);
        var verifier = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
        var cache = new MessageSignatureCache();

        // #1: verifies normally and pushes its own signature to slot 0.
        byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
        var packet1 = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: sig1, SignedContent: "one",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null);
        Assert.Equal(ChatVerificationStatus.Ok, SignedChatVerification.Verify(packet1, verifier, Ts, cache).Status);
        Assert.False(cache.IsDesynced);

        // #2 references cache id 5, which this cache never held anything at (only slot 0 is populated).
        DateTimeOffset ts2 = Ts.AddSeconds(1);
        var window2 = new List<AcknowledgedMessage> { new(Guid.Empty, sig1) };
        byte[] sig2 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "two", ts2, Salt, window2);
        var packet2 = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: sig2, SignedContent: "two",
            TimestampMillis: ts2.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Cached(5)],
        };

        ChatVerificationOutcome outcome2 = SignedChatVerification.Verify(packet2, verifier, ts2, cache);
        Assert.Equal(ChatVerificationStatus.UnsignedChat, outcome2.Status);
        Assert.False(verifier.IsBroken);
        Assert.True(cache.IsDesynced);

        // #3 references cache id 0, which genuinely still holds sig1 -- but because the cache is now
        // desynced, this must ALSO be refused rather than silently trusted.
        DateTimeOffset ts3 = Ts.AddSeconds(2);
        var window3 = new List<AcknowledgedMessage> { new(Guid.Empty, sig1) };
        byte[] sig3 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "three", ts3, Salt, window3);
        var packet3 = new ClientboundPlayerChatPacket(
            Sender, Index: 2, Signature: sig3, SignedContent: "three",
            TimestampMillis: ts3.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Cached(0)],
        };

        ChatVerificationOutcome outcome3 = SignedChatVerification.Verify(packet3, verifier, ts3, cache);
        Assert.Equal(ChatVerificationStatus.UnsignedChat, outcome3.Status);
        Assert.False(verifier.IsBroken);
    }

    /// <summary>Pins the TOTAL, PERMANENT verification blackout that was actually measured, not merely "later cache-id references fail", which is what an earlier version of the docs implied. After ONE message takes the unresolvable arm, the peer's chain index never advances past it, so the VERY NEXT message -- even one whose own window is EMPTY, needing no cache lookup at all -- also fails, through the pre-existing crypto-check/<see cref="SignedChatVerifier.IsBroken"/> mechanism, not through <c>RecordForCache</c>'s unresolvable arm a second time. From there the chain is latched and every later message for that peer is <see cref="ChatVerificationStatus.ChainBroken"/>.</summary>
    [Fact]
    public void PlayerChat_V3_AfterPoisoning_TheNextMessageFailsEvenWithAFullInlineSignature()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);
        var signer = new ChatSigningSession(Sender, Session);
        var verifier = new SignedChatVerifier(ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
        var cache = new MessageSignatureCache();

        // #1: verifies, advances the verifier's chain index to 1.
        byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
        var packet1 = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: sig1, SignedContent: "one",
            TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null);
        Assert.Equal(ChatVerificationStatus.Ok, SignedChatVerification.Verify(packet1, verifier, Ts, cache).Status);
        Assert.Equal(1, verifier.NextIndex);

        // #2: acks #1 by a cache id this cache never held -- poisons the cache, and never reaches
        // verifier.Verify at all, so the chain index stays at 1 even though the SENDER's real index moved on.
        DateTimeOffset ts2 = Ts.AddSeconds(1);
        var window2 = new List<AcknowledgedMessage> { new(Guid.Empty, sig1) };
        byte[] sig2 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "two", ts2, Salt, window2);
        var packet2 = new ClientboundPlayerChatPacket(
            Sender, Index: 1, Signature: sig2, SignedContent: "two",
            TimestampMillis: ts2.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Cached(5)],
        };
        Assert.Equal(ChatVerificationStatus.UnsignedChat, SignedChatVerification.Verify(packet2, verifier, ts2, cache).Status);
        Assert.True(cache.IsDesynced);
        Assert.False(verifier.IsBroken);
        Assert.Equal(1, verifier.NextIndex); // unchanged: #2 never reached the chain check

        // #3: an EMPTY last-seen window -- a fully inline-signed message that needs no cache lookup at
        // all. The old, softer doc claim ("only later cache-id references fail") would predict this verifies. It must not: the sender actually signed #3 with its real running index (2, since the signer's own counter advanced on #2 too, whether or not this side ever checked it), but this side's verifier is still expecting index 1, so the reconstructed payload does not match the signature and the crypto check fails.
        DateTimeOffset ts3 = Ts.AddSeconds(2);
        byte[] sig3 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "three", ts3, Salt, []);
        var packet3 = new ClientboundPlayerChatPacket(
            Sender, Index: 2, Signature: sig3, SignedContent: "three",
            TimestampMillis: ts3.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null);

        ChatVerificationOutcome outcome3 = SignedChatVerification.Verify(packet3, verifier, ts3, cache);

        Assert.Equal(ChatVerificationStatus.UnsignedChat, outcome3.Status);
        Assert.True(verifier.IsBroken);

        // And #4, with nothing wrong with it either, is now flatly ChainBroken -- the cascade the corrected docs describe, not a one-off refusal.
        DateTimeOffset ts4 = Ts.AddSeconds(3);
        byte[] sig4 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "four", ts4, Salt, []);
        var packet4 = new ClientboundPlayerChatPacket(
            Sender, Index: 3, Signature: sig4, SignedContent: "four",
            TimestampMillis: ts4.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("milutinke"), TargetName: null);
        Assert.Equal(
            ChatVerificationStatus.ChainBroken,
            SignedChatVerification.Verify(packet4, verifier, ts4, cache).Status);
    }

    /// <summary>The v2 (1.19.1/1.19.2) case the empty-window tests structurally cannot reach: a NON-EMPTY last-seen window verifies, and the peer's chain stays usable afterwards.</summary>
    /// <remarks>
    /// <para>Version 1.19.2 hashes each last-seen entry's sender UUID into the signed body. Replacing those UUIDs with the chat message's sender changes the digest, so the header signature mismatches, <c>SignedChatVerifier.Verify</c> returned <c>UnsignedChat</c>, and because that outcome sets the verifier's broken flag, every later message from that peer came back <c>ChainBroken</c> forever.</para>
    /// <para>The window here deliberately holds two entries from two DIFFERENT peers, neither of them the chat sender, so a build that substitutes any single uuid for all of them cannot pass. A second message follows, because "did not break the chain" is only demonstrated by the next message still working.</para>
    /// </remarks>
    [Fact]
    public void PlayerChat_V2_NonEmptyWindow_VerifiesAndLeavesTheChainUsable()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);

        var window = new List<AcknowledgedMessage>
        {
            new(PeerA, Sig(0x10)),
            new(PeerB, Sig(0x20)),
        };

        var signer = new ChatSigningSession(Sender, Session);
        var verifier = new SignedChatVerifier(
            ChatSignatureEra.V1_19_1, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

        byte[] first = signer.Sign(certs, ChatSignatureEra.V1_19_1, "hello world", Ts, Salt, window);
        ChatVerificationOutcome outcome = SignedChatVerification.Verify(
            V2Packet("hello world", first, Ts, window), verifier, Ts);

        Assert.Equal(ChatVerificationStatus.Ok, outcome.Status);
        Assert.False(outcome.ShouldDisconnect);
        Assert.Null(outcome.ReasonKey);
        Assert.False(verifier.IsBroken);

        // The chain is not merely unbroken, it still carries: the next message verifies too. It announces the first message's signature as its preceding link, exactly as the 1.19.2 wire does.
        DateTimeOffset next = Ts.AddSeconds(1);
        byte[] second = signer.Sign(certs, ChatSignatureEra.V1_19_1, "and again", next, Salt, window);
        ChatVerificationOutcome outcome2 = SignedChatVerification.Verify(
            V2Packet("and again", second, next, window, first), verifier, next);

        Assert.Equal(ChatVerificationStatus.Ok, outcome2.Status);
        Assert.False(verifier.IsBroken);
    }

    /// <summary>The v2 signed header's preceding signature must survive the wire and reach the verifier, because the peer hashed the header it actually sent. This is the end-to-end form of a second decode that had no consumer: <see cref="ClientboundPlayerChatPacket.PreviousSignature"/> was decoded, written back on encode, and read by nothing, while the verifier rebuilt the header from its own tracked signature. A client joining a conversation already in progress therefore failed the very first message it saw from every peer, and broke that peer's chain permanently.</summary>
    [Fact]
    public void PlayerChat_V2_FirstMessageFromAnAlreadyRunningChain_VerifiesThroughTheWire()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);

        var window = new List<AcknowledgedMessage> { new(PeerA, Sig(0x10)), new(PeerB, Sig(0x20)) };
        var signer = new ChatSigningSession(Sender, Session);

        // Two messages sent before this session joined, which it never received.
        signer.Sign(certs, ChatSignatureEra.V1_19_1, "before we joined", Ts, Salt, window);
        byte[] unseen = signer.Sign(certs, ChatSignatureEra.V1_19_1, "also before", Ts, Salt, window);

        DateTimeOffset ts1 = Ts.AddSeconds(1);
        byte[] signature = signer.Sign(certs, ChatSignatureEra.V1_19_1, "first we see", ts1, Salt, window);

        ClientboundPlayerChatPacket packet = V2Packet("first we see", signature, ts1, window, unseen);
        Assert.Equal(unseen, packet.PreviousSignature);

        var verifier = new SignedChatVerifier(
            ChatSignatureEra.V1_19_1, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
        ChatVerificationOutcome outcome = SignedChatVerification.Verify(packet, verifier, ts1);

        Assert.Equal(ChatVerificationStatus.Ok, outcome.Status);
        Assert.False(verifier.IsBroken);
    }

    /// <summary>The negative control that makes the test above mean something: the per-entry profile id is what the verification actually consumes. The peer signs over the true window, but the packet declares the same signatures paired with the CHAT SENDER's uuid. That substitution must fail and break the chain.</summary>
    [Fact]
    public void PlayerChat_V2_WindowWithSubstitutedProfileIds_FailsAndBreaksTheChain()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);

        var trueWindow = new List<AcknowledgedMessage> { new(PeerA, Sig(0x10)), new(PeerB, Sig(0x20)) };
        var substituted = new List<AcknowledgedMessage> { new(Sender, Sig(0x10)), new(Sender, Sig(0x20)) };

        var signer = new ChatSigningSession(Sender, Session);
        var verifier = new SignedChatVerifier(
            ChatSignatureEra.V1_19_1, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

        byte[] signature = signer.Sign(certs, ChatSignatureEra.V1_19_1, "hello world", Ts, Salt, trueWindow);
        ChatVerificationOutcome outcome = SignedChatVerification.Verify(
            V2Packet("hello world", signature, Ts, substituted), verifier, Ts);

        Assert.Equal(ChatVerificationStatus.UnsignedChat, outcome.Status);
        Assert.True(verifier.IsBroken);

        // And the follow-on damage: once broken, a perfectly good message is refused as chain-broken.
        DateTimeOffset next = Ts.AddSeconds(1);
        byte[] good = signer.Sign(certs, ChatSignatureEra.V1_19_1, "and again", next, Salt, trueWindow);
        Assert.Equal(
            ChatVerificationStatus.ChainBroken,
            SignedChatVerification.Verify(
                V2Packet("and again", good, next, trueWindow, signature), verifier, next).Status);
    }

    /// <summary>Version 3 has no profile id in its last-seen window. The packed wire entries carry only a cache id or full signature, so verification is identical whether the model's optional profile id is populated or not.</summary>
    [Fact]
    public void PlayerChat_V3_ProfileIdOnTheWindowIsIgnored()
    {
        using RSA rsa = RSA.Create(2048);
        PlayerCertificates certs = Certificates(rsa);

        var window = new List<AcknowledgedMessage> { new(Guid.Empty, Sig(0x10)) };
        var signer = new ChatSigningSession(Sender, Session);
        byte[] signature = signer.Sign(certs, ChatSignatureEra.V1_19_3, "hello", Ts, Salt, window);

        // Decoded from a v3 frame the entry has no profile id at all; a hand-built one that carries a bogus profile id must verify exactly the same, because the v3 payload never reads it.
        foreach (Guid claimed in new[] { Guid.Empty, PeerA })
        {
            var packet = new ClientboundPlayerChatPacket(
                Sender, Index: 0, Signature: signature, SignedContent: "hello",
                TimestampMillis: Ts.ToUnixTimeMilliseconds(), Salt: Salt,
                UnsignedContent: null, ChatTypeId: 0,
                SenderName: Component.Text("Steve"), TargetName: null)
            {
                LastSeen = [PackedMessageSignature.Full(claimed, Sig(0x10))],
            };

            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
            Assert.Equal(ChatVerificationStatus.Ok, SignedChatVerification.Verify(packet, verifier, Ts).Status);
        }
    }

    /// <summary>Builds the inbound packet and passes it through the bound protocol-760 <c>player_chat</c> codec, so the window the verifier sees is one that actually survived the 1.19.1/1.19.2 wire. Handing the verifier a record built in memory would prove only that the bridge reads the record; a codec that dropped the per-entry uuid would still pass. Going through the wire is what makes the whole path, decode included, load bearing.</summary>
    private static ClientboundPlayerChatPacket V2Packet(
        string content, byte[] signature, DateTimeOffset timestamp,
        IReadOnlyList<AcknowledgedMessage> window, byte[]? precedingSignature = null)
    {
        var built = new ClientboundPlayerChatPacket(
            Sender, Index: 0, Signature: signature, SignedContent: content,
            TimestampMillis: timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 1,
            SenderName: Component.Text("Steve"), TargetName: null)
        {
            PreviousSignature = precedingSignature,
            LastSeen = [.. window.Select(e => PackedMessageSignature.Full(e.ProfileId, e.Signature))],
        };

        var decoded = (ClientboundPlayerChatPacket)BoundDescriptorCodec.RoundTrip(
            V2Protocol, "player_chat", built);

        // The wire really did carry the profile ids, entry for entry and in order, and the signed header's preceding signature survived alongside them.
        Assert.Equal(
            window.Select(e => e.ProfileId).ToArray(),
            decoded.LastSeen.Select(e => e.ProfileId).ToArray());
        Assert.Equal(precedingSignature, decoded.PreviousSignature);
        return decoded;
    }

    private static PlayerCertificates Certificates(RSA rsa) =>
        new(rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), "sig", "sigv2",
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

    private static byte[] Sig(int seed)
    {
        var bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 31) + seed);

        return bytes;
    }

    [Fact]
    public void DisplayContent_PrefersUnsignedOverride()
    {
        var packet = new ClientboundPlayerChatPacket(
            Sender, 0, null, "signed body", 1L, 2L,
            UnsignedContent: Component.Text("unsigned override"), ChatTypeId: 0,
            SenderName: Component.Text("Steve"), TargetName: null);
        Assert.Equal("unsigned override", SignedChatVerification.DisplayContent(packet, ChatFilterMask.PassThrough)!.ToPlainText());
    }
}
