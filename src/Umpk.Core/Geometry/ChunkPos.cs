using System.Runtime.CompilerServices;

namespace Umpk.Geometry;

/// <summary>A chunk column coordinate (block coordinates arithmetically shifted right by 4).</summary>
public readonly record struct ChunkPos(int X, int Z)
{
    public static readonly ChunkPos Zero = new(0, 0);

    /// <summary>The chunk containing the given block (arithmetic shift, correct for negatives).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChunkPos Containing(BlockPos block) => new(block.X >> 4, block.Z >> 4);

    /// <summary>The world X of this chunk's minimum block corner.</summary>
    public int MinBlockX => X << 4;

    /// <summary>The world Z of this chunk's minimum block corner.</summary>
    public int MinBlockZ => Z << 4;

    public override string ToString() => $"[{X}, {Z}]";
}
