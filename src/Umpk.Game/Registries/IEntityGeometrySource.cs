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

/// <summary>A measured metadata selector and dimensions refresh contract. An absent scale attribute means attribute scaling does not apply.</summary>
public readonly record struct EntityGeometryRule(int MetadataIndex, int PoseMetadataIndex, EntityDimensionsRefresh Refresh, Identifier? ScaleAttribute);

/// <summary>Native dimensions for one metadata and pose state. Fixed dimensions bypass attribute scaling.</summary>
public readonly record struct EntityGeometryDimensions(float Width, float Height, bool Fixed, bool BlocksBuilding);

/// <summary>Audited entity geometry. Unmeasured types, selectors and poses remain unknown; registry placeholder dimensions are not a fallback.</summary>
public interface IEntityGeometrySource
{
    /// <summary>Returns the native Boolean metadata selector and refresh contract for an audited type.</summary>
    bool TryGetRule(Identifier entityType, out EntityGeometryRule rule);
    /// <summary>Returns dimensions at the default attribute scale for one Boolean selector and canonical pose.</summary>
    bool TryGetDimensions(Identifier entityType, bool selector, EntityPose pose, out EntityGeometryDimensions dimensions);
}
