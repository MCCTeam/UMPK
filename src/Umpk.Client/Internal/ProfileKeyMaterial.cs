using System.Security.Cryptography;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;

namespace Umpk.Client.Internal;

/// <summary>Converts host-supplied <see cref="PlayerCertificates"/> into the on-the-wire <see cref="ProfilePublicKeyData"/> the login-start (1.19/1.19.1) and <c>chat_session_update</c> (1.19.3+) packets carry. The key is decoded from its PEM to the exact DER <c>SubjectPublicKeyInfo</c> bytes Mojang signed (base64-decoding the PEM body rather than round tripping through a key type, so the bytes stay identical to what the Mojang signature covers), and the Mojang signature is the base64-decoded v1 or v2 signature the era requires.</summary>
internal static class ProfileKeyMaterial
{
    /// <summary>Builds the wire key payload. <paramref name="useV2Signature"/> selects Mojang's v2 signature (1.19.1 login start and every <c>chat_session_update</c>) over the v1 signature (1.19.0 login start).</summary>
    public static ProfilePublicKeyData Build(PlayerCertificates certificates, bool useV2Signature)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        byte[] keyDer = DecodePemBody(certificates.PublicKeyPem);
        string signatureBase64 = useV2Signature
            ? certificates.PublicKeySignatureV2
            : certificates.PublicKeySignature;
        byte[] signature = Convert.FromBase64String(signatureBase64);
        return new ProfilePublicKeyData(certificates.ExpiresAt.ToUnixTimeMilliseconds(), keyDer, signature);
    }

    /// <summary>Whether announcing <paramref name="announcement"/> would tell a 1.19.3+ server anything it does not already know, given that <paramref name="current"/> is what it was last told. True means the server will treat the update as a no-op, so the client must treat it as one too.</summary>
    /// <remarks>
    /// <para>Equality covers the three fields visible on the wire: expiry at millisecond resolution, encoded DER public-key bytes, and Mojang signature bytes.</para>
    /// <para>It is deliberately NOT a comparison of <see cref="PlayerCertificates"/>. The PEM text, the private key and <c>refreshedAfter</c> are all invisible to the server, so a difference in any of them must not start a rotation the server will silently refuse to follow. The caller is <c>ChatSigningCoordinator.ReplaceCertificatesAsync</c>, whose remarks carry the disconnect this prevents.</para>
    /// </remarks>
    /// <param name="current">The certificates whose announcement the server already holds.</param>
    /// <param name="announcement">The announcement payload built from the replacement certificates.</param>
    public static bool AnnouncesSameKey(PlayerCertificates current, ProfilePublicKeyData announcement)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(announcement);
        ProfilePublicKeyData held = Build(current, useV2Signature: true);
        return held.ExpiresAtMillis == announcement.ExpiresAtMillis
            && held.KeyDer.AsSpan().SequenceEqual(announcement.KeyDer)
            && held.KeySignature.AsSpan().SequenceEqual(announcement.KeySignature);
    }

    private static byte[] DecodePemBody(string pem)
    {
        ReadOnlySpan<char> span = pem;
        if (PemEncoding.TryFind(span, out PemFields fields))
        {
            // Convert.FromBase64String ignores the embedded line breaks in the PEM base64 body.
            return Convert.FromBase64String(span[fields.Base64Data].ToString());
        }

        // No PEM armor: treat the payload as raw base64 (already the DER body).
        return Convert.FromBase64String(pem.Trim());
    }
}
