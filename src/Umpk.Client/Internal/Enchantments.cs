using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Turns the server's own <c>minecraft:enchantment</c> data into a <see cref="Registry{T}"/> of <see cref="EnchantmentDefinition"/>.</summary>
/// <remarks>
/// <para>One wire shape only: the 1.20.5+ configuration-phase <c>registry_data</c> packet, entries in network-id order. The registry became synchronized data in 1.21, while 1.20.6 does not carry it. So there is no 764/765 NBT-blob arm and no join-game arm to write: on those protocols the server never sends this registry, and <c>JavaGameData</c>'s generated identity table serves 477-766 instead.</para>
/// <para>What is read is deliberately only the identity plus <c>max_level</c>. A holder id on the wire is meaningless without the name-to-id mapping and means everything with it, which is the whole reason this registry has to be installed at all; the remaining definition fields (description, supported items, weights, costs, slots, effects) have no consumer here and modelling them would only move the drop one layer up.</para>
/// <para>From 1.20.5 a server that agreed a known-pack set may send an entry with NO element (<see cref="PackedRegistryEntry.Data"/> null), meaning "you already have this one from a pack you told me you know". Such an entry KEEPS its slot: the id is positional, so dropping one would shift every id after it and silently rename every enchantment past the gap. It falls back to <see cref="EnchantmentDefinition"/>'s declared default rather than to an invented level.</para>
/// </remarks>
internal static class Enchantments
{
    /// <summary>The top-level element key carrying the maximum enchantment level.</summary>
    private const string MaxLevelKey = "max_level";

    /// <summary>Builds an enchantment registry from one configuration-phase <c>registry_data</c> packet's entries. Network ids are the entry order, which is how vanilla assigns them. Returns null when nothing usable was present, so an empty packet leaves the existing table alone.</summary>
    public static Registry<EnchantmentDefinition>? FromPackedEntries(IReadOnlyList<PackedRegistryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment, entries.Count);
        for (int id = 0; id < entries.Count; id++)
        {
            PackedRegistryEntry entry = entries[id];
            builder.Add(id, entry.Id, ReadElement(entry.Data));
        }

        return builder.Count == 0 ? null : builder.Build();
    }

    /// <summary>Reads the maximum level from one enchantment definition compound, or <see cref="EnchantmentDefinition"/>'s default when the element is absent or does not carry one.</summary>
    private static EnchantmentDefinition ReadElement(NbtTag? tag) =>
        tag is NbtCompound element && element.TryGet(MaxLevelKey, out NbtNumeric? maxLevel)
            ? new EnchantmentDefinition(maxLevel.AsInt)
            : new EnchantmentDefinition();
}
