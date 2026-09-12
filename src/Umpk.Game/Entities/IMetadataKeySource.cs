using Umpk.Game.Registries;

namespace Umpk.Game.Entities;

/// <summary>The tier-2 metadata-key resolution seam. Given an entity type and a semantic <see cref="MetadataKey"/>, it yields the tier-1 metadata index that field occupies for the bound version. <c>Umpk.Data.Java</c> supplies the per-version <c>(entity type, field index) -&gt; semantic key</c> map, with curated tables for the oldest versions.</summary>
/// <remarks>A source may cover only part of the entity hierarchy; a miss returns false and the tier-2 accessor degrades gracefully. Tier 1 remains complete without any source.</remarks>
public interface IMetadataKeySource
{
    /// <summary>Resolves the tier-1 metadata index that <paramref name="key"/> occupies for <paramref name="entityType"/>. Returns false when the index is unknown.</summary>
    bool TryResolveIndex(RegistryEntry<EntityTypeDefinition> entityType, MetadataKey key, out int index);
}
