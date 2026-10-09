using System.Runtime.InteropServices;
using Umpk.Protocol.Java.Crypto;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Crypto;

public sealed class Arm32AesCfb8Tests
{
    [Theory]
    [InlineData(Architecture.Arm, false, true)]
    [InlineData(Architecture.Arm, true, false)]
    [InlineData(Architecture.Arm64, false, false)]
    [InlineData(Architecture.X64, false, false)]
    [InlineData(Architecture.X86, false, false)]
    [InlineData(Architecture.Wasm, false, false)]
    public void Factory_SelectsNativeOnlyForArm32(Architecture architecture, bool forceSoftware, bool expectedNative)
    {
        byte[] key = new byte[16];
        AesCfb8 cipher = AesCfb8.Create(key, key, forceSoftware, architecture);

        Assert.Equal(expectedNative, cipher.UsesNativeArm32);
        if (forceSoftware || expectedNative)
            Assert.False(cipher.IsHardwareAccelerated);
    }

    [Fact]
    public void PublicFactory_UsesTheProcessArchitecture()
    {
        AesCfb8 cipher = AesCfb8.Create(new byte[16]);

        Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.Arm, cipher.UsesNativeArm32);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativePath_MatchesNistCfb8Vector(bool decrypt)
    {
        // NIST SP 800-38A, sections F.3.7/F.3.8: the first 16 CFB8-AES128 segments.
        byte[] key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        byte[] iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        byte[] plaintext = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172a");
        byte[] ciphertext = Convert.FromHexString("3b79424c9c0dd436bace9e0ed4586a4f");
        AesCfb8 cipher = Native(key, iv);
        byte[] output = new byte[plaintext.Length];

        if (decrypt)
            cipher.Decrypt(ciphertext, output);
        else
            cipher.Encrypt(plaintext, output);

        Assert.Equal(decrypt ? plaintext : ciphertext, output);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NativePath_PreservesFeedbackAcrossReadBoundaries(bool decrypt, bool inPlace)
    {
        byte[] key = Data(16);
        byte[] iv = Data(24);
        byte[] plaintext = Data(8193);
        byte[] ciphertext = new byte[plaintext.Length];
        AesCfb8.Create(key, iv, forceSoftware: true).Encrypt(plaintext, ciphertext);
        byte[] input = decrypt ? ciphertext : plaintext;
        byte[] output = inPlace ? (byte[])input.Clone() : new byte[input.Length];
        AesCfb8 cipher = Native(key, iv);
        int[] sizes = [0, 1, 2, 7, 15, 16, 17, 31, 255, 1024, 4096];
        int position = 0;
        int call = 0;
        while (position < input.Length)
        {
            int count = Math.Min(sizes[call++ % sizes.Length], input.Length - position);
            ReadOnlySpan<byte> source = (inPlace ? output : input).AsSpan(position, count);
            Span<byte> destination = output.AsSpan(position, count);
            if (decrypt)
                cipher.Decrypt(source, destination);
            else
                cipher.Encrypt(source, destination);

            position += count;
        }

        Assert.Equal(decrypt ? plaintext : ciphertext, output);
    }

    [Fact]
    public void NativePath_UsesOneFeedbackRegisterForBothDirections()
    {
        byte[] key = Data(16);
        AesCfb8 native = Native(key, key);
        AesCfb8 reference = AesCfb8.Create(key, key, forceSoftware: true);
        int[] sizes = [1, 15, 16, 17, 4097];
        for (int i = 0; i < sizes.Length; i++)
        {
            byte[] input = Data(sizes[i]);
            byte[] expected = new byte[input.Length];
            byte[] actual = new byte[input.Length + 3];
            Array.Fill(actual, (byte)0xa5);
            if (i % 2 == 0)
            {
                reference.Encrypt(input, expected);
                native.Encrypt(input, actual);
            }
            else
            {
                reference.Decrypt(input, expected);
                native.Decrypt(input, actual);
            }

            Assert.Equal(expected, actual[..input.Length]);
            Assert.Equal(new byte[] { 0xa5, 0xa5, 0xa5 }, actual[input.Length..]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortOutput_ThrowsWithoutAdvancingFeedback(bool decrypt)
    {
        byte[] key = Data(16);
        byte[] input = Data(33);
        AesCfb8 cipher = Native(key, key);
        byte[] shortOutput = new byte[input.Length - 1];
        if (decrypt)
            Assert.Throws<ArgumentException>(() => cipher.Decrypt(input, shortOutput));
        else
            Assert.Throws<ArgumentException>(() => cipher.Encrypt(input, shortOutput));

        byte[] actual = new byte[input.Length];
        byte[] expected = new byte[input.Length];
        AesCfb8 reference = AesCfb8.Create(key, key, forceSoftware: true);
        if (decrypt)
        {
            reference.Decrypt(input, expected);
            cipher.Decrypt(input, actual);
        }
        else
        {
            reference.Encrypt(input, expected);
            cipher.Encrypt(input, actual);
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NativePath_CopiesTheKeyAndInitialIv()
    {
        byte[] key = Data(16);
        byte[] iv = Data(24);
        byte[] input = Data(100);
        AesCfb8 native = Native(key, iv);
        AesCfb8 reference = AesCfb8.Create(key, iv, forceSoftware: true);
        Array.Fill(key, (byte)0xff);
        Array.Fill(iv, (byte)0xff);
        byte[] actual = new byte[input.Length];
        byte[] expected = new byte[input.Length];

        native.Encrypt(input, actual);
        reference.Encrypt(input, expected);

        Assert.Equal(expected, actual);
    }

    private static AesCfb8 Native(byte[] key, byte[] iv) =>
        AesCfb8.Create(key, iv, forceSoftware: false, Architecture.Arm);

    private static byte[] Data(int count)
    {
        var data = new byte[count];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i * 37 + 11);

        return data;
    }
}
