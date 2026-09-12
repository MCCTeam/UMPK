using Umpk.Nbt;
using Xunit;

namespace Umpk.Nbt.Tests;

public sealed class OrderPreservationTests
{
    [Fact]
    public void Compound_PreservesNonAlphabeticalKeyOrderAcrossReencode()
    {
        // Deliberately non-alphabetical: a HashMap-backed compound would reorder these and break byte-identity. Order preservation is the normative requirement.
        var c = new NbtCompound();
        c.PutInt("zebra", 1);
        c.PutInt("apple", 2);
        c.PutInt("mango", 3);
        c.PutInt("banana", 4);

        byte[] encoded = NbtWriter.ToArray(c, NbtWireFormat.JavaNamedRoot);
        var decoded = (NbtCompound)NbtReader.Read(encoded, NbtWireFormat.JavaNamedRoot);

        Assert.Equal(new[] { "zebra", "apple", "mango", "banana" }, decoded.Keys.ToArray());

        byte[] reencoded = NbtWriter.ToArray(decoded, NbtWireFormat.JavaNamedRoot);
        Assert.Equal(encoded, reencoded);
    }

    [Fact]
    public void DecodedOrderMatchesWireOrder_NotSortedOrder()
    {
        // Hand-built wire bytes with keys in reverse-sorted order.
        var builder = new List<byte> { 0x0A }; // compound root
        AppendName(builder);                    // empty root name
        AppendMember(builder, "c", 3);
        AppendMember(builder, "b", 2);
        AppendMember(builder, "a", 1);
        builder.Add(0x00); // end

        var decoded = (NbtCompound)NbtReader.Read(builder.ToArray(), NbtWireFormat.JavaNamedRoot);
        Assert.Equal(new[] { "c", "b", "a" }, decoded.Keys.ToArray());

        static void AppendName(List<byte> b)
        {
            b.Add(0x00);
            b.Add(0x00);
        }

        static void AppendMember(List<byte> b, string name, int value)
        {
            b.Add(0x03); // int
            b.Add(0x00);
            b.Add((byte)name.Length);
            foreach (char ch in name)
                b.Add((byte)ch);

            b.Add((byte)((value >> 24) & 0xFF));
            b.Add((byte)((value >> 16) & 0xFF));
            b.Add((byte)((value >> 8) & 0xFF));
            b.Add((byte)(value & 0xFF));
        }
    }
}
