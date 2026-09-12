using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Game.Items;

/// <summary>Bridges pre-1.20.5 item NBT to and from the unified <see cref="DataComponentMap"/>. Reading maps the well-known keys (<c>display.Name</c> to custom name, <c>display.Lore</c> to lore, <c>ench</c>/<c>Enchantments</c>/<c>StoredEnchantments</c> to enchantment components with the per-era numeric-id table, <c>Damage</c>, <c>Unbreakable</c>, <c>RepairCost</c>) and preserves every unrecognized member in a <see cref="LegacyNbtComponent"/> so re-encoding is byte-faithful. The per-era tables come from an <see cref="ILegacyItemBridgeSource"/> seam so the data lives in the dataset, not in this class.</summary>
public sealed class LegacyItemBridge
{
    private readonly ILegacyItemBridgeSource _source;
    private readonly Registry<EnchantmentDefinition> _enchantments;

    /// <summary>Creates a bridge over a per-era table source and the session's enchantment registry.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public LegacyItemBridge(ILegacyItemBridgeSource source, Registry<EnchantmentDefinition> enchantments)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(enchantments);
        _source = source;
        _enchantments = enchantments;
    }

    /// <summary>Converts a legacy item's root NBT compound into a component patch list plus a residual NBT compound holding everything not mapped. The residual is empty when every member was recognized. The caller (the Java item codec) forms a <see cref="DataComponentMap"/> from these against the item's prototype.</summary>
    /// <param name="era">The era key (e.g. <c>V1_8</c>) selecting the per-era table.</param>
    /// <param name="root">The item's root NBT compound (the old <c>tag</c> payload).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public LegacyBridgeResult ToComponents(string era, NbtCompound root)
    {
        ArgumentNullException.ThrowIfNull(era);
        ArgumentNullException.ThrowIfNull(root);

        var patch = new List<DataComponentEntry>();
        var residual = new NbtCompound();

        foreach (KeyValuePair<string, NbtTag> member in root)
        {
            switch (member.Key)
            {
                case "Damage" when member.Value is NbtNumeric damage:
                    patch.Add(DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(damage.AsInt)));
                    break;

                case "RepairCost" when member.Value is NbtNumeric repair:
                    patch.Add(DataComponentEntry.Set(DataComponents.RepairCost, new RepairCostComponent(repair.AsInt)));
                    break;

                case "Unbreakable" when member.Value is NbtNumeric unbreakable && unbreakable.AsInt != 0:
                    patch.Add(DataComponentEntry.Set(DataComponents.Unbreakable, new UnbreakableComponent()));
                    break;

                case "display" when member.Value is NbtCompound display:
                    MapDisplay(era, display, patch, residual);
                    break;

                case "ench" when member.Value is NbtList ench:
                    patch.Add(DataComponentEntry.Set(
                        DataComponents.Enchantments,
                        new EnchantmentsComponent(ReadEnchantments(era, ench))));
                    break;

                case "Enchantments" when member.Value is NbtList ench:
                    patch.Add(DataComponentEntry.Set(
                        DataComponents.Enchantments,
                        new EnchantmentsComponent(ReadEnchantments(era, ench))));
                    break;

                case "StoredEnchantments" when member.Value is NbtList stored:
                    patch.Add(DataComponentEntry.Set(
                        DataComponents.StoredEnchantments,
                        new StoredEnchantmentsComponent(ReadEnchantments(era, stored))));
                    break;

                default:
                    // Retain verbatim, preserving order for byte-faithful re-encode.
                    residual.Put(member.Key, member.Value.Copy());
                    break;
            }
        }

        if (!residual.IsEmpty)
            patch.Add(DataComponentEntry.Set(DataComponents.LegacyNbt, new LegacyNbtComponent(residual)));

        return new LegacyBridgeResult(patch);
    }

    /// <summary>Rebuilds a legacy root NBT compound from a component map's effective view, reversing <see cref="ToComponents"/>. The residual <see cref="LegacyNbtComponent"/> members are written first so their positions survive, then the well-known keys are re-derived; this reproduces the original bytes for a map that was itself produced by <see cref="ToComponents"/>.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public NbtCompound ToLegacyNbt(string era, DataComponentMap components)
    {
        ArgumentNullException.ThrowIfNull(era);
        ArgumentNullException.ThrowIfNull(components);

        var root = new NbtCompound();

        // Residual members first, in their preserved order.
        if (components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? residual))
            foreach (KeyValuePair<string, NbtTag> member in residual.Nbt)
                root.Put(member.Key, member.Value.Copy());

        if (components.TryGet(DataComponents.Damage, out DamageComponent? damage))
            root.PutInt("Damage", damage.Value);

        if (components.TryGet(DataComponents.RepairCost, out RepairCostComponent? repair))
            root.PutInt("RepairCost", repair.Value);

        if (components.Has(DataComponents.Unbreakable))
            root.PutBool("Unbreakable", true);

        WriteDisplay(root, components);
        WriteEnchantments(era, root, "ench", components.Get(DataComponents.Enchantments)?.Enchantments);
        WriteEnchantments(era, root, "StoredEnchantments", components.Get(DataComponents.StoredEnchantments)?.Enchantments);

        return root;
    }

    private void MapDisplay(string era, NbtCompound display, List<DataComponentEntry> patch, NbtCompound residual)
    {
        var leftover = new NbtCompound();
        foreach (KeyValuePair<string, NbtTag> member in display)
            if (member.Key == "Name" && member.Value is NbtString name &&
                _source.TryMapNbtKey(era, "display.Name", out _))
                patch.Add(DataComponentEntry.Set(DataComponents.CustomName, new CustomNameComponent(Component.Text(name.Value))));

            else if (member.Key == "Lore" && member.Value is NbtList lore &&
                     _source.TryMapNbtKey(era, "display.Lore", out _))
            {
                var lines = new List<Component>(lore.Count);
                foreach (NbtTag line in lore)
                    lines.Add(Component.Text(line is NbtString s ? s.Value : string.Empty));

                patch.Add(DataComponentEntry.Set(DataComponents.Lore, new LoreComponent(lines)));
            }
            else
                leftover.Put(member.Key, member.Value.Copy());

        if (!leftover.IsEmpty)
        {
            // Preserve unmapped display members (e.g. color) under a residual "display" so re-encode is faithful.
            residual.Put("display", leftover);
        }
    }

    private static void WriteDisplay(NbtCompound root, DataComponentMap components)
    {
        Component? name = components.Get(DataComponents.CustomName)?.Name;
        IReadOnlyList<Component>? lore = components.Get(DataComponents.Lore)?.Lines;
        if (name is null && lore is null)
            return;

        // Merge into any residual display already written.
        NbtCompound display = root.GetCompound("display") ?? new NbtCompound();
        if (name is not null)
            display.PutString("Name", name.ToPlainText());

        if (lore is not null)
        {
            var list = new NbtList(NbtTagType.String);
            foreach (Component line in lore)
                list.Add(new NbtString(line.ToPlainText()));

            display.Put("Lore", list);
        }

        root.Put("display", display);
    }

    private IReadOnlyList<EnchantmentInstance> ReadEnchantments(string era, NbtList list)
    {
        var result = new List<EnchantmentInstance>(list.Count);
        foreach (NbtTag element in list)
        {
            if (element is not NbtCompound entry)
                continue;

            int level = entry.GetShort("lvl");
            RegistryEntry<EnchantmentDefinition> handle = ResolveEnchantment(era, entry);
            if (!handle.IsDefault)
                result.Add(new EnchantmentInstance(handle, level));

        }

        return result;
    }

    private RegistryEntry<EnchantmentDefinition> ResolveEnchantment(string era, NbtCompound entry)
    {
        // Newer legacy eras (1.13+) already store a namespaced id string; older ones store a numeric id.
        if (entry.TryGet("id", out NbtString? idString) && Identifier.TryParse(idString.Value, out Identifier byName)
            && _enchantments.TryGet(byName, out RegistryEntry<EnchantmentDefinition> named))
            return named;

        if (entry.TryGet("id", out NbtNumeric? idNumeric)
            && _source.TryMapEnchantmentId(era, idNumeric.AsInt, out Identifier mapped)
            && _enchantments.TryGet(mapped, out RegistryEntry<EnchantmentDefinition> byId))
            return byId;

        return default;
    }

    private void WriteEnchantments(string era, NbtCompound root, string key, IReadOnlyList<EnchantmentInstance>? enchantments)
    {
        if (enchantments is null || enchantments.Count == 0)
            return;

        var list = new NbtList(NbtTagType.Compound);
        foreach (EnchantmentInstance instance in enchantments)
        {
            var entry = new NbtCompound();
            if (TryFindNumericId(era, instance.Enchantment.Id, out int numericId))
                entry.PutShort("id", (short)numericId);

            else
                entry.PutString("id", instance.Enchantment.Id.ToString());

            entry.PutShort("lvl", (short)instance.Level);
            list.Add(entry);
        }

        root.Put(key, list);
    }

    private bool TryFindNumericId(string era, Identifier enchantmentId, out int numericId)
    {
        // The seam is keyed numeric -> id; the inverse is a small scan bounded by the legacy id range.
        for (int candidate = 0; candidate <= 255; candidate++)
            if (_source.TryMapEnchantmentId(era, candidate, out Identifier mapped) && mapped.Equals(enchantmentId))
            {
                numericId = candidate;
                return true;
            }

        numericId = 0;
        return false;
    }
}

/// <summary>The result of a legacy-to-component conversion: the patch entries to apply against the item prototype.</summary>
/// <param name="Patch">The component patch, including any residual <see cref="LegacyNbtComponent"/>.</param>
public sealed record LegacyBridgeResult(IReadOnlyList<DataComponentEntry> Patch);
