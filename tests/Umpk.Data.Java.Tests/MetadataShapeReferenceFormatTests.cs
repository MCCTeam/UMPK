using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>The shape-reference table's KIND discriminator, exercised on hand-built blobs.</summary>
/// <remarks>
/// <para>Kinds use distinct numeric discriminators. Unknown kinds are refused, and kind 1 retains block-id semantics even though kind 2 begins with the same base array.</para>
/// <para>These are built by hand rather than read from the datasets on purpose. A round-trip through the emitter would agree with itself through a wrong codec; a literal blob cannot.</para>
/// </remarks>
public sealed class MetadataShapeReferenceFormatTests
{
    /// <summary>Pool: index 0 empty, 1 a full cube, 2 a bottom slab, 3 a top slab.</summary>
    private static byte[] Pool()
    {
        var bytes = new List<byte> { 4 };
        bytes.Add(0);                                        // shape 0: no boxes
        AppendBox(bytes, 0, 0, 0, 1, 1, 1);
        AppendBox(bytes, 0, 0, 0, 1, 0.5, 1);
        AppendBox(bytes, 0, 0.5, 0, 1, 1, 1);
        return [.. bytes];
    }

    private static void AppendBox(List<byte> into, params double[] coords)
    {
        into.Add(1);                                         // one box in this shape
        foreach (double coord in coords)
        {
            byte[] be = BitConverter.GetBytes(coord);
            Array.Reverse(be);
            into.AddRange(be);
        }
    }

    private static void WriteVarInt(List<byte> into, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            into.Add((byte)(v | 0x80));
            v >>= 7;
        }

        into.Add((byte)v);
    }

    /// <summary>Base array: block 1 is a full cube, block 44 a bottom slab, everything else uncovered.</summary>
    private static void AppendBase(List<byte> into)
    {
        WriteVarInt(into, 45);
        for (int blockId = 0; blockId < 45; blockId++)
            WriteVarInt(into, blockId switch { 1 => 2, 44 => 3, _ => 0 });

    }

    private static byte[] Refs(int kind, params (int StateId, int Value)[] overrides)
    {
        List<byte> bytes = [];
        WriteVarInt(bytes, kind);
        AppendBase(bytes);
        if (kind == 2)
        {
            WriteVarInt(bytes, overrides.Length);
            foreach ((int stateId, int value) in overrides)
            {
                WriteVarInt(bytes, stateId);
                WriteVarInt(bytes, value);
            }
        }

        return [.. bytes];
    }

    /// <summary>Kind 1 resolves by block id, so every metadata value shares one box.</summary>
    [Fact]
    public void Kind_1_still_resolves_by_block_id_so_every_meta_shares_one_box()
    {
        var shapes = new JavaBlockShapes(Pool(), Refs(1));

        AssertBox(shapes.GetCollisionShapes(44 << 4), 0, 0, 0, 1, 0.5, 1);
        AssertBox(shapes.GetCollisionShapes((44 << 4) | 8), 0, 0, 0, 1, 0.5, 1);
        Assert.True(shapes.Covers((44 << 4) | 8));
    }

    /// <summary>Kind 2 names individual states; the rest of the block's metas inherit its box.</summary>
    [Fact]
    public void Kind_2_resolves_the_named_state_and_inherits_everywhere_else()
    {
        var shapes = new JavaBlockShapes(Pool(), Refs(2, ((44 << 4) | 8, 4)));

        AssertBox(shapes.GetCollisionShapes(44 << 4), 0, 0, 0, 1, 0.5, 1);
        AssertBox(shapes.GetCollisionShapes((44 << 4) | 8), 0, 0.5, 0, 1, 1, 1);
        AssertBox(shapes.GetCollisionShapes((44 << 4) | 7), 0, 0, 0, 1, 0.5, 1);
        AssertBox(shapes.GetCollisionShapes((44 << 4) | 9), 0, 0, 0, 1, 0.5, 1);
    }

    /// <summary>An uncovered block stays uncovered under kind 2, for every one of its metas.</summary>
    [Fact]
    public void Kind_2_does_not_invent_coverage_for_an_uncovered_block()
    {
        var shapes = new JavaBlockShapes(Pool(), Refs(2, ((44 << 4) | 8, 4)));

        for (int meta = 0; meta < 16; meta++)
        {
            Assert.False(shapes.Covers((7 << 4) | meta));
            AssertBox(shapes.GetCollisionShapes((7 << 4) | meta), 0, 0, 0, 1, 1, 1);
        }
    }

    /// <summary>The two kinds share a prefix, so this is the cross-era rejection: the SAME base bytes must give different answers under kind 1 and kind 2, and a kind-1 reader must not run on past its array.</summary>
    [Fact]
    public void The_kind_number_and_not_the_length_decides_how_the_table_is_read()
    {
        byte[] asKind1 = Refs(1);
        byte[] asKind2 = Refs(2, ((44 << 4) | 8, 4));

        // Identical apart from the leading kind byte and the trailing override block.
        Assert.Equal(asKind1[1..], asKind2[1..(asKind1.Length - 1 + 1)]);

        var one = new JavaBlockShapes(Pool(), asKind1);
        var two = new JavaBlockShapes(Pool(), asKind2);

        AssertBox(one.GetCollisionShapes((44 << 4) | 8), 0, 0, 0, 1, 0.5, 1);
        AssertBox(two.GetCollisionShapes((44 << 4) | 8), 0, 0.5, 0, 1, 1, 1);
    }

    /// <summary>A kind nobody wrote must degrade to the honest flag fallback, not be read as one of the kinds that happen to be implemented. Answering geometry out of a format you do not understand is how a bot ends up standing inside a wall.</summary>
    [Fact]
    public void An_unknown_kind_is_refused_rather_than_guessed_at()
    {
        var shapes = new JavaBlockShapes(Pool(), Refs(3));

        Assert.Equal(0, shapes.RefCount);
        Assert.False(shapes.Covers(44 << 4));
        AssertBox(shapes.GetCollisionShapes(44 << 4), 0, 0, 0, 1, 1, 1);
        Assert.True(shapes.GetCollisionShapes(0).IsEmpty);
    }

    /// <summary>The expansion is per legacy state, so the table is sixteen entries per block id.</summary>
    [Fact]
    public void Kind_2_expands_to_sixteen_entries_per_block_id()
    {
        var shapes = new JavaBlockShapes(Pool(), Refs(2, ((44 << 4) | 8, 4)));
        Assert.Equal(45 * 16, shapes.RefCount);
        Assert.Equal(2 * 16, shapes.CoveredCount);   // blocks 1 and 44, all sixteen metas each
    }

    private static void AssertBox(ReadOnlySpan<Aabb> shape,
        double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.Equal(1, shape.Length);
        Assert.Equal(minX, shape[0].MinX, 6);
        Assert.Equal(minY, shape[0].MinY, 6);
        Assert.Equal(minZ, shape[0].MinZ, 6);
        Assert.Equal(maxX, shape[0].MaxX, 6);
        Assert.Equal(maxY, shape[0].MaxY, 6);
        Assert.Equal(maxZ, shape[0].MaxZ, 6);
    }
}
