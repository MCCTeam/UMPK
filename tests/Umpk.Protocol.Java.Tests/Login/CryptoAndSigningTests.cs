using System.Security.Cryptography;
using Umpk.Protocol.Java.Crypto;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Tests for the online-mode crypto helper (server-id hash) and the chat-signing session skeleton. The server-id vectors are canonical protocol values. The signing tests use generated key pairs for sign/verify round trips.</summary>
public class CryptoAndSigningTests
{
    [Theory]
    // Canonical Minecraft server-id hash vectors (Notch/jeb_/simon and the negative case).
    [InlineData("Notch", "4ed1f46bbe04bc756bcb17c0c7ce3e4632f06a48")]
    [InlineData("jeb_", "-7c9d5b0044c130109a5d7b5fb5c317c02b4e28c1")]
    [InlineData("simon", "88e16a1019277b15d58faf0541e11910eb756f6")]
    public void ServerIdHash_MatchesCanonicalVectors(string input, string expected)
    {
        // These vectors are the SHA-1 of the input string alone (empty secret + empty key), the exact documented test cases for the two's-complement hex rendering.
        string hash = MinecraftServerId.Compute(input, [], []);
        Assert.Equal(expected, hash);
    }

    [Fact]
    public void ServerIdHash_ConcatenatesServerIdSecretAndKey()
    {
        // Ordering matters: id || secret || key. A different secret must change the hash.
        string a = MinecraftServerId.Compute(string.Empty, [1, 2, 3], [4, 5, 6]);
        string b = MinecraftServerId.Compute(string.Empty, [1, 2, 4], [4, 5, 6]);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SigningSession_SignThenVerify_RoundTrips_AllWireLayouts()
    {
        using RSA rsa = RSA.Create(2048);
        var certs = new PlayerCertificates(
            PublicKeyPem: rsa.ExportSubjectPublicKeyInfoPem(),
            PrivateKeyPem: rsa.ExportPkcs8PrivateKeyPem(),
            PublicKeySignature: "", PublicKeySignatureV2: "",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1), RefreshedAfter: DateTimeOffset.UtcNow);

        Guid sender = Guid.NewGuid();
        Guid session = Guid.NewGuid();
        DateTimeOffset ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var lastSeen = new List<AcknowledgedMessage> { new(Guid.NewGuid(), new byte[256]) };

        foreach (ChatSignatureEra era in Enum.GetValues<ChatSignatureEra>())
        {
            var signer = new ChatSigningSession(sender, session);
            byte[] signature = signer.Sign(certs, era, "hello world", ts, salt: 12345L, lastSeen);

            var ctx = new ChatVerificationContext(sender, session, MessageIndex: 0, "hello world", ts, 12345L, lastSeen);
            Assert.True(ChatSigningSession.Verify(certs.PublicKeyPem, signature, era, ctx),
                $"self-signed message must verify for era {era}");

            // A tampered message must not verify.
            var tampered = ctx with { Message = "goodbye" };
            Assert.False(ChatSigningSession.Verify(certs.PublicKeyPem, signature, era, tampered));
        }
    }

    [Fact]
    public void SigningSession_AdvancesIndexAndChainsSignature()
    {
        using RSA rsa = RSA.Create(2048);
        var certs = new PlayerCertificates(
            rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem(), "", "",
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

        var session = new ChatSigningSession(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(0, session.MessageIndex);
        Assert.Null(session.PrecedingSignature);

        session.Sign(certs, ChatSignatureEra.V1_19_3, "one", DateTimeOffset.UtcNow, 1L, []);
        Assert.Equal(1, session.MessageIndex);
        Assert.NotNull(session.PrecedingSignature);

        session.Sign(certs, ChatSignatureEra.V1_19_3, "two", DateTimeOffset.UtcNow, 2L, []);
        Assert.Equal(2, session.MessageIndex);
    }

    [Fact]
    public void ProfileKeyChallenge_SignThenVerify_RoundTrips()
    {
        // The 1.19/1.19.1 encryption response signs the server's verify token (raw, 4 bytes like vanilla's real one) plus a salt with the profile PRIVATE key; the server verifies against the profile PUBLIC key using the exact same nonce-then-big-endian-salt payload. This proves the signature ProfileKeyChallenge.Sign produces actually verifies - independent of the wire shape, which the pinned-frame tests cover separately.
        using RSA rsa = RSA.Create(2048);
        var certs = new PlayerCertificates(
            PublicKeyPem: rsa.ExportSubjectPublicKeyInfoPem(),
            PrivateKeyPem: rsa.ExportPkcs8PrivateKeyPem(),
            PublicKeySignature: "", PublicKeySignatureV2: "",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1), RefreshedAfter: DateTimeOffset.UtcNow);

        byte[] verifyToken = [0xDE, 0xAD, 0xBE, 0xEF];
        const long Salt = 0x0102030405060708L;

        byte[] signature = ProfileKeyChallenge.Sign(certs, verifyToken, Salt);
        Assert.True(ProfileKeyChallenge.Verify(certs.PublicKeyPem, verifyToken, Salt, signature));

        // A different salt, a different token, or a tampered signature must all fail: this is exactly what a real server checks the moment it does not match its own nonce/salt.
        Assert.False(ProfileKeyChallenge.Verify(certs.PublicKeyPem, verifyToken, Salt + 1, signature));
        Assert.False(ProfileKeyChallenge.Verify(certs.PublicKeyPem, [0x00, 0x00, 0x00, 0x00], Salt, signature));
        byte[] tampered = [.. signature];
        tampered[0] ^= 0xFF;
        Assert.False(ProfileKeyChallenge.Verify(certs.PublicKeyPem, verifyToken, Salt, tampered));
    }

    /// <summary>The literal bytes of the signed challenge payload, which no round trip can see. The server hashes the nonce followed by the salt encoded as an 8-byte big-endian value. So the payload is the RAW verify-token bytes followed by the salt's big-endian encoding, and nothing else - no length prefixes, no separator, no re-ordering.</summary>
    /// <remarks>This pin exists because <see cref="ProfileKeyChallenge_SignThenVerify_RoundTrips"/> cannot fail on a wrong byte order: it signs with <c>Sign</c> and verifies with <c>Verify</c>, and BOTH build the payload with the same private helper, so the two agree with each other no matter what they agree on. Flipping the salt to little-endian leaves a paired sign-and-verify round trip green while producing a signature that the server rejects. The expected bytes are literal vectors written by hand, so they cannot track a change in the product code. The two vectors below are chosen so that byte order is observable - each salt is asymmetric under reversal, and the second has its high bit set so a sign-extension or unsigned mix-up cannot hide either.</remarks>
    [Fact]
    public void ProfileKeyChallenge_SignedPayload_PinsVanillaLayoutByteForByte()
    {
        Assert.Equal(
            [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08],
            ProfileKeyChallenge.BuildPayload([0xDE, 0xAD, 0xBE, 0xEF], 0x0102030405060708L));

        Assert.Equal(
            [0x00, 0x11, 0x22, 0x33, 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88],
            ProfileKeyChallenge.BuildPayload([0x00, 0x11, 0x22, 0x33], unchecked((long)0xFFEEDDCCBBAA9988UL)));

        // And the signature is over exactly those bytes, not over something else that merely round-trips: signing the pinned payload as opaque data with the same algorithm/padding must reproduce what Sign produces. RSA PKCS#1 v1.5 is deterministic, so this is an exact byte comparison.
        using RSA rsa = RSA.Create(2048);
        var certs = new PlayerCertificates(
            PublicKeyPem: rsa.ExportSubjectPublicKeyInfoPem(),
            PrivateKeyPem: rsa.ExportPkcs8PrivateKeyPem(),
            PublicKeySignature: "", PublicKeySignatureV2: "",
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(1), RefreshedAfter: DateTimeOffset.UtcNow);

        byte[] pinned = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        Assert.Equal(
            rsa.SignData(pinned, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            ProfileKeyChallenge.Sign(certs, [0xDE, 0xAD, 0xBE, 0xEF], 0x0102030405060708L));
    }

    [Fact]
    public void LastSeenCollector_EvictsAndDeDupesBySender()
    {
        var collector = new LastSeenMessagesCollector(LastSeenMessagesCollector.Window1_19);
        Guid a = Guid.NewGuid();
        collector.Add(new AcknowledgedMessage(a, [1]));
        collector.Add(new AcknowledgedMessage(Guid.NewGuid(), [2]));
        collector.Add(new AcknowledgedMessage(a, [3])); // same sender -> replaces older with null-hole

        IReadOnlyList<AcknowledgedMessage> snapshot = collector.Snapshot();
        Assert.Contains(snapshot, m => m.ProfileId == a && m.Signature[0] == 3);
        // Only one live entry for sender a (the older one was nulled, not compacted).
        Assert.Single(snapshot, m => m.ProfileId == a);
    }
}
