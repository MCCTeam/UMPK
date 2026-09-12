using System.Runtime.CompilerServices;

namespace Umpk.Protocol.Java.Crypto;

/// <summary>Incremental AES-CFB8 stream transform, as Minecraft's protocol encryption uses it. One instance holds one direction's 16-byte shift register (IV) and mutates it as bytes flow, so a connection creates two: one for encrypt and one for decrypt. Span transforms operate directly inside the transport pipeline.</summary>
/// <remarks>In Minecraft's protocol both the read and write ciphers are seeded with the shared secret as the initial IV; the shared secret doubles as the IV.</remarks>
public sealed class AesCfb8
{
    private const int BlockSize = 16;

    private readonly IAesBlockTransform _cipher;

    private readonly byte[] _iv = new byte[BlockSize];

    private AesCfb8(IAesBlockTransform cipher, ReadOnlySpan<byte> iv)
    {
        _cipher = cipher;
        iv[..BlockSize].CopyTo(_iv);
    }

    /// <summary>Creates a transform seeded with <paramref name="keyAndIv"/> as both the AES key and the initial IV, selecting the hardware path when available. This is the shape Minecraft uses.</summary>
    public static AesCfb8 Create(ReadOnlySpan<byte> keyAndIv) =>
        Create(keyAndIv, keyAndIv, forceSoftware: false);

    /// <summary>Creates a transform with an explicit key and IV. <paramref name="forceSoftware"/> selects the portable table-driven AES path even when intrinsics are available (test cross-check).</summary>
    public static AesCfb8 Create(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, bool forceSoftware)
    {
        if (key.Length != BlockSize)
            throw new ArgumentException("AES-128 requires a 16-byte key.", nameof(key));

        if (iv.Length < BlockSize)
            throw new ArgumentException("CFB8 requires a 16-byte IV.", nameof(iv));

        IAesBlockTransform cipher = !forceSoftware && AesNiTransform.IsSupported
            ? AesNiTransform.Create(key)
            : SoftwareAes.Create(key);

        return new AesCfb8(cipher, iv);
    }

    /// <summary>True when this instance is backed by a hardware AES path on this machine.</summary>
    public bool IsHardwareAccelerated => _cipher is AesNiTransform;

    /// <summary>Encrypts <paramref name="input"/> into <paramref name="output"/> (may alias in place), advancing the shift register. CFB8 processes one byte at a time.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Encrypt(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length)
            throw new ArgumentException("Output span is shorter than input.", nameof(output));

        Span<byte> keystream = stackalloc byte[BlockSize];
        Span<byte> iv = _iv;
        for (int i = 0; i < input.Length; i++)
        {
            _cipher.EncryptBlock(iv, keystream);
            byte cipherByte = (byte)(input[i] ^ keystream[0]);
            output[i] = cipherByte;

            // Shift IV left one byte, append ciphertext byte.
            iv[1..].CopyTo(iv[..(BlockSize - 1)]);
            iv[BlockSize - 1] = cipherByte;
        }
    }

    /// <summary>Decrypts <paramref name="input"/> into <paramref name="output"/> (may alias in place), advancing the shift register.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Decrypt(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length)
            throw new ArgumentException("Output span is shorter than input.", nameof(output));

        Span<byte> keystream = stackalloc byte[BlockSize];
        Span<byte> iv = _iv;
        for (int i = 0; i < input.Length; i++)
        {
            _cipher.EncryptBlock(iv, keystream);
            byte cipherByte = input[i];
            output[i] = (byte)(cipherByte ^ keystream[0]);

            // Feedback is the ciphertext byte, computed before the plaintext overwrites it in place.
            iv[1..].CopyTo(iv[..(BlockSize - 1)]);
            iv[BlockSize - 1] = cipherByte;
        }
    }
}
