using Umpk.Game.Registries;
using Umpk.Game.World;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Builds a <see cref="ChunkColumn"/> sized to a parsed section count. Chunk decode is self-describing: the section count comes from the payload's own buffer length, not from session-derived dimension height, so the read loop needs no session state to decode terrain. This factory materializes a WIRE-RELATIVE placeholder dimension for the column.</summary>
/// <remarks>
/// <para>The placeholder is not a claim about which dimension the column belongs to, and it must not be read as one. A chunk frame carries no dimension: its sections ride the section buffer bottom-up from the dimension's own floor, and which floor that is is a SESSION fact the codec layer does not have (the same codecs run in a proxy and a server role, where there may be no session at all). So the placeholder states the only thing the wire actually says: section index 0 is the floor section, hence origin 0, one slot per parsed section, under the identifier <c>umpk:wire_relative</c> so a consumer can see the bounds are unbound rather than believing an invented <c>minecraft:overworld</c>.</para>
/// <para><c>World.LoadColumn</c> re-binds the column to the world's real dimension on install, where the floor and section count are known. The column's own floor then governs block placement.</para>
/// </remarks>
internal static class ChunkColumnFactory
{
    /// <summary>The placeholder dimension identifier: bounds relative to the wire, not to a dimension.</summary>
    private static readonly Identifier WireRelative = new("umpk", "wire_relative");

    /// <summary>Creates a column at (x,z) whose placeholder dimension spans <paramref name="sectionCount"/> sections from wire-relative origin 0.</summary>
    public static ChunkColumn Create(int chunkX, int chunkZ, int sectionCount)
    {
        var registry = new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType)
            .Add(0, WireRelative,
                // HasSkylight is false rather than true because the wire says nothing about skylight and a placeholder should not assert the overworld's answer; the real value arrives with the dimension at install. Nothing outside DimensionState reads it today either way.
                new DimensionTypeDefinition(0, Math.Max(sectionCount, 1) * ChunkSection.Size, HasSkylight: false))
            .Build();
        RegistryEntry<DimensionTypeDefinition> entry = registry[0];
        var dimension = new DimensionState(entry, WireRelative);
        return new ChunkColumn(new Umpk.Geometry.ChunkPos(chunkX, chunkZ), dimension);
    }
}
