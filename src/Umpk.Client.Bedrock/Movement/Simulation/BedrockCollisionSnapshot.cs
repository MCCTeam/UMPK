namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Availability of one integer cell with unknown kept distinct from air.</summary>
internal enum BedrockCollisionCell
{
    /// <summary>Cell availability is unknown and must refuse movement rather than read as air.</summary>
    Unknown = 0,
    /// <summary>Cell is known to hold no collision.</summary>
    KnownAir = 1,
    /// <summary>Cell is known to hold solid collision treated as a full cube as scaffold policy.</summary>
    KnownSolid = 2,
}

/// <summary>Immutable versioned cell view with explicit unavailable terrain.</summary>
/// <remarks>The version is caller-owned and the stepper does not bind it to world or request generations, so callers must supply a generation-consistent snapshot with caller-owned bounds.</remarks>
internal sealed class BedrockCollisionSnapshot
{
    private readonly Dictionary<(int X, int Y, int Z), BedrockCollisionCell> _cells;

    internal BedrockCollisionSnapshot(long version, Dictionary<(int X, int Y, int Z), BedrockCollisionCell>? cells = null)
    {
        Version = version;
        _cells = cells is null ? new Dictionary<(int X, int Y, int Z), BedrockCollisionCell>() : new Dictionary<(int X, int Y, int Z), BedrockCollisionCell>(cells);
    }

    /// <summary>Gets the caller-owned snapshot version without any world or request generation binding.</summary>
    internal long Version { get; }

    /// <summary>Gets the number of explicitly stored cells.</summary>
    internal int CellCount => _cells.Count;

    /// <summary>Returns the stored cell at the given integer coordinate or Unknown when absent.</summary>
    internal BedrockCollisionCell GetCell(int x, int y, int z) => _cells.TryGetValue((x, y, z), out BedrockCollisionCell cell) ? cell : BedrockCollisionCell.Unknown;

    /// <summary>Collects explicitly supplied cells; the caller must keep them consistent with the requested generation.</summary>
    internal sealed class Builder
    {
        private readonly Dictionary<(int X, int Y, int Z), BedrockCollisionCell> _cells = new();

        /// <summary>Stores one cell and returns the builder for chaining.</summary>
        internal Builder Set(int x, int y, int z, BedrockCollisionCell cell)
        {
            _cells[(x, y, z)] = cell;
            return this;
        }

        /// <summary>Stores one cell value over the inclusive integer box and returns the builder for chaining.</summary>
        internal Builder FillBox(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, BedrockCollisionCell cell)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    for (int z = minZ; z <= maxZ; z++)
                        _cells[(x, y, z)] = cell;
            return this;
        }

        /// <summary>Builds an immutable snapshot carrying the given caller-owned version.</summary>
        internal BedrockCollisionSnapshot Build(long version) => new(version, _cells);
    }
}
