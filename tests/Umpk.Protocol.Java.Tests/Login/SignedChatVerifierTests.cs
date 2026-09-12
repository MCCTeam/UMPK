using System.Security.Cryptography;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Tests for the decode-side chain validator (<see cref="SignedChatVerifier"/>). The signer here is UMPK's own era-3 builder, whose byte layout is proven byte-exact against 1.20.4 by <see cref="SignedChatPayloadLayoutTests"/>; verifying against that proven layout is legitimate (we are checking the verifier reconstructs the same bytes and enforces the four vanilla violation outcomes).</summary>
public class SignedChatVerifierTests
{
    private static readonly Guid Sender = Guid.Parse("11112222-3333-4444-5555-666677778888");
    private static readonly Guid Session = Guid.Parse("99990000-aaaa-bbbb-cccc-ddddeeeeffff");
    private static readonly DateTimeOffset Ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
    private const long Salt = 0x0102030405060708L;

    private static (PlayerCertificates certs, RSA rsa) NewCerts(DateTimeOffset? expiry = null)
    {
        RSA rsa = RSA.Create(2048);
        var certs = new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), "", "",
            expiry ?? DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);
        return (certs, rsa);
    }

    [Fact]
    public void Verify_ValidWireLayout3Chain_AcceptsAndAdvances()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig0 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
            ChatVerificationOutcome r0 = verifier.Verify("one", Ts, Salt, [], sig0, null, Ts);
            Assert.True(r0.IsValid);
            Assert.Equal(1, verifier.NextIndex);

            // Second message: index has advanced on both sides, timestamp not before the last.
            DateTimeOffset ts1 = Ts.AddSeconds(1);
            byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "two", ts1, Salt, []);
            ChatVerificationOutcome r1 = verifier.Verify("two", ts1, Salt, [], sig1, null, ts1);
            Assert.True(r1.IsValid);
            Assert.Equal(2, verifier.NextIndex);
            Assert.False(verifier.IsBroken);
        }
    }

    [Fact]
    public void Verify_TamperedMessage_ReturnsUnsignedChatAndDisconnects()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
            ChatVerificationOutcome r = verifier.Verify("tampered", Ts, Salt, [], sig, null, Ts);

            Assert.Equal(ChatVerificationStatus.UnsignedChat, r.Status);
            Assert.True(r.ShouldDisconnect);
            Assert.Equal("multiplayer.disconnect.unsigned_chat", r.ReasonKey);
            Assert.True(verifier.IsBroken);
        }
    }

    [Fact]
    public void Verify_ReplayedIndex_FailsBecausePayloadNoLongerMatches()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig0 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
            Assert.True(verifier.Verify("one", Ts, Salt, [], sig0, null, Ts).IsValid);

            // Replaying the index-0 signature at index 1 must fail: the reconstructed payload now embeds index 1, so the index-0 signature no longer verifies (chain continuity via the signed index).
            ChatVerificationOutcome r = verifier.Verify("one", Ts, Salt, [], sig0, null, Ts);
            Assert.Equal(ChatVerificationStatus.UnsignedChat, r.Status);
            Assert.True(r.ShouldDisconnect);
        }
    }

    [Fact]
    public void Verify_OutOfOrderTimestamp_ReturnsOutOfOrderAndDisconnects()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            DateTimeOffset tsLate = Ts.AddSeconds(10);
            byte[] sig0 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "late", tsLate, Salt, []);
            Assert.True(verifier.Verify("late", tsLate, Salt, [], sig0, null, tsLate).IsValid);

            DateTimeOffset tsEarly = Ts; // before the last accepted timestamp
            byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "early", tsEarly, Salt, []);
            ChatVerificationOutcome r = verifier.Verify("early", tsEarly, Salt, [], sig1, null, tsLate);

            Assert.Equal(ChatVerificationStatus.OutOfOrderChat, r.Status);
            Assert.True(r.ShouldDisconnect);
            Assert.Equal("multiplayer.disconnect.out_of_order_chat", r.ReasonKey);
        }
    }

    [Fact]
    public void Verify_ExpiredKey_ReturnsExpiredProfileKeyWithoutDisconnect()
    {
        // Key already expired relative to "now".
        (PlayerCertificates certs, RSA rsa) = NewCerts(expiry: Ts.AddSeconds(-1));
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
            ChatVerificationOutcome r = verifier.Verify("one", Ts, Salt, [], sig, null, Ts);

            Assert.Equal(ChatVerificationStatus.ExpiredProfileKey, r.Status);
            Assert.False(r.ShouldDisconnect);
            Assert.Equal("chat.disabled.expiredProfileKey", r.ReasonKey);
        }
    }

    [Fact]
    public void Verify_AfterBreak_ReturnsChainBroken()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
            Assert.Equal(ChatVerificationStatus.UnsignedChat, verifier.Verify("bad", Ts, Salt, [], sig, null, Ts).Status);

            // Chain is now broken; subsequent (even valid) messages are refused as chain-broken (no disconnect).
            byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "two", Ts, Salt, []);
            ChatVerificationOutcome r = verifier.Verify("two", Ts, Salt, [], sig1, null, Ts);
            Assert.Equal(ChatVerificationStatus.ChainBroken, r.Status);
            Assert.False(r.ShouldDisconnect);
            Assert.Equal("chat.disabled.chain_broken", r.ReasonKey);
        }
    }

    /// <summary>Era-2 chaining: the second message announces the first message's signature in its signed header, and that ANNOUNCED value is what the peer hashed, so it is what the verifier must rebuild from.</summary>
    [Fact]
    public void Verify_WireLayout2Chain_ChainsTheAnnouncedPrecedingSignature()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_1, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig0 = signer.Sign(certs, ChatSignatureEra.V1_19_1, "one", Ts, Salt, []);
            Assert.True(verifier.Verify("one", Ts, Salt, [], sig0, null, Ts).IsValid);

            DateTimeOffset ts1 = Ts.AddSeconds(1);
            byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_1, "two", ts1, Salt, []);
            ChatVerificationOutcome r = verifier.Verify("two", ts1, Salt, [], sig1, sig0, ts1);
            Assert.True(r.IsValid);
            Assert.False(verifier.IsBroken);
        }
    }

    /// <summary>The era-2 case that made the announced preceding signature load bearing: a peer whose chain STARTED BEFORE this session joined.</summary>
    /// <remarks>
    /// <para>A 1.19.2 server broadcasts the same signed header to every recipient, so the preceding signature belongs to the sender's global chain rather than a recipient-specific chain. A client joining mid-conversation can therefore receive a header that names a signature it never observed.</para>
    /// <para>A valid signature chain accepts it. The validation rule returns true while its own <c>lastSignature</c> is null, and the signature is then checked over the announced header. Rebuilding the header from this side's tracked signature instead produced a 48-byte payload (<c>senderUuid || digest</c>) where the peer had signed 304 bytes (<c>previousSignature || senderUuid || digest</c>), so the message failed and the peer's chain was marked broken forever.</para>
    /// </remarks>
    [Fact]
    public void Verify_WireLayout2_FirstMessageSeenFromAnAlreadyRunningChain_IsAccepted()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            // The peer has been chatting since before we joined: two messages we never received.
            var signer = new ChatSigningSession(Sender, Session);
            signer.Sign(certs, ChatSignatureEra.V1_19_1, "before we joined", Ts, Salt, []);
            byte[] unseen = signer.Sign(certs, ChatSignatureEra.V1_19_1, "also before", Ts, Salt, []);

            // Now we join and receive the next message, which announces `unseen` as its preceding link.
            DateTimeOffset ts1 = Ts.AddSeconds(1);
            byte[] sig = signer.Sign(certs, ChatSignatureEra.V1_19_1, "first we see", ts1, Salt, []);

            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_1, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);
            ChatVerificationOutcome r = verifier.Verify("first we see", ts1, Salt, [], sig, unseen, ts1);

            Assert.Equal(ChatVerificationStatus.Ok, r.Status);
            Assert.False(verifier.IsBroken);

            // And the chain continues from there.
            DateTimeOffset ts2 = ts1.AddSeconds(1);
            byte[] next = signer.Sign(certs, ChatSignatureEra.V1_19_1, "and the next", ts2, Salt, []);
            Assert.True(verifier.Verify("and the next", ts2, Salt, [], next, sig, ts2).IsValid);
        }
    }

    /// <summary>The security half of using the announced value: once this side HAS accepted a message, a later message that names a different preceding signature is a chain break. Without this, taking the announced value on trust would let a hostile server reorder or drop genuine peer messages undetected.</summary>
    [Fact]
    public void Verify_WireLayout2_AnnouncedPrecedingSignatureThatDoesNotMatchWhatWeAccepted_BreaksTheChain()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_1, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig0 = signer.Sign(certs, ChatSignatureEra.V1_19_1, "one", Ts, Salt, []);
            Assert.True(verifier.Verify("one", Ts, Salt, [], sig0, null, Ts).IsValid);

            DateTimeOffset ts1 = Ts.AddSeconds(1);
            byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_1, "two", ts1, Salt, []);

            // The frame claims a preceding link that is not the one we accepted.
            var bogus = new byte[256];
            ChatVerificationOutcome r = verifier.Verify("two", ts1, Salt, [], sig1, bogus, ts1);

            Assert.Equal(ChatVerificationStatus.ChainBroken, r.Status);
            Assert.False(r.ShouldDisconnect);
            Assert.Equal("chat.disabled.chain_broken", r.ReasonKey);
            Assert.True(verifier.IsBroken);
        }
    }

    /// <summary>The era gate: 1.19.3+ carries continuity as the signed message index, not a preceding signature, so a v3 message must verify whatever is passed here. Applying the v2 chain rule to v3 would break every v3 session after its first message.</summary>
    [Fact]
    public void Verify_WireLayout3_IgnoresTheAnnouncedPrecedingSignature()
    {
        (PlayerCertificates certs, RSA rsa) = NewCerts();
        using (rsa)
        {
            var signer = new ChatSigningSession(Sender, Session);
            var verifier = new SignedChatVerifier(
                ChatSignatureEra.V1_19_3, certs.PublicKeyPem, certs.ExpiresAt, Sender, Session);

            byte[] sig0 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "one", Ts, Salt, []);
            Assert.True(verifier.Verify("one", Ts, Salt, [], sig0, null, Ts).IsValid);

            DateTimeOffset ts1 = Ts.AddSeconds(1);
            byte[] sig1 = signer.Sign(certs, ChatSignatureEra.V1_19_3, "two", ts1, Salt, []);
            Assert.True(verifier.Verify("two", ts1, Salt, [], sig1, new byte[256], ts1).IsValid);
            Assert.False(verifier.IsBroken);
        }
    }
}
