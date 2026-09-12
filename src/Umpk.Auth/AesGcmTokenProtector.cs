using System.Diagnostics;
using System.Security.Cryptography;

namespace Umpk.Auth;

/// <summary>An <see cref="ITokenProtector"/> that encrypts stored credentials with AES-256-GCM under a key held in a separate, owner-only key file. This is the default on every platform that has no OS data protection API bound in (that is, everything except Windows, which uses <see cref="DpapiTokenProtector"/>).</summary>
/// <remarks>
/// <para><b>Format.</b> <c>"UMPKTK" | version(1) | nonce(12) | ciphertext | tag(16)</c>. The 7-byte header is authenticated as GCM associated data, so a payload cannot be downgraded to a different version by editing its header. A payload that does not start with the magic is returned unchanged by <see cref="UnprotectAsync"/>: stores written before this type existed hold bare JSON, and rejecting them would sign every existing user out on upgrade. They are re-written encrypted by the next <see cref="ProtectAsync"/>.</para>
/// <para><b>Threat model, stated plainly.</b> The key is a random 32 bytes in a file the OS restricts to the owner (<c>0600</c> on Unix), held outside the token directory. That defends the realistic exposures: a token directory copied into a backup, a synced folder, a container image or a git commit is inert without the key file, and the ciphertext is tamper-evident. It does NOT defend against an attacker who can already read the owner's home directory or run code as the owner: such an attacker reads the key file and decrypts. This is weaker than Windows DPAPI, whose key is wrapped by the user's logon credentials and so survives whole-disk theft. Binding to a passphrase would close that gap and cost an interactive prompt on every start, which a headless client cannot pay; a desktop keyring (Secret Service) would close it on desktops only. Neither is what this type is.</para>
/// <para><b>Key loss is not fatal.</b> <see cref="FileTokenStore"/> treats a <see cref="CryptographicException"/> as a cache miss and drops the entry, so a deleted or rotated key file costs one interactive sign-in, not a crash.</para>
/// </remarks>
public sealed class AesGcmTokenProtector : ITokenProtector, IDisposable
{
    private static readonly byte[] Magic = "UMPKTK"u8.ToArray();

    private const byte Version = 1;
    private const int HeaderLength = 7;   // Magic (6) + version (1)
    private const int NonceLength = 12;   // AesGcm.NonceByteSizes fixed size
    private const int TagLength = 16;     // AesGcm.TagByteSizes maximum
    private const int KeyLength = 32;     // AES-256

    private readonly string _keyFilePath;
    private readonly Lock _gate = new();

    private AesGcm? _aes;
    private bool _disposed;

    /// <summary>Creates a protector whose key lives at <paramref name="keyFilePath"/>.</summary>
    /// <remarks>The key file is created on first use, not here, so constructing a protector never writes to disk and a store that is only ever read does not leave a key behind.</remarks>
    public AesGcmTokenProtector(string keyFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyFilePath);
        _keyFilePath = keyFilePath;
    }

    /// <summary>Whether this platform can run AES-GCM at all.</summary>
    public static bool IsSupported => AesGcm.IsSupported;

    /// <summary>The default key-file path for the current user: <c>umpk/token-protection.key</c> under the platform's local application data directory, which is deliberately NOT the token directory, so copying the token directory does not copy the key.</summary>
    public static string DefaultKeyFilePath()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

        // A stripped-down container can report no home at all; fall back to the token store's own conventions rather than writing a key to the filesystem root.
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Path.GetTempPath(), ".umpk-local");

        return Path.Combine(root, "umpk", "token-protection.key");
    }

    /// <inheritdoc />
    public ValueTask<byte[]> ProtectAsync(byte[] plaintext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] result = new byte[HeaderLength + NonceLength + plaintext.Length + TagLength];
        Magic.CopyTo(result.AsSpan());
        result[HeaderLength - 1] = Version;

        Span<byte> nonce = result.AsSpan(HeaderLength, NonceLength);
        RandomNumberGenerator.Fill(nonce);

        Span<byte> ciphertext = result.AsSpan(HeaderLength + NonceLength, plaintext.Length);
        Span<byte> tag = result.AsSpan(HeaderLength + NonceLength + plaintext.Length, TagLength);

        // The cipher is used under the gate, not merely fetched under it: AesGcm is not documented as thread-safe, and one protector instance is shared by every write the store makes.
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Cipher().Encrypt(nonce, plaintext, ciphertext, tag, result.AsSpan(0, HeaderLength));
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc />
    public ValueTask<byte[]> UnprotectAsync(byte[] protectedData, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Written before this protector existed: bare bytes, handed back as they are. See the type remarks on why this is a migration path and not a downgrade hole (an attacker who can rewrite the token file can equally delete it and force a fresh sign-in).
        if (!HasHeader(protectedData))
            return ValueTask.FromResult(protectedData);

        if (protectedData[HeaderLength - 1] != Version)
            throw new CryptographicException(
                $"Unsupported token payload version {protectedData[HeaderLength - 1]}.");

        int bodyLength = protectedData.Length - HeaderLength - NonceLength - TagLength;
        if (bodyLength < 0)
            throw new CryptographicException("Token payload is truncated.");

        ReadOnlySpan<byte> all = protectedData;
        ReadOnlySpan<byte> nonce = all.Slice(HeaderLength, NonceLength);
        ReadOnlySpan<byte> ciphertext = all.Slice(HeaderLength + NonceLength, bodyLength);
        ReadOnlySpan<byte> tag = all.Slice(HeaderLength + NonceLength + bodyLength, TagLength);

        byte[] plaintext = new byte[bodyLength];
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Cipher().Decrypt(nonce, ciphertext, tag, plaintext, all[..HeaderLength]);
        }

        return ValueTask.FromResult(plaintext);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _aes?.Dispose();
            _aes = null;
        }
    }

    private static bool HasHeader(byte[] payload)
        => payload.Length >= HeaderLength && payload.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    /// <summary>The cipher, created from the key file on first use. Callers must already hold <see cref="_gate"/>.</summary>
    private AesGcm Cipher()
    {
        Debug.Assert(_gate.IsHeldByCurrentThread, "Cipher() must be called under _gate.");
        if (_aes is not null)
            return _aes;

        byte[] key = LoadOrCreateKey();
        try
        {
            _aes = new AesGcm(key, TagLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return _aes;
    }

    /// <summary>Reads the key file, or creates it with 32 fresh random bytes. The write goes to a temporary file that is restricted BEFORE the rename, so the key is never briefly world-readable under its final name.</summary>
    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(_keyFilePath))
        {
            byte[] existing = File.ReadAllBytes(_keyFilePath);
            if (existing.Length == KeyLength)
                return existing;

            CryptographicOperations.ZeroMemory(existing);
            throw new CryptographicException(
                $"The token protection key at '{_keyFilePath}' is not {KeyLength} bytes; delete it to have a new one generated.");
        }

        string directory = Path.GetDirectoryName(_keyFilePath)
            ?? throw new CryptographicException($"The token protection key path '{_keyFilePath}' has no directory.");

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            TokenStoragePermissions.RestrictDirectory(directory);
        }

        byte[] key = RandomNumberGenerator.GetBytes(KeyLength);
        string temp = _keyFilePath + ".tmp";
        File.WriteAllBytes(temp, key);
        TokenStoragePermissions.RestrictFile(temp);

        try
        {
            // Another process may have won the race between File.Exists above and here. Its key is just as good as ours, so take theirs rather than clobbering tokens it has already written.
            File.Move(temp, _keyFilePath, overwrite: false);
        }
        catch (IOException)
        {
            TryDelete(temp);
            CryptographicOperations.ZeroMemory(key);
            byte[] winner = File.ReadAllBytes(_keyFilePath);
            if (winner.Length != KeyLength)
            {
                CryptographicOperations.ZeroMemory(winner);
                throw new CryptographicException(
                    $"The token protection key at '{_keyFilePath}' is not {KeyLength} bytes; delete it to have a new one generated.");
            }

            return winner;
        }

        return key;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);

        }
        catch (IOException)
        {
            // Best effort: a stray .tmp is harmless next to a key that is already in place.
        }
    }

}
