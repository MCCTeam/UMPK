using System.Security.Cryptography;
using Umpk.Protocol.Java.Crypto;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Crypto;

public class AesCfb8Tests
{
    private static byte[] Key16(byte seed)
    {
        var key = new byte[16];
        for (int i = 0; i < 16; i++)
            key[i] = (byte)(seed + i * 7);

        return key;
    }

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        byte[] key = Key16(0x11);
        var data = new byte[1000];
        Random.Shared.NextBytes(data);

        var enc = AesCfb8.Create(key);
        var dec = AesCfb8.Create(key);

        var cipher = new byte[data.Length];
        enc.Encrypt(data, cipher);
        var plain = new byte[data.Length];
        dec.Decrypt(cipher, plain);

        Assert.Equal(data, plain);
        Assert.NotEqual(data, cipher);
    }

    [Fact]
    public void InPlace_EncryptDecrypt_RoundTrips()
    {
        byte[] key = Key16(0x22);
        var data = new byte[257];
        Random.Shared.NextBytes(data);
        byte[] original = (byte[])data.Clone();

        AesCfb8.Create(key).Encrypt(data, data);
        Assert.NotEqual(original, data);
        AesCfb8.Create(key).Decrypt(data, data);
        Assert.Equal(original, data);
    }

    [Fact]
    public void IntrinsicPath_MatchesSoftwarePath()
    {
        byte[] key = Key16(0x33);
        var data = new byte[512];
        Random.Shared.NextBytes(data);

        var hw = AesCfb8.Create(key, key, forceSoftware: false);
        var sw = AesCfb8.Create(key, key, forceSoftware: true);

        var hwOut = new byte[data.Length];
        var swOut = new byte[data.Length];
        hw.Encrypt(data, hwOut);
        sw.Encrypt(data, swOut);

        Assert.Equal(swOut, hwOut);
    }

    [Fact]
    public void ChunkedEncryption_EqualsSingleShot()
    {
        // Feeding the stream in pieces must equal encrypting all at once (stateful cipher).
        byte[] key = Key16(0x44);
        var data = new byte[300];
        Random.Shared.NextBytes(data);

        var single = new byte[data.Length];
        AesCfb8.Create(key).Encrypt(data, single);

        var chunked = new byte[data.Length];
        var streaming = AesCfb8.Create(key);
        int[] sizes = [1, 15, 16, 17, 100, 151];
        int pos = 0;
        foreach (int size in sizes)
        {
            int n = Math.Min(size, data.Length - pos);
            streaming.Encrypt(data.AsSpan(pos, n), chunked.AsSpan(pos, n));
            pos += n;
        }

        streaming.Encrypt(data.AsSpan(pos), chunked.AsSpan(pos));
        Assert.Equal(single, chunked);
    }

    [Fact]
    public void MatchesBclAesCfb8_KnownAnswer()
    {
        // Cross-check against the BCL's AES-CFB8 (feedback size 8) as an independent oracle.
        byte[] key = Key16(0x55);
        var data = new byte[128];
        Random.Shared.NextBytes(data);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CFB;
        aes.FeedbackSize = 8;
        aes.Padding = PaddingMode.None;
        aes.IV = key; // Minecraft uses the key as the IV.

        using var bclEncryptor = aes.CreateEncryptor();
        var expected = bclEncryptor.TransformFinalBlock(data, 0, data.Length);

        var actual = new byte[data.Length];
        AesCfb8.Create(key).Encrypt(data, actual);

        Assert.Equal(expected, actual);
    }
}
