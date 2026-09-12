using System.Collections.Frozen;
using Umpk.Game.Entities;
using Umpk.Game.Registries;

namespace Umpk.Data.Java;

/// <summary>The tier-2 metadata-key source for Java: which tier-1 index each semantic <see cref="EntityMetadataKeys"/> field occupies at a protocol number.</summary>
/// <remarks>The index tables are generated per protocol and retain the declaring entity kind. Health is declared on <c>LivingEntity</c> rather than <c>Entity</c>: resolving it for a non-living entity yields whatever that type put at the same index, so the tier-2 accessor projects by kind and treats a mismatch as a graceful miss, and a wrong read degrades to no read.</remarks>
public sealed partial class JavaEntityMetadataKeys : IMetadataKeySource
{
    private readonly FrozenDictionary<MetadataKey, int> _indices;
    private readonly int _itemStackIndex;

    private JavaEntityMetadataKeys(int itemStackIndex, KeyValuePair<MetadataKey, int>[] indices)
    {
        _itemStackIndex = itemStackIndex;
        _indices = indices.ToFrozenDictionary();
    }

    private static ArgumentOutOfRangeException Unsupported(int protocol) =>
        new(nameof(protocol), protocol, $"No entity-metadata key table: protocol {protocol} is not supported.");

    /// <inheritdoc />
    public bool TryResolveIndex(RegistryEntry<EntityTypeDefinition> entityType, MetadataKey key, out int index)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (ReferenceEquals(key, EntityMetadataKeys.CarriedItem))
        {
            bool itemEntity = !entityType.IsDefault
                && entityType.Id.Namespace == "minecraft"
                && entityType.Id.Path == "item";
            index = _itemStackIndex;
            return itemEntity;
        }

        return _indices.TryGetValue(key, out index);
    }
}
