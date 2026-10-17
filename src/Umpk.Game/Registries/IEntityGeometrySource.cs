using Umpk.Game.Entities;

namespace Umpk.Game.Registries;

/// <summary>The native operation used when synchronized data changes an entity's dimensions. Position reapplication always recenters the box.</summary>
public enum EntityDimensionsRefresh
{
    /// <summary>Recenter the box around the current position.</summary>
    Recenter = 0,
    /// <summary>Recenter when shrinking; otherwise retain the current box minimum.</summary>
    RetainMinimumOnGrowth = 1,
    /// <summary>Retain the current box minimum when growing or shrinking.</summary>
    RetainMinimum = 2,
}

/// <summary>The native predicate that selects an entity's alternate dimensions.</summary>
public enum EntityGeometrySelectorKind
{
    /// <summary>No synchronized selector applies.</summary>
    None = 0,
    /// <summary>A synchronized Boolean selects the alternate dimensions when true.</summary>
    Boolean = 1,
    /// <summary>A synchronized signed byte selects the alternate dimensions when negative.</summary>
    NegativeByte = 2,
}

/// <summary>The native phase that applies changed dimensions.</summary>
public enum EntityGeometryRefreshTiming
{
    /// <summary>Apply the dimensions during the synchronized-data callback.</summary>
    SynchronizedData = 0,
    /// <summary>Apply the dimensions after the inherited living-entity phase.</summary>
    LivingTick = 1,
}

/// <summary>A measured metadata selector and dimensions refresh contract. MetadataIndex is -1 when no selector applies. An absent scale attribute means attribute scaling does not apply.</summary>
public readonly record struct EntityGeometryRule(int MetadataIndex, int PoseMetadataIndex, EntityDimensionsRefresh Refresh, Identifier? ScaleAttribute)
{
    /// <summary>The native selector. Existing rules retain their Boolean or absent selector.</summary>
    public EntityGeometrySelectorKind SelectorKind { get; init; } = MetadataIndex < 0 ? EntityGeometrySelectorKind.None : EntityGeometrySelectorKind.Boolean;

    /// <summary>The phase that applies changed dimensions.</summary>
    public EntityGeometryRefreshTiming RefreshTiming { get; init; }

    /// <summary>The horizontal block radius whose intersecting columns must be loaded before a deferred living phase can run. The area includes both endpoints and the entity's own column.</summary>
    public int RequiredLoadedRadius { get; init; }
}

/// <summary>Native dimensions for one metadata and pose state. Fixed dimensions bypass attribute scaling.</summary>
public readonly record struct EntityGeometryDimensions(float Width, float Height, bool Fixed, bool BlocksBuilding);

/// <summary>Audited entity geometry. Unmeasured types, selectors and poses remain unknown; registry placeholder dimensions are not a fallback.</summary>
public interface IEntityGeometrySource
{
    /// <summary>Returns the native metadata selector and refresh contract for an audited type. A MetadataIndex of -1 requires no selector.</summary>
    bool TryGetRule(Identifier entityType, out EntityGeometryRule rule);
    /// <summary>Returns dimensions at the default attribute scale for one Boolean selector and canonical pose. Use false when the rule has no selector.</summary>
    bool TryGetDimensions(Identifier entityType, bool selector, EntityPose pose, out EntityGeometryDimensions dimensions);
}
