using System.Security.Cryptography;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Encryption;

public sealed class BedrockEncryptionTests
{
    private static readonly byte[] Secret = Convert.FromHexString("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");

    [Fact]
    public void KeyAndIvDerivationMatchIndependentVectors()
    {
        byte[] salt = Convert.FromHexString("202122232425262728292a2b2c2d2e2f");

        byte[] key = BedrockEncryption.DeriveKey(salt, Secret);
        byte[] iv = BedrockEncryption.CreateInitialCounter(key);

        Assert.Equal("4234682ed6415336edfffa94419419ae8bd0c20e28256070cb5914ca8a526b50", Convert.ToHexString(key).ToLowerInvariant());
        Assert.Equal("4234682ed6415336edfffa9400000002", Convert.ToHexString(iv).ToLowerInvariant());
    }

    [Fact]
    public void ContinuousCipherAndChecksumsAdvancePerBatchAndDirection()
    {
        byte[] key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        using var sender = new BedrockEncryptionState(key, Secret);
        using var receiver = new BedrockEncryptionState(key, Secret);

        byte[] first = sender.EncryptBatch(Convert.FromHexString("ff0102030405060708090a0b0c0d0e0f10"));
        byte[] second = sender.EncryptBatch(Convert.FromHexString("ff111213"));

        Assert.Equal("b803d418c1e0c41c85489d80bde4766293c4ca4f9098a3031c", Convert.ToHexString(first).ToLowerInvariant());
        Assert.Equal("98f4970e5745d2c13934514b", Convert.ToHexString(second).ToLowerInvariant());
        Assert.Equal(Convert.FromHexString("ff0102030405060708090a0b0c0d0e0f10"), receiver.DecryptBatch(first));
        Assert.Equal(Convert.FromHexString("ff111213"), receiver.DecryptBatch(second));
        Assert.Equal(2UL, sender.Counter);
        Assert.Equal(2UL, receiver.Counter);
    }

    [Fact]
    public void TamperedCiphertextFailsBeforeCallerCanDecompress()
    {
        byte[] key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        using var sender = new BedrockEncryptionState(key, Secret);
        using var receiver = new BedrockEncryptionState(key, Secret);
        byte[] ciphertext = sender.EncryptBatch(Convert.FromHexString("ff010203"));
        ciphertext[0] ^= 0x80;

        Assert.Throws<BedrockCodecException>(() => receiver.DecryptBatch(ciphertext));
        Assert.True(receiver.IsTerminal);
    }

    [Fact]
    public void RawP384AgreementMatchesCurveDoubleVector()
    {
        byte[] generatorX = Convert.FromHexString("aa87ca22be8b05378eb1c71ef320ad746e1d3b628ba79b9859f741e082542a385502f25dbf55296c3a545e3872760ab7");
        byte[] generatorY = Convert.FromHexString("3617de4a96262c6f5d9e98bf9292dc29f8f41dbd289a147ce9da3113b5f0b8c00a60b1ce1d7e819d7a431d7c90ea0e5f");
        byte[] privateScalar = new byte[48];
        privateScalar[^1] = 1;
        using ECDiffieHellman local = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP384,
            D = privateScalar,
            Q = new ECPoint { X = generatorX, Y = generatorY },
        });
        byte[] peer = Convert.FromHexString("0408d999057ba3d2d969260045c55b97f089025959a6f434d651d207d19fb96e9e4fe0e86ebe0e64f85b96a9c75295df618e80f1fa5b1b3cedb7bfe8dffd6dba74b275d875bc6cc43e904e505f256ab4255ffd43e94d39e22d61501e700a940e80");

        byte[] secret = BedrockEncryption.DeriveRawP384Secret(local, peer);

        Assert.Equal(peer.AsSpan(1, 48).ToArray(), secret);
    }
}
