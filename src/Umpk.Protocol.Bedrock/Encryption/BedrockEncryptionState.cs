using System.Security.Cryptography;

namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockEncryptionState : IDisposable
{
    private readonly byte[] _secret;
    private readonly ICryptoTransform _blockCipher;
    private readonly byte[] _counterBlock;
    private readonly byte[] _keystream = new byte[16];
    private int _keystreamOffset = 16;
    private bool _disposed;

    internal BedrockEncryptionState(ReadOnlySpan<byte> key, ReadOnlySpan<byte> secret)
    {
        if (key.Length != 32)
            throw new ArgumentException("Bedrock encryption requires a 256-bit key.", nameof(key));

        if (secret.IsEmpty)
            throw new ArgumentException("The shared secret is required.", nameof(secret));

        _secret = secret.ToArray();
        _counterBlock = BedrockEncryption.CreateInitialCounter(key);
        Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = key.ToArray();
        _blockCipher = new OwnedTransform(aes, aes.CreateEncryptor());
    }

    internal ulong Counter { get; private set; }

    internal bool IsTerminal { get; private set; }

    internal byte[] EncryptBatch(ReadOnlySpan<byte> plaintext)
    {
        ThrowIfUnavailable();
        byte[] checksum = BedrockEncryption.ComputeChecksum(Counter, plaintext, _secret);
        byte[] authenticated = new byte[plaintext.Length + checksum.Length];
        plaintext.CopyTo(authenticated);
        checksum.CopyTo(authenticated.AsSpan(plaintext.Length));
        Transform(authenticated);
        Counter++;
        return authenticated;
    }

    internal byte[] DecryptBatch(ReadOnlySpan<byte> ciphertext)
    {
        ThrowIfUnavailable();
        if (ciphertext.Length < 8)
        {
            IsTerminal = true;
            throw new BedrockCodecException("The encrypted batch is shorter than its checksum.");
        }

        byte[] authenticated = ciphertext.ToArray();
        Transform(authenticated);
        ReadOnlySpan<byte> payload = authenticated.AsSpan(0, authenticated.Length - 8);
        ReadOnlySpan<byte> actualChecksum = authenticated.AsSpan(authenticated.Length - 8);
        byte[] expectedChecksum = BedrockEncryption.ComputeChecksum(Counter, payload, _secret);
        if (!CryptographicOperations.FixedTimeEquals(actualChecksum, expectedChecksum))
        {
            IsTerminal = true;
            CryptographicOperations.ZeroMemory(authenticated);
            throw new BedrockCodecException("The encrypted batch checksum is invalid.");
        }

        Counter++;
        return payload.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _blockCipher.Dispose();
        CryptographicOperations.ZeroMemory(_secret);
        CryptographicOperations.ZeroMemory(_counterBlock);
        CryptographicOperations.ZeroMemory(_keystream);
    }

    private void Transform(Span<byte> data)
    {
        for (int index = 0; index < data.Length; index++)
        {
            if (_keystreamOffset == 16)
            {
                int transformed = _blockCipher.TransformBlock(_counterBlock, 0, 16, _keystream, 0);
                if (transformed != 16)
                    throw new CryptographicException("AES did not produce a complete counter block.");

                IncrementCounter(_counterBlock);
                _keystreamOffset = 0;
            }

            data[index] ^= _keystream[_keystreamOffset++];
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsTerminal)
            throw new BedrockCodecException("The encryption stream is terminal.");
    }

    private static void IncrementCounter(Span<byte> counter)
    {
        for (int index = counter.Length - 1; index >= 0; index--)
        {
            counter[index]++;
            if (counter[index] != 0)
                break;
        }
    }

    private sealed class OwnedTransform(Aes aes, ICryptoTransform transform) : ICryptoTransform
    {
        private readonly Aes _aes = aes;
        private readonly ICryptoTransform _transform = transform;

        public bool CanReuseTransform => _transform.CanReuseTransform;

        public bool CanTransformMultipleBlocks => _transform.CanTransformMultipleBlocks;

        public int InputBlockSize => _transform.InputBlockSize;

        public int OutputBlockSize => _transform.OutputBlockSize;

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset) =>
            _transform.TransformBlock(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset);

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount) =>
            _transform.TransformFinalBlock(inputBuffer, inputOffset, inputCount);

        public void Dispose()
        {
            _transform.Dispose();
            _aes.Dispose();
        }
    }
}
