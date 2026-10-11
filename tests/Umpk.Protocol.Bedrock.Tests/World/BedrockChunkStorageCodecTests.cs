using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.World;

public sealed class BedrockChunkStorageCodecTests
{
    [Fact]
    public void V2StorageDecodesIndicesAndRoundTrips()
    {
        byte[] payload = StorageBytes(2, true, WordsV2(), [10, 20, 30]);

        BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeStorage(payload);

        Assert.Equal(2, storage.BitsPerBlock);
        Assert.True(storage.IsRuntime);
        Assert.Equal([10, 20, 30], storage.Palette);
        Assert.Equal(20, storage.Get(0, 0, 0));
        Assert.Equal(10, storage.Get(1, 0, 0));
        Assert.Equal(30, storage.Get(0, 1, 0));
        Assert.Equal(payload, BedrockChunkPayloadCodecs.EncodeStorage(storage));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(16)]
    public void EverySupportedWidthDecodesUniformPaletteAndRoundTrips(int bits)
    {
        byte[] payload = StorageBytes(bits, true, new int[WordCount(bits)], [7]);

        BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeStorage(payload);

        Assert.Equal(bits, storage.BitsPerBlock);
        Assert.Equal([7], storage.Palette);
        Assert.Equal(7, storage.Get(15, 15, 15));
        Assert.Equal(7, storage.Get(0, 0, 0));
        Assert.Equal(payload, BedrockChunkPayloadCodecs.EncodeStorage(storage));
    }

    [Fact]
    public void SingletonV0CarriesOneEntryWithoutSize()
    {
        byte[] payload = [0x01, 0x2A];

        BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeStorage(payload);

        Assert.Equal(0, storage.BitsPerBlock);
        Assert.True(storage.IsRuntime);
        Assert.Equal([21], storage.Palette);
        Assert.Equal(21, storage.Get(7, 3, 11));
        Assert.Equal(payload, BedrockChunkPayloadCodecs.EncodeStorage(storage));
    }

    [Fact]
    public void SingletonV0RejectsTrailingBytes()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeStorage([0x01, 0x2A, 0x00]));
    }

    [Fact]
    public void SingletonV0BiomeRejectsNegativeEntry()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeBiomeStorage([0x01, 0x01]));
    }

    [Fact]
    public void SingletonV0BlockPreservesNegativeEntry()
    {
        BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeStorage([0x01, 0x01]);

        Assert.Equal([-1], storage.Palette);
    }

    [Fact]
    public void IndexOrderIsXzy()
    {
        var words = new int[1024];
        words[0] = 1;
        byte[] payload = StorageBytes(8, true, words, [100, 200]);

        BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeStorage(payload);

        Assert.Equal(200, storage.Get(0, 0, 0));
        Assert.Equal(100, storage.Get(1, 0, 0));
        Assert.Equal(100, storage.Get(0, 1, 0));
        Assert.Equal(100, storage.Get(0, 0, 1));
    }

    [Fact]
    public void PaddedV3UnpacksWithoutStraddlingWords()
    {
        var words = new int[410];
        words[0] = 1 | (2 << 6) | (1 << 9);
        byte[] payload = StorageBytes(3, true, words, [50, 60, 70]);

        BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeStorage(payload);

        Assert.Equal(60, storage.Get(0, 0, 0));
        Assert.Equal(50, storage.Get(0, 1, 0));
        Assert.Equal(70, storage.Get(0, 2, 0));
        Assert.Equal(60, storage.Get(0, 3, 0));
        Assert.Equal(50, storage.Get(1, 0, 0));
        Assert.Equal(payload, BedrockChunkPayloadCodecs.EncodeStorage(storage));
    }

    [Fact]
    public void PersistentStorageFlagDecodes()
    {
        byte[] payload = StorageBytes(2, false, WordsV2(), [10, 20, 30]);

        BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeStorage(payload);

        Assert.False(storage.IsRuntime);
        Assert.Equal(20, storage.Get(0, 0, 0));
        Assert.Equal(payload, BedrockChunkPayloadCodecs.EncodeStorage(storage));
    }

    [Fact]
    public void CopyLastHeaderIsRejected()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeStorage([0xFF]));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(15)]
    public void UnsupportedWidthsAreRejected(int bits)
    {
        byte header = (byte)((bits << 1) | 1);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeStorage([header]));
    }

    [Fact]
    public void EmptyPaletteIsRejected()
    {
        byte[] payload = StorageBytes(1, true, new int[128], []);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeStorage(payload));
    }

    [Fact]
    public void OversizedPaletteIsRejected()
    {
        using var output = new MemoryStream();
        output.WriteByte((byte)((1 << 1) | 1));
        for (int index = 0; index < 128; index++)
            output.Write([0, 0, 0, 0], 0, 4);
        WriteZigZag(output, 5000);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeStorage(output.ToArray()));
    }

    [Fact]
    public void PaletteIndexOutOfRangeIsRejected()
    {
        var words = new int[256];
        words[0] = 3;
        byte[] payload = StorageBytes(2, true, words, [10, 20]);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeStorage(payload));
    }

    [Fact]
    public void TruncatedWordsAreRejected()
    {
        byte[] payload = StorageBytes(8, true, new int[1024], [1]);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeStorage(payload[..^4]));
    }

    [Fact]
    public void BiomeStorageRejectsNegativeEntries()
    {
        byte[] payload = StorageBytes(1, true, new int[128], [-5]);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeBiomeStorage(payload));
    }

    [Fact]
    public void SectionEnvelopeV9RoundTripsWithNegativeY()
    {
        byte[] first = StorageBytes(1, true, new int[128], [0]);
        byte[] second = StorageBytes(1, true, new int[128], [0]);
        byte[] payload = [9, 2, 0xFC, .. first, .. second];

        BedrockChunkSection section = BedrockChunkPayloadCodecs.DecodeSection(payload);

        Assert.Equal(-4, section.SectionY);
        Assert.Equal(2, section.Layers.Count);
        Assert.Equal(payload, BedrockChunkPayloadCodecs.EncodeSection(section));
    }

    [Fact]
    public void SectionRejectsUnknownVersion()
    {
        byte[] layer = StorageBytes(1, true, new int[128], [0]);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeSection([8, 1, 0, .. layer]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SectionRejectsLayerCountsOutsideOneToTwo(int layers)
    {
        byte[] layer = StorageBytes(1, true, new int[128], [0]);
        var payload = new byte[3 + layer.Length * layers];
        payload[0] = 9;
        payload[1] = (byte)layers;
        for (int index = 0; index < layers; index++)
            Array.Copy(layer, 0, payload, 3 + index * layer.Length, layer.Length);
        Assert.Throws<BedrockCodecException>(() => BedrockChunkPayloadCodecs.DecodeSection(payload));
    }

    private static int[] WordsV2()
    {
        var words = new int[256];
        words[0] = 0b00_01_10_01;
        return words;
    }

    private static int WordCount(int bits) => bits switch
    {
        0 => 0,
        1 => 128,
        2 => 256,
        3 => 410,
        4 => 512,
        5 => 683,
        6 => 820,
        8 => 1024,
        16 => 2048,
        _ => throw new ArgumentOutOfRangeException(nameof(bits)),
    };

    private static byte[] StorageBytes(int bits, bool runtime, int[] words, int[] palette)
    {
        using var output = new MemoryStream();
        output.WriteByte((byte)((bits << 1) | (runtime ? 1 : 0)));
        Span<byte> scratch = stackalloc byte[4];
        foreach (int word in words)
        {
            BitConverter.TryWriteBytes(scratch, word);
            if (!BitConverter.IsLittleEndian)
                scratch.Reverse();
            output.Write(scratch.ToArray(), 0, 4);
        }
        if (bits == 0)
        {
            if (palette.Length != 1)
                throw new ArgumentOutOfRangeException(nameof(palette), "The singleton helper carries exactly one entry.");
            WriteZigZag(output, palette[0]);
            return output.ToArray();
        }

        WriteZigZag(output, palette.Length);
        foreach (int entry in palette)
            WriteZigZag(output, entry);
        return output.ToArray();
    }

    private static void WriteZigZag(MemoryStream output, int value)
    {
        uint encoded = (uint)((value << 1) ^ (value >> 31));
        while (encoded >= 0x80)
        {
            output.WriteByte((byte)(encoded | 0x80));
            encoded >>= 7;
        }
        output.WriteByte((byte)encoded);
    }
}
