using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary><see cref="IBlockPushSource"/> over the generated <c>BlockPush</c> table: one packed VarInt per block, in the version's own block order, so a lookup is a registry hit plus an array index.</summary>
/// <remarks>
/// <para>The table is positional. The guard is arithmetic and refuses rather than degrades: the row count must equal the block registry's own entry count, and every block network id must fall inside the table. A table that disagrees with its registry answers <see cref="HasData"/> false, which the client reads as "do not model pushed blocks here".</para>
/// <para>An EMPTY table says the dataset has no measurement for this protocol. It is deliberately not the same statement as "every block is normal"; see <see cref="IBlockPushSource"/>.</para>
/// </remarks>
internal sealed class JavaBlockPush : IBlockPushSource
{
    private const int ReactionMask = 0x7;
    private const int UnbreakableBit = 1 << 3;
    private const int BlockEntityBit = 1 << 4;

    private readonly Registry<BlockDefinition> _blocks;
    private readonly int[] _rows;

    private JavaBlockPush(Registry<BlockDefinition> blocks, int[] rows)
    {
        _blocks = blocks;
        _rows = rows;
    }

    public bool HasData => _rows.Length > 0;

    /// <summary>Decodes the packed table, or yields a source with no data when the table is empty or does not line up with the block registry it is meant to index.</summary>
    public static JavaBlockPush Create(Registry<BlockDefinition> blocks, ReadOnlySpan<byte> table)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (table.IsEmpty)
            return new JavaBlockPush(blocks, []);

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        if (count <= 0)
            return new JavaBlockPush(blocks, []);

        // The positional-read guard, checked BEFORE allocating or reading a single row. The emitted table has one row per block in the version's own block table; the registry is built from the SAME table, so the counts must agree, and any registry id must be addressable. Anything else means the two were built from different data, and validating first means a mismatched or corrupted `count` never drives an allocation or a read loop sized off it.
        if (blocks.Count != count)
            return new JavaBlockPush(blocks, []);

        foreach (RegistryEntry<BlockDefinition> entry in blocks)
            if ((uint)entry.NetworkId >= (uint)count)
                return new JavaBlockPush(blocks, []);

        int[] rows = new int[count];
        for (int i = 0; i < count; i++)
            rows[i] = reader.ReadVarInt();

        return new JavaBlockPush(blocks, rows);
    }

    public bool TryGet(Identifier block, out BlockPushInfo info)
    {
        info = default;
        if (_rows.Length == 0 || !_blocks.TryGet(block, out RegistryEntry<BlockDefinition> entry))
            return false;

        int networkId = entry.NetworkId;
        if ((uint)networkId >= (uint)_rows.Length)
            return false;

        int packed = _rows[networkId];
        info = new BlockPushInfo(
            (PistonPushReaction)(packed & ReactionMask),
            (packed & UnbreakableBit) != 0,
            (packed & BlockEntityBit) != 0);
        return true;
    }
}
