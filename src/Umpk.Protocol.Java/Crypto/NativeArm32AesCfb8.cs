using System.Security.Cryptography;

namespace Umpk.Protocol.Java.Crypto;

/// <summary>Runs a whole CFB8 buffer through the platform AES provider on ARM32, where .NET's AES intrinsics are unavailable. Linux uses OpenSSL's native AES implementation.</summary>
/// <remarks>The caller owns the continuous feedback register. Native cipher resources are scoped to each call, so this backend adds no disposable session resource.</remarks>
internal sealed class NativeArm32AesCfb8
{
    private const int BlockSize = 16;

    private readonly byte[] _key;

    internal NativeArm32AesCfb8(ReadOnlySpan<byte> key) => _key = key.ToArray();

    internal void Encrypt(ReadOnlySpan<byte> input, Span<byte> output, Span<byte> iv)
    {
        if (input.IsEmpty)
            return;

        using Aes aes = Aes.Create();
        aes.SetKey(_key);
        aes.EncryptCfb(input, iv, output, PaddingMode.None, feedbackSizeInBits: 8);
        AdvanceFeedback(output[..input.Length], iv);
    }

    internal void Decrypt(ReadOnlySpan<byte> input, Span<byte> output, Span<byte> iv)
    {
        if (input.IsEmpty)
            return;

        // Preserve ciphertext before an in-place decrypt overwrites the bytes needed for the next call.
        int feedbackLength = Math.Min(BlockSize, input.Length);
        Span<byte> feedback = stackalloc byte[BlockSize];
        input[^feedbackLength..].CopyTo(feedback);

        using Aes aes = Aes.Create();
        aes.SetKey(_key);
        aes.DecryptCfb(input, iv, output, PaddingMode.None, feedbackSizeInBits: 8);
        AdvanceFeedback(feedback[..feedbackLength], iv);
    }

    private static void AdvanceFeedback(ReadOnlySpan<byte> ciphertext, Span<byte> iv)
    {
        if (ciphertext.Length >= BlockSize)
        {
            ciphertext[^BlockSize..].CopyTo(iv);
            return;
        }

        // Short reads retain the preceding ciphertext in the register, including one-byte calls.
        iv[ciphertext.Length..].CopyTo(iv);
        ciphertext.CopyTo(iv[(BlockSize - ciphertext.Length)..]);
    }
}
