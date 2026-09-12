using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class RandomizedRoundTripTests
{
    [Fact]
    public void SeededRandomTrees_EncodeDecodeEncode_ByteIdentical()
    {
        var rng = new Random(0xC0FFEE);
        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            NbtCompound tree = BuildCompound(rng, depth: 0);
            byte[] encoded = NbtWriter.ToArray(tree, NbtWireFormat.JavaNamedRoot);
            var decoded = (NbtCompound)NbtReader.Read(encoded, NbtWireFormat.JavaNamedRoot);
            byte[] reencoded = NbtWriter.ToArray(decoded, NbtWireFormat.JavaNamedRoot);

            Assert.True(
                encoded.AsSpan().SequenceEqual(reencoded),
                $"Byte mismatch on iteration {iteration}");
            Assert.Equal(tree, decoded);
        }
    }

    private static NbtCompound BuildCompound(Random rng, int depth)
    {
        var c = new NbtCompound();
        int members = rng.Next(0, 6);
        for (int i = 0; i < members; i++)
        {
            string key = RandomKey(rng);
            c.Put(key, RandomTag(rng, depth));
        }

        return c;
    }

    private static NbtTag RandomTag(Random rng, int depth)
    {
        // Cap recursion so trees stay well under the depth limit.
        int max = depth >= 4 ? 9 : 12;
        int choice = rng.Next(0, max);
        return choice switch
        {
            0 => new NbtByte(unchecked((sbyte)rng.Next())),
            1 => new NbtShort((short)rng.Next()),
            2 => new NbtInt(rng.Next()),
            3 => new NbtLong(((long)rng.Next() << 32) | (uint)rng.Next()),
            4 => new NbtFloat((float)(rng.NextDouble() * 1000 - 500)),
            5 => new NbtDouble(rng.NextDouble() * 1e9 - 5e8),
            6 => new NbtString(RandomString(rng)),
            7 => RandomByteArray(rng),
            8 => RandomIntArray(rng),
            9 => RandomLongArray(rng),
            10 => RandomList(rng, depth + 1),
            _ => BuildCompound(rng, depth + 1),
        };
    }

    private static NbtByteArray RandomByteArray(Random rng)
    {
        var data = new sbyte[rng.Next(0, 8)];
        for (int i = 0; i < data.Length; i++)
            data[i] = unchecked((sbyte)rng.Next());

        return new NbtByteArray(data);
    }

    private static NbtIntArray RandomIntArray(Random rng)
    {
        var data = new int[rng.Next(0, 8)];
        for (int i = 0; i < data.Length; i++)
            data[i] = rng.Next();

        return new NbtIntArray(data);
    }

    private static NbtLongArray RandomLongArray(Random rng)
    {
        var data = new long[rng.Next(0, 8)];
        for (int i = 0; i < data.Length; i++)
            data[i] = ((long)rng.Next() << 32) | (uint)rng.Next();

        return new NbtLongArray(data);
    }

    private static NbtList RandomList(Random rng, int depth)
    {
        // A homogeneous list: pick one element type, then emit 0..4 elements of it.
        int count = rng.Next(0, 5);
        int kind = rng.Next(0, 7);
        var list = new NbtList();
        for (int i = 0; i < count; i++)
        {
            NbtTag element = kind switch
            {
                0 => new NbtByte(unchecked((sbyte)rng.Next())),
                1 => new NbtInt(rng.Next()),
                2 => new NbtLong(((long)rng.Next() << 32) | (uint)rng.Next()),
                3 => new NbtFloat((float)rng.NextDouble()),
                4 => new NbtDouble(rng.NextDouble()),
                5 => new NbtString(RandomString(rng)),
                _ => BuildCompound(rng, depth + 1),
            };
            list.Add(element);
        }

        return list;
    }

    private static string RandomKey(Random rng)
    {
        int len = rng.Next(1, 8);
        Span<char> chars = stackalloc char[len];
        for (int i = 0; i < len; i++)
            chars[i] = (char)('a' + rng.Next(0, 26));

        return new string(chars);
    }

    private static string RandomString(Random rng)
    {
        int len = rng.Next(0, 12);
        var chars = new char[len];
        for (int i = 0; i < len; i++)
        {
            // Mix ASCII, Latin-1, and occasionally a BMP character to exercise modified UTF-8.
            int r = rng.Next(0, 100);
            chars[i] = r switch
            {
                < 70 => (char)rng.Next(0x20, 0x7F),
                < 90 => (char)rng.Next(0x80, 0x800),
                _ => (char)rng.Next(0x800, 0xFFFF),
            };
        }

        return new string(chars);
    }
}
