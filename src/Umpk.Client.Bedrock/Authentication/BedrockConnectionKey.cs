using System.Security.Cryptography;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.Authentication;

/// <summary>Holds one paired P-384 agreement and signing key with SubjectPublicKeyInfo identity and dispose-time zeroization.</summary>
/// <remarks>Both keys share one P-384 generation so signing and key agreement stay bound to the same connection identity, temporary exported private parameters and derived shared secrets are cleared after use, the caller keeps ownership of peer-key input bytes, and disposal releases both handles while clearing the cached public copy.</remarks>
internal sealed class BedrockConnectionKey : IDisposable
{
    private const string P384Oid = "1.3.132.0.34";
    private readonly ECDiffieHellman _keyAgreement;
    private readonly ECDsa _signingKey;
    private readonly byte[] _publicKey;
    private bool _disposed;

    private BedrockConnectionKey(ECDiffieHellman keyAgreement, ECDsa signingKey)
    {
        _keyAgreement = keyAgreement;
        _signingKey = signingKey;
        _publicKey = keyAgreement.ExportSubjectPublicKeyInfo();
    }

    /// <summary>Gets the cached SubjectPublicKeyInfo identity bytes for this connection key.</summary>
    internal ReadOnlyMemory<byte> PublicKeySubjectPublicKeyInfo
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _publicKey;
        }
    }

    internal string PublicKeyBase64 => Convert.ToBase64String(PublicKeySubjectPublicKeyInfo.Span);

    /// <summary>Creates one fresh paired P-384 connection key with agreement and signing keys bound to the same generation.</summary>
    /// <remarks>The signing key is rebuilt from the freshly generated parameters so both keys share one identity, the temporary exported private parameters are cleared before return, and a signing-key failure disposes the agreement key instead of leaking it.</remarks>
    internal static BedrockConnectionKey Create()
    {
        ECDiffieHellman keyAgreement = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        try
        {
            ECParameters parameters = keyAgreement.ExportParameters(true);
            ECDsa signingKey = ECDsa.Create(parameters);
            ZeroPrivateParameters(ref parameters);
            return new BedrockConnectionKey(keyAgreement, signingKey);
        }
        catch
        {
            keyAgreement.Dispose();
            throw;
        }
    }

    /// <summary>Imports one PKCS#8 private key as a paired P-384 connection key with strict trailing-byte rejection.</summary>
    /// <remarks>The imported key must decode exactly with P-384 coordinates and no trailing bytes, the signing key is rebuilt from the same parameters so the pair stays bound, and the temporary exported private parameters are cleared before return.</remarks>
    internal static BedrockConnectionKey ImportPkcs8(ReadOnlySpan<byte> pkcs8)
    {
        ECDiffieHellman keyAgreement = ECDiffieHellman.Create();
        try
        {
            keyAgreement.ImportPkcs8PrivateKey(pkcs8, out int bytesRead);
            if (bytesRead != pkcs8.Length)
                throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidCurve, "The connection key contains trailing bytes.");

            ValidateP384(keyAgreement.ExportParameters(false));
            ECParameters parameters = keyAgreement.ExportParameters(true);
            ECDsa signingKey = ECDsa.Create(parameters);
            ZeroPrivateParameters(ref parameters);
            return new BedrockConnectionKey(keyAgreement, signingKey);
        }
        catch
        {
            keyAgreement.Dispose();
            throw;
        }
    }

    /// <summary>Signs one payload with the connection signing key using SHA-384 and fixed-field concatenated P-384 signatures.</summary>
    /// <remarks>The signature proves possession of this connection key only and authenticates no player identity.</remarks>
    internal byte[] Sign(ReadOnlySpan<byte> content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _signingKey.SignData(content, HashAlgorithmName.SHA384, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Derives one session encryption key from the peer P-384 public key and a 1 to 64 byte salt through the shared secret.</summary>
    /// <remarks>The peer key must carry exact P-384 bytes with no trailing data, the derived shared secret is cleared once the session key is derived, and the caller retains ownership of the supplied peer-key bytes.</remarks>
    internal byte[] DeriveEncryptionKey(ReadOnlySpan<byte> peerSubjectPublicKeyInfo, ReadOnlySpan<byte> salt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (salt.IsEmpty || salt.Length > 64)
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidHandshake, "The handshake salt must contain between 1 and 64 bytes.");

        using ECDiffieHellman peer = ECDiffieHellman.Create();
        try
        {
            peer.ImportSubjectPublicKeyInfo(peerSubjectPublicKeyInfo, out int bytesRead);
            if (bytesRead != peerSubjectPublicKeyInfo.Length)
                throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidCurve, "The peer key contains trailing bytes.");
            ValidateP384(peer.ExportParameters(false));
            byte[] sharedSecret = _keyAgreement.DeriveRawSecretAgreement(peer.PublicKey);
            try
            {
                return BedrockEncryption.DeriveKey(salt, sharedSecret);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sharedSecret);
            }
        }
        catch (BedrockAuthenticationException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidCurve, "The peer key is not a valid P-384 public key.", exception);
        }
    }

    /// <summary>Imports one P-384 SubjectPublicKeyInfo value as a signature-verification key with strict trailing-byte rejection.</summary>
    /// <remarks>The value must decode exactly with P-384 coordinates and no trailing data, and a failed import disposes the partially built key instead of leaking it.</remarks>
    internal static ECDsa ImportP384PublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
            if (bytesRead != subjectPublicKeyInfo.Length)
                throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidCurve, "The public key contains trailing bytes.");
            ValidateP384(key.ExportParameters(false));
            return key;
        }
        catch (BedrockAuthenticationException)
        {
            key.Dispose();
            throw;
        }
        catch (CryptographicException exception)
        {
            key.Dispose();
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidCurve, "The public key is not a valid P-384 SubjectPublicKeyInfo value.", exception);
        }
    }

    /// <summary>Releases both key handles and clears the cached public key bytes.</summary>
    /// <remarks>Disposal is idempotent and covers only the bytes this type owns, while peer-key input supplied by the caller is never owned or cleared here.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _signingKey.Dispose();
        _keyAgreement.Dispose();
        CryptographicOperations.ZeroMemory(_publicKey);
    }

    private static void ValidateP384(ECParameters parameters)
    {
        if (!string.Equals(parameters.Curve.Oid.Value, P384Oid, StringComparison.Ordinal) || parameters.Q.X is not { Length: 48 } || parameters.Q.Y is not { Length: 48 })
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidCurve, "The key must use the P-384 curve.");
    }

    private static void ZeroPrivateParameters(ref ECParameters parameters)
    {
        if (parameters.D is not null)
            CryptographicOperations.ZeroMemory(parameters.D);
    }
}
