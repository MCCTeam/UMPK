using Umpk.Game.Registries;

namespace Umpk.Game.World;

/// <summary>The per-world dimension identity and vertical bounds. Bound to a <see cref="DimensionTypeDefinition"/> so section sizing, Y-to-section math, and skylight presence come from registry data.</summary>
public sealed class DimensionState
{
    /// <summary>Creates a dimension state from its type entry and world name.</summary>
    /// <param name="type">The dimension-type registry entry.</param>
    /// <param name="dimensionName">The dimension/world identifier (for example <c>minecraft:overworld</c>).</param>
    public DimensionState(RegistryEntry<DimensionTypeDefinition> type, Identifier dimensionName)
    {
        Type = type;
        DimensionName = dimensionName;
    }

    /// <summary>The dimension-type registry entry.</summary>
    public RegistryEntry<DimensionTypeDefinition> Type { get; }

    /// <summary>The dimension/world identifier this world instance represents.</summary>
    public Identifier DimensionName { get; }

    /// <summary>The lowest block Y coordinate in this dimension.</summary>
    public int MinY => Type.Value.MinY;

    /// <summary>The total build height in blocks.</summary>
    public int Height => Type.Value.Height;

    /// <summary>The exclusive upper Y bound.</summary>
    public int MaxY => Type.Value.MaxY;

    /// <summary>Whether this dimension receives skylight.</summary>
    public bool HasSkylight => Type.Value.HasSkylight;

    /// <summary>The number of 16-block sections stacked vertically.</summary>
    public int SectionCount => Height / ChunkSection.Size;

    /// <summary>The section index (0-based from <see cref="MinY"/>) that contains block Y <paramref name="blockY"/>.</summary>
    public int SectionIndexForY(int blockY) => (blockY - MinY) >> 4;

    /// <summary>The lowest section's world Y origin.</summary>
    public int MinSectionY => MinY >> 4;
}
