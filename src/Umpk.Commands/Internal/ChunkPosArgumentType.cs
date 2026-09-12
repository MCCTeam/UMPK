using Brigadier.NET;
using Umpk.Geometry;
using Brig = Brigadier.NET.ArgumentTypes;

namespace Umpk.Commands.Internal;

/// <summary>Parses a chunk coordinate pair (two whitespace-separated integers) into a <see cref="ChunkPos"/>. Only plain integers are accepted; <c>~</c> and <c>^</c> relative syntax belongs to <see cref="LocationArgumentType"/>.</summary>
internal sealed class ChunkPosArgumentType : Brig.IArgumentType<ChunkPos>
{
    internal static readonly ChunkPosArgumentType Instance = new();

    /// <inheritdoc/>
    public IEnumerable<string> Examples => ["0 0", "-3 7"];

    /// <inheritdoc/>
    public ChunkPos Parse(IStringReader reader)
    {
        reader.SkipWhitespace();
        int x = reader.ReadInt();
        reader.SkipWhitespace();
        int z = reader.ReadInt();
        return new ChunkPos(x, z);
    }
}
