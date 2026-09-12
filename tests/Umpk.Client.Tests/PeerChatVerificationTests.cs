using System.Security.Cryptography;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// Inbound per-peer signature verification, end to end: a peer announces its profile key on the player-info roster, then sends a signed <c>player_chat</c>, and the published event must say <see cref="ChatVerification.Verified"/>.
/// <para>Verification requires the resolver, the decoded <c>INITIALIZE_CHAT</c> payload in <c>TabListEntry.ChatSession</c>, and the legacy <c>LegacyPlayerListEntry.ProfileKey</c> source.</para>
/// <para>The assertion is on <see cref="ChatVerification.Verified"/> rather than only on resolver presence.</para>
/// </summary>
public sealed class PeerChatVerificationTests
{
    private const int ModernProtocol = 770;
    private const int LegacyChatKeyProtocol = 760;

    private static readonly Guid Peer = Guid.Parse("bd90c77b-03cb-394f-bdc0-e4ff70a95c6a");
    private static readonly Guid PeerSession = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private const long Salt = 0x0102_0304_0506_0708L;
    private const string Content = "verifiable words";

    /// <summary>A decorated component in the protocol-760 wire key order.</summary>
    private const string DecoratedWire =
        "{\"bold\":true,\"italic\":false,\"color\":\"#AABBCC\",\"insertion\":\"x\","
        + "\"extra\":[{\"text\":\"verifiable words\"}],\"text\":\"[Guild] \"}";

    /// <summary>The same component in stable hash form, with every object's keys sorted.</summary>
    private const string DecoratedStable =
        "{\"bold\":true,\"color\":\"#AABBCC\",\"extra\":[{\"text\":\"verifiable words\"}],"
        + "\"insertion\":\"x\",\"italic\":false,\"text\":\"[Guild] \"}";

    /// <summary>A known 1.19.2 signature over <see cref="Content"/>, <see cref="Timestamp"/>, <see cref="Salt"/>, an empty last-seen window, and <see cref="DecoratedWire"/>. The fixed value keeps this test independent from UMPK's signer.</summary>
    private const string VanillaDecoratedBodyDigest =
        "7220757664d9379274a89f0a0db0f58d01cc7f39ac30da5542feaf0ebf0ccb4e";

    /// <summary>The same body with NO decoration, which is an incorrect reconstruction for the frame above. Different digest, hence a failed signature and a permanently broken peer chain.</summary>
    private const string VanillaUndecoratedBodyDigest =
        "5a9e51230726da070883c5cb383eb0e3676092bd497307c0281a5dd564ce5b05";

    /// <summary>The 16-byte big-endian uuid encoding vanilla's signed payloads use.</summary>
    private static byte[] BigEndian(Guid value)
    {
        var bytes = new byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }

    [Fact]
    public async Task SignedChat_FromARosterPeerWithAKey_IsVerified()
    {
        using RSA peerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(ModernProtocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        await AnnouncePeerAsync(harness, peerKey, DateTimeOffset.UtcNow.AddHours(1));
        await harness.ApplyAsync(SignedChat(peerKey, PeerSession));

        Assert.NotNull(seen);

        // 1.19+ sends the bare body; the client composes the displayed line from the chat type's decoration and the sender name, so the rendered form names the sender and Body keeps the undecorated text the signature covers.
        Assert.Equal($"<Notch> {Content}", seen!.Message.ToPlainText());
        Assert.Equal(Content, seen.Body.ToPlainText());
        Assert.Equal(ChatVerification.Verified, seen.Verification);
    }

    /// <summary>End to end through the REAL applier wiring (<c>ApplierContext.SignatureCache</c> -> <c>ChatApplier</c> -> <c>SignedChatVerification.Verify</c>), not just the bridge function called directly. A peer's second message acks its first as a cache-id reference, exactly the shape a real 1.21.8 server sends once its own per-connection cache already holds a signature it just broadcast, and both must verify in the live wiring, not merely in an isolated unit test.</summary>
    [Fact]
    public async Task SignedChat_SecondMessageAcksTheFirstAsACacheIdReference_VerifiesThroughTheLiveApplierWiring()
    {
        using RSA peerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(ModernProtocol));
        var seen = new List<ChatMessageReceived>();
        harness.Events.Subscribe<ChatMessageReceived>(e => seen.Add(e));

        await AnnouncePeerAsync(harness, peerKey, DateTimeOffset.UtcNow.AddHours(1));

        var signer = new ChatSigningSession(Peer, PeerSession);
        PlayerCertificates certs = Certificates(peerKey);

        byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "packetsizeprobe-1", Timestamp, Salt, []);
        await harness.ApplyAsync(new ClientboundPlayerChatPacket(
            Peer, Index: 0, Signature: sig1, SignedContent: "packetsizeprobe-1",
            TimestampMillis: Timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null));

        DateTimeOffset ts2 = Timestamp.AddSeconds(1);
        var window2 = new List<AcknowledgedMessage> { new(Guid.Empty, sig1) };
        byte[] sig2 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "packetsizeprobe-2", ts2, Salt, window2);
        await harness.ApplyAsync(new ClientboundPlayerChatPacket(
            Peer, Index: 1, Signature: sig2, SignedContent: "packetsizeprobe-2",
            TimestampMillis: ts2.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null)
        {
            // The applier chain's own SignatureCache (harness.SignatureCache) must already hold sig1 at slot 0 from message #1's push, purely by having gone through the real wiring above -- this is what a hand-built ChatSigningSession call could never prove.
            LastSeen = [PackedMessageSignature.Cached(0)],
        });

        Assert.Equal(2, seen.Count);
        Assert.Equal(ChatVerification.Verified, seen[0].Verification);
        Assert.Equal(ChatVerification.Verified, seen[1].Verification);
    }

    /// <summary>A signed chat message from an unresolvable sender still advances the shared cache, so later acknowledgements from resolvable peers refer to the expected signature.</summary>
    [Fact]
    public async Task SignedChat_FromAnUnresolvableSender_StillAdvancesTheSharedCacheForALaterResolvablePeer()
    {
        using RSA peerKey = RSA.Create(2048);
        using RSA strangerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(ModernProtocol));
        var seen = new List<ChatMessageReceived>();
        harness.Events.Subscribe<ChatMessageReceived>(e => seen.Add(e));

        // The peer is announced (resolvable); the stranger never is (off-roster, unresolvable).
        await AnnouncePeerAsync(harness, peerKey, DateTimeOffset.UtcNow.AddHours(1));

        Guid stranger = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var strangerSigner = new ChatSigningSession(stranger, Guid.NewGuid());
        byte[] strangerSig = strangerSigner.Sign(
            Certificates(strangerKey), ChatSignatureEra.V1_19_3, "from a stranger", Timestamp, Salt, []);
        var strangerPacket = new ClientboundPlayerChatPacket(
            stranger, Index: 0, Signature: strangerSig, SignedContent: "from a stranger",
            TimestampMillis: Timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Stranger"), TargetName: null);

        await harness.ApplyAsync(strangerPacket);

        Assert.Single(seen);
        Assert.Equal(ChatVerification.Unverified, seen[0].Verification);

        // Load-bearing: the push must have happened even though no verifier exists for `stranger`.
        Assert.Equal(strangerSig, harness.SignatureCache.Unpack(0));

        // The resolvable peer's own first message acks the stranger's message by the cache-id the server would have assigned it (slot 0, since it was the only thing pushed so far).
        DateTimeOffset ts2 = Timestamp.AddSeconds(1);
        var signer = new ChatSigningSession(Peer, PeerSession);
        var window = new List<AcknowledgedMessage> { new(stranger, strangerSig) };
        byte[] sig = signer.Sign(Certificates(peerKey), ChatSignatureEra.V1_19_3, "from the peer", ts2, Salt, window);
        var packet = new ClientboundPlayerChatPacket(
            Peer, Index: 0, Signature: sig, SignedContent: "from the peer",
            TimestampMillis: ts2.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Cached(0)],
        };

        await harness.ApplyAsync(packet);

        Assert.Equal(2, seen.Count);
        Assert.Equal(ChatVerification.Verified, seen[1].Verification);
    }

    /// <summary>A message whose signature does not match the announced key is labelled <see cref="ChatVerification.Failed"/> and still DELIVERED. Verification is advisory by design: one hypothesis that was investigated and refuted was that a null verifier was dropping messages, and verification failure must not drop the delivered message.</summary>
    [Fact]
    public async Task TamperedSignature_IsLabelledFailed_ButStillDelivered()
    {
        using RSA peerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(ModernProtocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        await AnnouncePeerAsync(harness, peerKey, DateTimeOffset.UtcNow.AddHours(1));

        ClientboundPlayerChatPacket good = SignedChat(peerKey, PeerSession);
        byte[] tampered = [.. good.Signature!];
        tampered[0] ^= 0xFF;
        await harness.ApplyAsync(good with { Signature = tampered });

        Assert.NotNull(seen);
        Assert.Equal($"<Notch> {Content}", seen!.Message.ToPlainText());
        Assert.Equal(Content, seen.Body.ToPlainText());
        Assert.Equal(ChatVerification.Failed, seen.Verification);

        // Negative control: a genuine cryptographic rejection is NOT a cache-desync signal. Without this the SignatureCacheDesynced flag would be meaningless noise rather than something a consumer can actually use to distinguish the two situations.
        Assert.False(seen.SignatureCacheDesynced);
        Assert.False(harness.SignatureCache.IsDesynced);
    }

    /// <summary>A consumer seeing <see cref="ChatVerification.Failed"/> must be able to tell "this peer's signature is genuinely bad" (the test above) apart from "our cache is dead" (this one). Also demonstrates the total-blackout consequence at the event level: the SECOND message, even carrying a well-formed cache-id reference for real, still comes back Failed with the same flag, because the FIRST message already poisoned the cache.</summary>
    [Fact]
    public async Task SignedChat_UnresolvableLastSeenEntry_MarksTheReceivedEventSignatureCacheDesynced()
    {
        using RSA peerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(ModernProtocol));
        var seen = new List<ChatMessageReceived>();
        harness.Events.Subscribe<ChatMessageReceived>(e => seen.Add(e));

        await AnnouncePeerAsync(harness, peerKey, DateTimeOffset.UtcNow.AddHours(1));

        var signer = new ChatSigningSession(Peer, PeerSession);
        PlayerCertificates certs = Certificates(peerKey);

        byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Timestamp, Salt, []);
        await harness.ApplyAsync(new ClientboundPlayerChatPacket(
            Peer, Index: 0, Signature: sig1, SignedContent: "one",
            TimestampMillis: Timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null));

        Assert.Equal(ChatVerification.Verified, seen[0].Verification);
        Assert.False(seen[0].SignatureCacheDesynced);

        // References a cache id this session's cache never held anything at.
        DateTimeOffset ts2 = Timestamp.AddSeconds(1);
        var window2 = new List<AcknowledgedMessage> { new(Guid.Empty, sig1) };
        byte[] sig2 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "two", ts2, Salt, window2);
        await harness.ApplyAsync(new ClientboundPlayerChatPacket(
            Peer, Index: 1, Signature: sig2, SignedContent: "two",
            TimestampMillis: ts2.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null)
        {
            LastSeen = [PackedMessageSignature.Cached(5)],
        });

        Assert.Equal(2, seen.Count);
        Assert.Equal(ChatVerification.Failed, seen[1].Verification);
        Assert.True(seen[1].SignatureCacheDesynced);
        Assert.True(harness.SignatureCache.IsDesynced);
        Assert.Equal("unresolvable last-seen entry", harness.SignatureCache.DesyncReason);
    }

    /// <summary>The negative controls that separate "no key" from "wrong key". A peer on the roster with no chat session, and a peer whose announced key has already expired, both stay <see cref="ChatVerification.Unverified"/> rather than being reported as a signature failure, which is a materially different thing to tell a consumer.</summary>
    [Fact]
    public async Task PeerWithNoSessionOrAnExpiredKey_StaysUnverified()
    {
        using RSA peerKey = RSA.Create(2048);

        var noSession = new ApplierHarness(Version(ModernProtocol));
        ChatMessageReceived? withoutSession = null;
        noSession.Events.Subscribe<ChatMessageReceived>(e => withoutSession = e);
        await noSession.ApplyAsync(BoundDescriptorCodec.RoundTrip(
            ModernProtocol, "player_info_update",
            new ClientboundPlayerInfoUpdatePacket(
                PlayerInfoActions.AddPlayer, [Entry(hasSession: false, key: null, expiresAt: default)])));
        await noSession.ApplyAsync(SignedChat(peerKey, PeerSession));
        Assert.Equal(ChatVerification.Unverified, withoutSession!.Verification);

        var expired = new ApplierHarness(Version(ModernProtocol));
        ChatMessageReceived? withExpiredKey = null;
        expired.Events.Subscribe<ChatMessageReceived>(e => withExpiredKey = e);
        await AnnouncePeerAsync(expired, peerKey, DateTimeOffset.UtcNow.AddHours(-1));
        await expired.ApplyAsync(SignedChat(peerKey, PeerSession));
        Assert.Equal(ChatVerification.Unverified, withExpiredKey!.Verification);
    }

    /// <summary>On 759/760, the legacy <c>player_info</c> add-player action carries an optional profile public key without a chat session id, so the v2 payload uses the empty uuid.</summary>
    [Fact]
    public async Task LegacyRoster_ProfileKey_FeedsTheVerifierOn760()
    {
        using RSA peerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(LegacyChatKeyProtocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);

        var announced = new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [
                new LegacyPlayerListEntry(Peer, "Notch", [], GameMode: 0, Latency: 12, DisplayName: null)
                {
                    ProfileKey = new ProfilePublicKeyData(
                        DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                        peerKey.ExportSubjectPublicKeyInfo(),
                        MojangSignature()),
                },
            ]);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(LegacyChatKeyProtocol, "player_info", announced));

        // The decoded key must reach the roster.
        Assert.True(harness.State.TabList.TryGet(Peer, out TabListEntry? entry));
        Assert.IsType<ProfilePublicKeyData>(entry!.ChatSession);

        // And a v2-signed message from that peer verifies against it. v2 signs the two-stage header-over-body-digest payload with no session id, so the verifier is built with Guid.Empty.
        var signer = new ChatSigningSession(Peer, Guid.Empty);
        byte[] signature = signer.Sign(
            Certificates(peerKey), ChatSignatureEra.V1_19_1, Content, Timestamp, Salt, []);

        await harness.ApplyAsync(new ClientboundPlayerChatPacket(
            Peer, Index: 0, Signature: signature, SignedContent: Content,
            TimestampMillis: Timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null));

        Assert.NotNull(seen);
        Assert.Equal(ChatVerification.Verified, seen!.Verification);
    }

    /// <summary>A DECORATED v2 message from a server that formats chat verifies end to end, through the bound 760 codec.</summary>
    /// <remarks>
    /// <para>The signed body includes stable JSON for decorated content between the 0x46 separator and the last-seen entries. Dropping that component changes the digest. Because a signature failure BREAKS the chain, one formatted message can make every later message from that peer unverifiable.</para>
    /// <para>The frame goes through the bound protocol-760 codec, so the raw decorated JSON must survive encode and decode: re-serialising a parsed component model instead would produce a different JsonElement tree and a different digest, proving the decorated form participates in verification.</para>
    /// <para><b>The signature here is NOT produced by UMPK's own payload builder.</b> Signing with the builder and then verifying with the builder is structurally blind: both sides would agree through the same wrong rule. Instead the signed payload is assembled from the fixed <see cref="VanillaDecoratedBodyDigest"/> fixture, so UMPK must reconstruct the expected body.</para>
    /// </remarks>
    [Fact]
    public async Task DecoratedV2Chat_FromAServerThatFormatsChat_IsVerified()
    {
        using RSA peerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(LegacyChatKeyProtocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);
        await AnnounceLegacyPeerAsync(harness, peerKey);

        // The signed payload is vanilla's, not ours: no preceding signature on the first message of a chain, then the 16-byte sender uuid, then vanilla's own body digest.
        byte[] payload = [.. BigEndian(Peer), .. Convert.FromHexString(VanillaDecoratedBodyDigest)];
        Assert.Equal(48, payload.Length);
        byte[] signature = peerKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var packet = new ClientboundPlayerChatPacket(
            Peer, Index: 0, Signature: signature, SignedContent: Content,
            TimestampMillis: Timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null)
        {
            SignedComponentJson = DecoratedWire,
        };

        var decoded = (ClientboundPlayerChatPacket)BoundDescriptorCodec.RoundTrip(
            LegacyChatKeyProtocol, "player_chat", packet);

        // The raw JSON came back byte for byte, and it is NOT the canonical form vanilla hashes: the wire order is the serializer's insertion order, the hashed order is sorted. A build that re-serialised the parsed component, or hashed this text as-is, reconstructs a different body.
        Assert.Equal(DecoratedWire, decoded.SignedComponentJson);
        Assert.NotEqual(DecoratedWire, DecoratedStable);

        await harness.ApplyAsync(decoded);

        Assert.NotNull(seen);
        Assert.Equal(ChatVerification.Verified, seen!.Verification);
    }

    /// <summary>The discriminator for the test above. A frame that CARRIES a decoration but whose signature was produced over the undecorated body is an incorrect reconstruction, and it must be reported Failed. Without this, the test above would still pass on a build that ignored the decoration on both sides.</summary>
    [Fact]
    public async Task V2Chat_SignedWithoutTheDecorationItCarries_IsFailed()
    {
        using RSA peerKey = RSA.Create(2048);
        var harness = new ApplierHarness(Version(LegacyChatKeyProtocol));
        ChatMessageReceived? seen = null;
        harness.Events.Subscribe<ChatMessageReceived>(e => seen = e);
        await AnnounceLegacyPeerAsync(harness, peerKey);

        // Vanilla's digest for the UNDECORATED body, signed and then attached to a frame that DOES carry a decoration. Both digests are vanilla's, so this test cannot pass by agreeing with UMPK's own builder either: it fails the moment the client stops folding the decoration back in.
        byte[] payload = [.. BigEndian(Peer), .. Convert.FromHexString(VanillaUndecoratedBodyDigest)];
        byte[] undecoratedSignature = peerKey.SignData(
            payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var packet = new ClientboundPlayerChatPacket(
            Peer, Index: 0, Signature: undecoratedSignature, SignedContent: Content,
            TimestampMillis: Timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null)
        {
            SignedComponentJson = DecoratedWire,
        };

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(LegacyChatKeyProtocol, "player_chat", packet));

        Assert.NotNull(seen);
        Assert.Equal(ChatVerification.Failed, seen!.Verification);
    }

    /// <summary><c>delete_chat</c>'s <c>Id</c> follows the exact same packed convention as a last-seen entry and identifies the deleted message by the RESOLVED signature, never by the raw wire id. A cache-id reference must resolve through the shared cache rather than surfacing a bare, meaningless index.</summary>
    [Fact]
    public async Task DeleteChat_CacheIdReference_ResolvesThroughTheSharedSignatureCache()
    {
        var harness = new ApplierHarness(Version(ModernProtocol));
        ChatMessageDeleted? seen = null;
        harness.Events.Subscribe<ChatMessageDeleted>(e => seen = e);

        byte[] sig = new byte[256];
        Array.Fill(sig, (byte)0x77);
        harness.SignatureCache.Push([], sig); // lands at slot 0

        await harness.ApplyAsync(new ClientboundDeleteChatPacket(Id: 0, FullSignature: null));

        Assert.NotNull(seen);
        Assert.Equal(0, seen!.MessageId);
        Assert.Equal(sig, seen.ResolvedSignature);
    }

    /// <summary>The full-signature arm needs no cache lookup at all: the bytes rode the wire inline.</summary>
    [Fact]
    public async Task DeleteChat_InlineFullSignature_ResolvesWithoutTheCache()
    {
        var harness = new ApplierHarness(Version(ModernProtocol));
        ChatMessageDeleted? seen = null;
        harness.Events.Subscribe<ChatMessageDeleted>(e => seen = e);

        byte[] sig = new byte[256];
        Array.Fill(sig, (byte)0x99);

        await harness.ApplyAsync(new ClientboundDeleteChatPacket(Id: -1, FullSignature: sig));

        Assert.NotNull(seen);
        Assert.Equal(-1, seen!.MessageId);
        Assert.Equal(sig, seen.ResolvedSignature);
    }

    /// <summary>An id this session's cache never held resolves to null, not a wrong or a crashing lookup.</summary>
    [Fact]
    public async Task DeleteChat_UnresolvableCacheId_ResolvesToNull()
    {
        var harness = new ApplierHarness(Version(ModernProtocol));
        ChatMessageDeleted? seen = null;
        harness.Events.Subscribe<ChatMessageDeleted>(e => seen = e);

        await harness.ApplyAsync(new ClientboundDeleteChatPacket(Id: 42, FullSignature: null));

        Assert.NotNull(seen);
        Assert.Equal(42, seen!.MessageId);
        Assert.Null(seen.ResolvedSignature);
    }

    /// <summary>Made consistent with the last-seen path rather than left as an asymmetry: an unresolvable <c>delete_chat</c> id is exactly as strong a desync signal as an unresolvable last-seen id --both draw from the SAME shared per-connection cache, and a compliant server only ever packs a cache-id reference for a signature it has already sent down this exact connection -- so it now poisons the cache too, naming the cause distinctly from the last-seen and chat-stream-gap triggers.</summary>
    [Fact]
    public async Task DeleteChat_UnresolvableCacheId_MarksTheSharedSignatureCacheDesynced()
    {
        var harness = new ApplierHarness(Version(ModernProtocol));
        Assert.False(harness.SignatureCache.IsDesynced);

        await harness.ApplyAsync(new ClientboundDeleteChatPacket(Id: 42, FullSignature: null));

        Assert.True(harness.SignatureCache.IsDesynced);
        Assert.Equal("unresolvable delete_chat reference", harness.SignatureCache.DesyncReason);
    }

    private static async Task AnnounceLegacyPeerAsync(ApplierHarness harness, RSA peerKey)
    {
        var announced = new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [
                new LegacyPlayerListEntry(Peer, "Notch", [], GameMode: 0, Latency: 12, DisplayName: null)
                {
                    ProfileKey = new ProfilePublicKeyData(
                        DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                        peerKey.ExportSubjectPublicKeyInfo(),
                        MojangSignature()),
                },
            ]);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(LegacyChatKeyProtocol, "player_info", announced));
    }

    /// <summary>The resolver factory itself: present on every signing-era version, absent below 1.19 where no peer can announce a key. A resolver that existed on 47-758 would only ever return null, and its absence is what tells a reader the era has no signing rather than that the wiring was forgotten.</summary>
    [Theory]
    [InlineData(47, false)]
    [InlineData(340, false)]
    [InlineData(758, false)]
    [InlineData(759, true)]
    [InlineData(760, true)]
    [InlineData(765, true)]
    [InlineData(776, true)]
    public void ResolverFactory_TracksTheVersionSigningWireLayout(int protocol, bool expected)
    {
        JavaVersion version = Version(protocol);
        Func<Guid, SignedChatVerifier?>? resolver = Umpk.Client.Internal.PeerChatVerifiers.CreateResolver(
            new ClientState(new ClientFeatures().Normalized()),
            version.Features.ChatSigning,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Equal(expected, resolver is not null);
    }

    private static async Task AnnouncePeerAsync(ApplierHarness harness, RSA peerKey, DateTimeOffset expiresAt)
    {
        var packet = new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer | PlayerInfoActions.InitializeChat,
            [Entry(hasSession: true, peerKey, expiresAt)]);

        // Through the bound codec, so the chat session has to survive the wire, not just the record.
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(ModernProtocol, "player_info_update", packet));
    }

    private static PlayerInfoEntry Entry(bool hasSession, RSA? key, DateTimeOffset expiresAt) =>
        new(Peer, "Notch", [], hasSession,
            hasSession
                ? new RemoteChatSession(
                    PeerSession, expiresAt.ToUnixTimeMilliseconds(), key!.ExportSubjectPublicKeyInfo(), MojangSignature())
                : null,
            GameMode.Survival, Listed: true, Latency: 12, DisplayName: null, ListOrder: 0, ShowHat: true);

    private static ClientboundPlayerChatPacket SignedChat(RSA peerKey, Guid sessionId)
    {
        var signer = new ChatSigningSession(Peer, sessionId);
        byte[] signature = signer.Sign(
            Certificates(peerKey), ChatSignatureEra.V1_19_3, Content, Timestamp, Salt, []);

        return new ClientboundPlayerChatPacket(
            Peer, Index: 0, Signature: signature, SignedContent: Content,
            TimestampMillis: Timestamp.ToUnixTimeMilliseconds(), Salt: Salt,
            UnsignedContent: null, ChatTypeId: 0, SenderName: Component.Text("Notch"), TargetName: null);
    }

    private static PlayerCertificates Certificates(RSA key) =>
        new(key.ExportSubjectPublicKeyInfoPem(), key.ExportPkcs8PrivateKeyPem(), "", "",
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

    /// <summary>A stand-in for Mojang's signature over the key. UMPK cannot verify it (it ships no Yggdrasil session public key, and the newest copy available to this repository predates the signing era by years), but it does reject an ABSENT one, so the bytes have to be present and non-empty.</summary>
    private static byte[] MojangSignature() => [.. Enumerable.Repeat((byte)0x11, 512)];

    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version), $"unknown protocol {protocol}");
        return version!;
    }
}
