using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Umpk.Protocol.Bedrock;

internal static class BedrockEncryption
{
    internal static byte[] DeriveKey(ReadOnlySpan<byte> salt, ReadOnlySpan<byte> sharedSecret)
    {
        byte[] input = new byte[salt.Length + sharedSecret.Length];
        salt.CopyTo(input);
        sharedSecret.CopyTo(input.AsSpan(salt.Length));
        return SHA256.HashData(input);
    }

    internal static byte[] CreateInitialCounter(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Bedrock encryption requires a 256-bit key.", nameof(key));

        byte[] counter = new byte[16];
        key[..12].CopyTo(counter);
        counter[15] = 2;
        return counter;
    }

    internal static byte[] DeriveRawP384Secret(ECDiffieHellman localKey, ReadOnlySpan<byte> peerUncompressedPoint)
    {
        ArgumentNullException.ThrowIfNull(localKey);
        if (peerUncompressedPoint.Length != 97 || peerUncompressedPoint[0] != 0x04)
            throw new BedrockCodecException("The peer P-384 key must be a 97-byte uncompressed point.");

        var peerParameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            Q = new ECPoint
            {
                X = peerUncompressedPoint.Slice(1, 48).ToArray(),
                Y = peerUncompressedPoint.Slice(49, 48).ToArray(),
            },
        };
        using ECDiffieHellman peerKey = ECDiffieHellman.Create(peerParameters);
        return localKey.DeriveRawSecretAgreement(peerKey.PublicKey);
    }

    internal static byte[] ComputeChecksum(ulong counter, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> secret)
    {
        byte[] input = new byte[8 + payload.Length + secret.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(input, counter);
        payload.CopyTo(input.AsSpan(8));
        secret.CopyTo(input.AsSpan(8 + payload.Length));
        return SHA256.HashData(input)[..8];
    }
}
