using System.Security.Cryptography;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.Authentication;

/// <summary>Names the only server authentication outcome this handshake step can report.</summary>
/// <remarks>The token signature verifies under its declared key, but that alone does not authenticate the current peer or any player identity.</remarks>
internal enum BedrockServerAuthenticationLevel
{
    /// <summary>The handshake token verified under its declared key without establishing server identity.</summary>
    KeyPossession,
}

/// <summary>Holds one derived handshake encryption key with a key-possession authentication level and dispose-time zeroization.</summary>
/// <remarks>The result owns the key bytes until disposal, reports key possession only rather than any player identity, and hands copies to each encryption state so the states stay usable after the result is disposed.</remarks>
internal sealed class BedrockHandshakeResult : IDisposable
{
    private byte[] _encryptionKey;
    private bool _disposed;

    internal BedrockHandshakeResult(byte[] encryptionKey)
    {
        _encryptionKey = encryptionKey ?? throw new ArgumentNullException(nameof(encryptionKey));
    }

    internal BedrockServerAuthenticationLevel AuthenticationLevel => BedrockServerAuthenticationLevel.KeyPossession;

    /// <summary>Gets a guarded view of the derived encryption key owned by this result.</summary>
    internal ReadOnlyMemory<byte> EncryptionKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _encryptionKey;
        }
    }

    /// <summary>Creates the paired send and receive encryption states with independent copies of the derived key.</summary>
    /// <remarks>Each state copies the key bytes at construction so the states stay usable after this result is disposed, while this result keeps owning its own copy until disposal.</remarks>
    internal (BedrockEncryptionState Send, BedrockEncryptionState Receive) CreateEncryptionStates()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (new BedrockEncryptionState(_encryptionKey, _encryptionKey), new BedrockEncryptionState(_encryptionKey, _encryptionKey));
    }

    /// <summary>Clears the owned encryption key bytes and releases the result.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_encryptionKey);
        _encryptionKey = [];
    }
}

/// <summary>Validates server handshake tokens and derives session encryption keys for Bedrock joins.</summary>
internal static class BedrockHandshakeProcessor
{
    /// <summary>Validates one server handshake token and derives a session encryption key without establishing server identity.</summary>
    /// <remarks>The token signature verifies under its declared key, but this step alone does not prove the current peer controls that key; the salt must decode to between 1 and 64 bytes, and transient salt and server-key bytes are cleared before return.</remarks>
    internal static BedrockHandshakeResult Process(string compactToken, BedrockConnectionKey connectionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compactToken);
        ArgumentNullException.ThrowIfNull(connectionKey);
        BedrockHandshakeClaims claims = BedrockCompactJwt.ValidateHandshakeToken(compactToken, out byte[] serverPublicKey);
        byte[]? salt = null;
        try
        {
            salt = Convert.FromBase64String(claims.Salt);
            if (salt.Length is < 1 or > 64)
                throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidHandshake, "The handshake salt must contain between 1 and 64 bytes.");
            byte[] key = connectionKey.DeriveEncryptionKey(serverPublicKey, salt);
            return new BedrockHandshakeResult(key);
        }
        catch (FormatException exception)
        {
            throw new BedrockAuthenticationException(BedrockAuthenticationFailureCode.InvalidHandshake, "The handshake salt is not valid base64.", exception);
        }

        finally
        {
            if (salt is not null)
                CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(serverPublicKey);
        }
    }
}
