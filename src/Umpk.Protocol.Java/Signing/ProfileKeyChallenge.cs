using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Umpk.Protocol.Java.Signing;

/// <summary>Builds and verifies the 1.19/1.19.1 (protocols 759/760) signed-challenge payload the encryption response carries in place of a plain verify token when login-start already presented a profile public key. The signed payload is the server's verify-token bytes followed by the salt's big-endian encoding, signed with SHA256withRSA and verified against the profile public key.</summary>
internal static class ProfileKeyChallenge
{
    /// <summary>Builds the big-endian verify-token followed by salt payload used for signing.</summary>
    /// <remarks>Exposed internally so tests can pin the byte order independently of <see cref="Sign"/> and <see cref="Verify"/>; a sign-then-verify round trip would prove only their mutual consistency.</remarks>
    internal static byte[] BuildPayload(byte[] verifyToken, long salt)
    {
        var payload = new byte[verifyToken.Length + sizeof(long)];
        verifyToken.CopyTo(payload, 0);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(verifyToken.Length), salt);
        return payload;
    }

    /// <summary>Signs the server's verify token and the given salt with the profile private key (<see cref="PlayerCertificates.PrivateKeyPem"/>), producing the signature a 759/760 encryption response carries alongside the salt in place of an RSA-encrypted verify token.</summary>
    public static byte[] Sign(PlayerCertificates certificates, byte[] verifyToken, long salt)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(verifyToken);
        byte[] payload = BuildPayload(verifyToken, salt);

        // Mojang mislabels the PEM armor; see RsaPemKeys for why ImportFromPem cannot be used directly.
        using RSA rsa = RsaPemKeys.ImportPrivateKey(certificates.PrivateKeyPem);
        return rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <summary>Verifies a signed challenge against the profile PUBLIC key, mirroring <c>ServerLoginPacketListenerImpl.isChallengeSignatureValid</c> exactly. Used by tests to prove the signature <see cref="Sign"/> produces actually validates, independent of wire shape.</summary>
    public static bool Verify(string profilePublicKeyPem, byte[] verifyToken, long salt, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(profilePublicKeyPem);
        ArgumentNullException.ThrowIfNull(verifyToken);
        ArgumentNullException.ThrowIfNull(signature);
        byte[] payload = BuildPayload(verifyToken, salt);

        using RSA rsa = RsaPemKeys.ImportPublicKey(profilePublicKeyPem);
        return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
