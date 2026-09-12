namespace Umpk.Game.Registries;

/// <summary>The per-version item table seam. Implemented in <c>Umpk.Data.Java</c> over generated tables. Modern versions key items by a contiguous network id; pre-1.20.5 legacy versions key them by a composite <c>(id, damage)</c> encoded as <c>(id &lt;&lt; 16) | damage</c>, so the seam exposes that composite bridge behind <see cref="IsLegacy"/>.</summary>
public interface IItemDataSource
{
    /// <summary>The item registry for this version; item network ids index it.</summary>
    Registry<ItemDefinition> Items { get; }

    /// <summary>True when this is a pre-1.20.5 source keyed by the <c>(id, damage)</c> composite.</summary>
    bool IsLegacy { get; }

    /// <summary>Resolves the item registry entry for a network id.</summary>
    bool TryGetItem(int networkId, out RegistryEntry<ItemDefinition> item);

    /// <summary>Composes a legacy <c>(id, damage)</c> pair into the composite network key (<c>(id &lt;&lt; 16) | damage</c>). Meaningful only on legacy sources.</summary>
    int EncodeLegacy(int itemId, int damage);

    /// <summary>Splits a legacy composite key back into <c>(id, damage)</c>. Returns false on modern sources.</summary>
    bool TryDecodeLegacy(int compositeKey, out int itemId, out int damage);
}
