namespace Umpk.Protocol.Java.Crypto;

/// <summary>A single-block AES-128 forward (encrypt) primitive. AES-CFB8 needs only block encryption for both stream directions, so this interface intentionally exposes just that.</summary>
internal interface IAesBlockTransform
{
    /// <summary>Encrypts a single 16-byte block. <paramref name="input"/> and <paramref name="output"/> must each be at least 16 bytes.</summary>
    void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output);
}
