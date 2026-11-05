using Umpk.Data.Java;
using Umpk.Game.Registries;

namespace Umpk.Client.Internal;

/// <summary>The native connection-owned feature set. Default flags are vanilla; a packet replaces the set, while world and configuration transitions retain it.</summary>
internal sealed class EnabledFeatureState
{
    private static readonly Identifier Vanilla = Identifier.Minecraft("vanilla");
    private HashSet<Identifier> _enabled = [Vanilla];

    public void Install(IReadOnlyList<Identifier> features, int protocol) =>
        _enabled = features.Where(feature => IsNativeFlag(feature, protocol)).ToHashSet();

    public void Reset() => _enabled = [Vanilla];

    public bool? IsUnobservedItemEnabled(RegistryAccess? registries, int protocol)
    {
        if (protocol is < 761 or > 777)
            return true;
        if (registries is null)
            return null;
        var native = JavaGameData.Registries(protocol);
        if (!ReferenceEquals(registries.Items, native.Items))
            foreach (var item in registries.Items)
                if (!native.Items.TryGet(item.Id, out var known) || item.NetworkId != known.NetworkId || item.Value != known.Value)
                    return null;
        // Every possible registered item is enabled only when all source-bound native requirements are present. An absent requirement cannot prove rejection without observing the held stack, which could be empty.
        string? additional = protocol is 761 or 762 ? "update_1_20" : protocol is 765 or 766 ? "update_1_21" : protocol == 768 ? "winter_drop" : null;
        return _enabled.Contains(Vanilla) && (additional is null || _enabled.Contains(Identifier.Minecraft(additional))) ? true : null;
    }

    public bool? IsBlockEnabled(Identifier block, int protocol) => IsEnabled(block, protocol, item: false);
    public bool? IsItemEnabled(Identifier item, int protocol) => IsEnabled(item, protocol, item: true);

    private bool? IsEnabled(Identifier id, int protocol, bool item)
    {
        // Earlier clients have no feature gate. Source-bound feature permission remains independent of an unknown data-driven interaction result.
        if (protocol is < 761 or > 777)
            return true;
        if (!id.IsMinecraft)
            return null;
        var native = JavaGameData.Registries(protocol);
        if (item ? !native.Items.TryGet(id, out _) : !native.Blocks.TryGet(id, out _))
            return null;
        string required = RequiredFlag(id.Path, protocol, item);
        return _enabled.Contains(Identifier.Minecraft(required));
    }

    private static bool IsNativeFlag(Identifier feature, int protocol) => feature.IsMinecraft && (feature.Path switch
    {
        "vanilla" => true,
        "bundle" => protocol is >= 761 and <= 767,
        "update_1_20" => protocol is 761 or 762,
        "trade_rebalance" => protocol >= 764,
        "update_1_21" => protocol is 765 or 766,
        "winter_drop" => protocol == 768,
        "redstone_experiments" or "minecart_improvements" => protocol >= 768,
        _ => false,
    });

    private static string RequiredFlag(string name, int protocol, bool item)
    {
        // Each group is checked against every native block declaration, inherited property copy and explicit item flag across the 25 feature-bearing releases through 26.2. BlockItem inherits the block requirement; SpawnEggItem inherits its registered entity type.
        if (protocol is 761 or 762)
        {
            if (name.StartsWith("bamboo_", StringComparison.Ordinal) && name != "bamboo_sapling" || name == "stripped_bamboo_block" || name.EndsWith("_hanging_sign", StringComparison.Ordinal) || name is "chiseled_bookshelf" or "piglin_head" or "piglin_wall_head"
                || protocol == 762 && (name.StartsWith("cherry_", StringComparison.Ordinal) || name.StartsWith("stripped_cherry_", StringComparison.Ordinal) || name is "potted_cherry_sapling" or "suspicious_sand" or "torchflower" or "torchflower_crop" or "potted_torchflower" or "pink_petals" or "decorated_pot")
                || item && (name == "camel_spawn_egg" || protocol == 762 && (name is "sniffer_spawn_egg" or "brush" || name.StartsWith("pottery_shard_", StringComparison.Ordinal))))
                return "update_1_20";
        }
        if (protocol is 765 or 766)
        {
            ReadOnlySpan<char> copper = name;
            if (copper.StartsWith("waxed_", StringComparison.Ordinal))
                copper = copper[6..];
            int offset = copper.StartsWith("exposed_", StringComparison.Ordinal) ? 8 : copper.StartsWith("weathered_", StringComparison.Ordinal) ? 10 : copper.StartsWith("oxidized_", StringComparison.Ordinal) ? 9 : 0;
            copper = copper[offset..];
            if (copper is "chiseled_copper" or "copper_door" or "copper_trapdoor" or "copper_grate" or "copper_bulb" || name.Contains("tuff", StringComparison.Ordinal) && name != "tuff" || name is "crafter" or "trial_spawner" || protocol == 766 && name is "heavy_core" or "vault"
                || item && (name is "trial_key" or "breeze_spawn_egg" || protocol == 766 && name is "bogged_spawn_egg" or "wind_charge" or "mace" or "flow_banner_pattern" or "guster_banner_pattern" or "flow_armor_trim_smithing_template" or "bolt_armor_trim_smithing_template" or "flow_pottery_sherd" or "guster_pottery_sherd" or "scrape_pottery_sherd" or "ominous_trial_key" or "ominous_bottle" or "breeze_rod"))
                return "update_1_21";
        }
        if (protocol == 768 && (name.StartsWith("pale_oak_", StringComparison.Ordinal) || name.StartsWith("stripped_pale_oak_", StringComparison.Ordinal) || name.StartsWith("pale_moss_", StringComparison.Ordinal) || name is "potted_pale_oak_sapling" or "pale_hanging_moss" or "creaking_heart" || item && name == "creaking_spawn_egg"))
            return "winter_drop";
        return "vanilla";
    }
}
