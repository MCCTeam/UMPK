using System.Globalization;
using System.Text.Json;

namespace Umpk.DataGen;

/// <summary>The shared block-attribute model plus the resolver that turns a version's block table into per-block attributes: physics scalars, semantic flags, and (where it can be proven) the ordered VALUE domain of each block-state property.</summary>
/// <remarks>
/// Per-version block data keeps property names but not their value domains, physics scalars, or semantic categories. The shared table records those values explicitly.
/// <para>The load-bearing rule is that nothing is emitted unless the arithmetic proves it. Vanilla builds a block's state table as a cartesian product over its properties sorted by name with the LAST property varying fastest, so the product of the domain sizes must equal the block's own state count. The resolver enumerates every candidate assignment and accepts only when EXACTLY ONE reproduces that count; zero or several means the block reports its property names and no values, which is an honest "unknown" rather than a plausible-looking guess.</para>
/// </remarks>
internal sealed class BlockAttributeTable
{
    public required IReadOnlySet<string> Air { get; init; }
    public required IReadOnlySet<string> Fluid { get; init; }

    /// <summary>Blocks whose <c>getFluidState</c> is an UNCONDITIONAL water source rather than the usual <c>WATERLOGGED ? water : super</c> conditional, so every one of their states contains water and none of them can say so through a property.</summary>
    /// <remarks>
    /// Exactly five block families behave this way in 1.14.4, 1.21.11, and 26.2: kelp, kelp plants, seagrass, tall seagrass, and bubble columns. A bubble column is deliberately absent from the configured list.
    /// <para>These blocks cannot carry the <c>waterlogged</c> property because they reject placed liquids instead of using the ordinary waterlogged-block behavior. Consequently, <see cref="ResolveBlockAttributes"/> cannot discover them through the property path.</para>
    /// </remarks>
    public required IReadOnlySet<string> IntrinsicallyWaterlogged { get; init; }
    public required IReadOnlySet<string> Climbable { get; init; }
    public required IReadOnlySet<string> Replaceable { get; init; }

    /// <summary>The pre-flattening mirror of <see cref="Climbable"/>, under the identifiers the 1.8-1.12.2 registries use. See <see cref="LegacyReplaceable"/> for why these are era-scoped.</summary>
    public required IReadOnlySet<string> LegacyClimbable { get; init; }

    /// <summary>The pre-flattening mirror of <see cref="Replaceable"/>. Three of those names denote a DIFFERENT block in each era (before the flattening <c>minecraft:grass</c> is the grass BLOCK and <c>minecraft:snow</c> is the snow BLOCK; the short plant is <c>tallgrass</c> and the layer is <c>snow_layer</c>), so one flat list cannot serve both without telling a caller that a solid block can be built over.</summary>
    public required IReadOnlySet<string> LegacyReplaceable { get; init; }

    /// <summary>The climbable set for one era.</summary>
    public IReadOnlySet<string> ClimbableFor(bool legacy) => legacy ? LegacyClimbable : Climbable;

    /// <summary>The placement-replaceable set for one era.</summary>
    public IReadOnlySet<string> ReplaceableFor(bool legacy) => legacy ? LegacyReplaceable : Replaceable;

    public required IReadOnlyDictionary<string, double> Friction { get; init; }
    public required IReadOnlyDictionary<string, double> SpeedFactor { get; init; }
    public required IReadOnlyDictionary<string, double> JumpFactor { get; init; }
    public required IReadOnlyDictionary<string, PropertySpec> Properties { get; init; }
    public required IReadOnlyList<PropertyOverride> Overrides { get; init; }
    public required IReadOnlyList<StateDecodeCheck> StateDecodeChecks { get; init; }

    /// <summary>One property's candidate value domains. An enum or boolean property has one or more explicit ordered lists; an integer property has zero or more fixed (min, count) ranges and optionally an "open" form whose count is solved from the block's state count.</summary>
    internal sealed record PropertySpec(
        IReadOnlyList<IReadOnlyList<string>> Explicit,
        IReadOnlyList<(int Min, int Count)> Ranges,
        int? OpenMin);

    /// <summary>A per-block domain that wins over the property's own candidates. Globs allow a single trailing or leading <c>*</c>.</summary>
    internal sealed record PropertyOverride(IReadOnlyList<string> BlockPatterns, string Property, IReadOnlyList<string> Values)
    {
        public bool Matches(string blockName)
        {
            foreach (string pattern in BlockPatterns)
            {
                if (pattern.StartsWith('*') && blockName.EndsWith(pattern[1..], StringComparison.Ordinal))
                    return true;

                if (pattern.EndsWith('*') && blockName.StartsWith(pattern[..^1], StringComparison.Ordinal))
                    return true;

                if (string.Equals(pattern, blockName, StringComparison.Ordinal))
                    return true;

            }

            return false;
        }
    }

    /// <summary>A pinned decode of ONE state of one block, checked by the validator on every band that has the block and falls inside the optional protocol window. The window exists because vanilla has changed a default: the structure block defaulted to <c>save</c> through 1.16 and to <c>load</c> from 1.17, so an unscoped assertion would be false on one era or the other.</summary>
    /// <remarks><see cref="StateOffset"/> is what makes this able to see a permuted domain at all. Checking only DEFAULT states cannot: every one of vanilla's 255 four-valued <c>facing</c> blocks defaults to <c>north</c>, which is index 0 in the right order AND in the wrong one, so the horizontal-facing domain sat permuted behind seven green facing checks. A null offset means the block's own default state; a value is an offset from <c>MinState</c>.</remarks>
    internal sealed record StateDecodeCheck(
        string Block,
        IReadOnlyDictionary<string, string> Values,
        int MinProtocol,
        int MaxProtocol,
        int? StateOffset);

    public static BlockAttributeTable Load(SharedData shared)
    {
        JsonElement root = shared.RawFiles.TryGetValue("block-attributes.json", out JsonElement el)
            ? el
            : throw new DatasetException("shared/block-attributes.json is missing");
        JsonElement flags = shared.RawFiles.TryGetValue("curated-flags.json", out JsonElement f)
            ? f
            : throw new DatasetException("shared/curated-flags.json is missing");

        // The pre-flattening mirror is required, not optional: a missing block would silently fall back to an empty set and drop every legacy semantic flag without a single diagnostic.
        JsonElement legacy = flags.TryGetProperty("pre_flattening", out JsonElement pf)
            && pf.ValueKind == JsonValueKind.Object
            ? pf
            : throw new DatasetException("shared/curated-flags.json is missing the pre_flattening block");

        return new BlockAttributeTable
        {
            Air = StringSet(root, "air"),
            Fluid = StringSet(root, "fluid"),
            IntrinsicallyWaterlogged = StringSet(root, "intrinsically_waterlogged"),
            Climbable = StringSet(flags, "climbable"),
            Replaceable = StringSet(flags, "replaceable_by_placement"),
            LegacyClimbable = StringSet(legacy, "climbable"),
            LegacyReplaceable = StringSet(legacy, "replaceable_by_placement"),
            Friction = Scalars(root, "friction"),
            SpeedFactor = Scalars(root, "speed_factor"),
            JumpFactor = Scalars(root, "jump_factor"),
            Properties = LoadProperties(root),
            Overrides = LoadOverrides(root),
            StateDecodeChecks = LoadChecks(root),
        };
    }

    /// <summary>Resolves one block's property domains. Returns null when the arithmetic does not single out one assignment, which the caller records as "names known, values unknown".</summary>
    public IReadOnlyList<IReadOnlyList<string>>? ResolveDomains(string blockName, IReadOnlyList<string> propertyNames, int stateCount)
    {
        if (propertyNames.Count == 0 || stateCount <= 0)
            return null;

        var candidates = new List<IReadOnlyList<IReadOnlyList<string>>>(propertyNames.Count);
        int openCount = 0;
        int openIndex = -1;
        int openMin = 0;
        foreach (string name in propertyNames)
        {
            IReadOnlyList<string>? forced = OverrideFor(blockName, name);
            if (forced is not null)
            {
                candidates.Add([forced]);
                continue;
            }

            if (!Properties.TryGetValue(name, out PropertySpec? spec))
            {
                return null; // A property this table does not describe; refuse to guess the whole block.
            }

            List<IReadOnlyList<string>> options = [.. spec.Explicit];
            foreach ((int min, int count) in spec.Ranges)
                options.Add(Numbers(min, count));

            if (spec.OpenMin is int min2)
            {
                openCount++;
                openIndex = candidates.Count;
                openMin = min2;
            }

            if (options.Count == 0 && spec.OpenMin is null)
                return null;

            candidates.Add(options);
        }

        if (openCount > 1)
        {
            return null; // Two unknown sizes cannot be separated by one product.
        }

        IReadOnlyList<IReadOnlyList<string>>? solution = null;
        var choice = new IReadOnlyList<string>[propertyNames.Count];
        int solutions = 0;

        void Search(int index, long product)
        {
            if (solutions > 1)
                return;

            if (index == candidates.Count)
            {
                if (openIndex < 0)
                {
                    if (product == stateCount)
                    {
                        solutions++;
                        solution = [.. choice];
                    }

                    return;
                }

                if (product <= 0 || stateCount % product != 0)
                    return;

                int size = (int)(stateCount / product);
                if (size < 1)
                    return;

                choice[openIndex] = Numbers(openMin, size);
                solutions++;
                solution = [.. choice];
                return;
            }

            if (index == openIndex)
            {
                // The open property contributes its size at the end, once the rest is known.
                Search(index + 1, product);
                return;
            }

            foreach (IReadOnlyList<string> option in candidates[index])
            {
                long next = product * option.Count;
                if (next > stateCount)
                    continue;

                choice[index] = option;
                Search(index + 1, next);
                if (solutions > 1)
                    return;

            }
        }

        Search(0, 1);
        return solutions == 1 ? solution : null;
    }

    /// <summary>Decomposes a state offset into per-property values, using the vanilla odometer: properties in the given (name-sorted) order with the LAST varying fastest.</summary>
    public static IReadOnlyList<string> Decompose(IReadOnlyList<IReadOnlyList<string>> domains, int offset)
    {
        var values = new string[domains.Count];
        int remaining = offset;
        for (int i = domains.Count - 1; i >= 0; i--)
        {
            int size = domains[i].Count;
            values[i] = domains[i][remaining % size];
            remaining /= size;
        }

        return values;
    }

    private IReadOnlyList<string>? OverrideFor(string blockName, string property)
    {
        foreach (PropertyOverride rule in Overrides)
            if (string.Equals(rule.Property, property, StringComparison.Ordinal) && rule.Matches(blockName))
                return rule.Values;

        return null;
    }

    private static IReadOnlyList<string> Numbers(int min, int count)
    {
        var values = new string[count];
        for (int i = 0; i < count; i++)
            values[i] = (min + i).ToString(CultureInfo.InvariantCulture);

        return values;
    }

    private static IReadOnlySet<string> StringSet(JsonElement root, string name)
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Array)
            foreach (JsonElement s in el.EnumerateArray())
                if (s.GetString() is { Length: > 0 } value)
                    set.Add(Qualify(value));

        return set;
    }

    private static IReadOnlyDictionary<string, double> Scalars(JsonElement root, string name)
    {
        Dictionary<string, double> map = new(StringComparer.Ordinal);
        if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty p in el.EnumerateObject())
                map[Qualify(p.Name)] = p.Value.GetDouble();

        return map;
    }

    private static IReadOnlyDictionary<string, PropertySpec> LoadProperties(JsonElement root)
    {
        Dictionary<string, PropertySpec> specs = new(StringComparer.Ordinal);
        if (!root.TryGetProperty("properties", out JsonElement el))
            return specs;

        foreach (JsonProperty p in el.EnumerateObject())
        {
            string kind = p.Value.GetProperty("kind").GetString() ?? "";
            List<IReadOnlyList<string>> explicitLists = [];
            List<(int, int)> ranges = [];
            int? openMin = null;

            switch (kind)
            {
                case "bool":
                    // Boolean property domains use true then false. A "false, true" reading would shift every waterlogged/powered/lit state by one.
                    explicitLists.Add(["true", "false"]);
                    break;

                case "enum":
                    foreach (JsonElement c in p.Value.GetProperty("candidates").EnumerateArray())
                    {
                        List<string> values = [];
                        foreach (JsonElement v in c.EnumerateArray())
                            values.Add(v.GetString()!);

                        explicitLists.Add(values);
                    }

                    break;

                case "int":
                    if (p.Value.TryGetProperty("ranges", out JsonElement rangesEl))
                        foreach (JsonElement r in rangesEl.EnumerateArray())
                            ranges.Add((r[0].GetInt32(), r[1].GetInt32()));

                    if (p.Value.TryGetProperty("open", out JsonElement openEl))
                        openMin = openEl.GetProperty("min").GetInt32();

                    break;

                default:
                    throw new DatasetException($"block-attributes.json: property '{p.Name}' has unknown kind '{kind}'");
            }

            specs[p.Name] = new PropertySpec(explicitLists, ranges, openMin);
        }

        return specs;
    }

    private static IReadOnlyList<PropertyOverride> LoadOverrides(JsonElement root)
    {
        List<PropertyOverride> rules = [];
        if (!root.TryGetProperty("property_overrides", out JsonElement el))
            return rules;

        foreach (JsonElement r in el.EnumerateArray())
        {
            List<string> blocks = [];
            foreach (JsonElement b in r.GetProperty("blocks").EnumerateArray())
            {
                // Glob patterns are matched against the qualified name but must NOT be qualified themselves: "minecraft:*_door" is neither a prefix nor a suffix pattern.
                string pattern = b.GetString()!;
                blocks.Add(pattern.Contains('*', StringComparison.Ordinal) ? pattern : Qualify(pattern));
            }

            List<string> values = [];
            foreach (JsonElement v in r.GetProperty("values").EnumerateArray())
                values.Add(v.GetString()!);

            rules.Add(new PropertyOverride(blocks, r.GetProperty("property").GetString()!, values));
        }

        return rules;
    }

    private static IReadOnlyList<StateDecodeCheck> LoadChecks(JsonElement root)
    {
        List<StateDecodeCheck> checks = [];
        if (!root.TryGetProperty("default_state_checks", out JsonElement el))
            return checks;

        foreach (JsonElement c in el.EnumerateArray())
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach (JsonProperty v in c.GetProperty("values").EnumerateObject())
                values[v.Name] = v.Value.GetString()!;

            int minProtocol = c.TryGetProperty("min_protocol", out JsonElement lo) ? lo.GetInt32() : int.MinValue;
            int maxProtocol = c.TryGetProperty("max_protocol", out JsonElement hi) ? hi.GetInt32() : int.MaxValue;
            int? offset = c.TryGetProperty("state_offset", out JsonElement off) ? off.GetInt32() : null;
            checks.Add(new StateDecodeCheck(
                Qualify(c.GetProperty("block").GetString()!), values, minProtocol, maxProtocol, offset));
        }

        return checks;
    }

    /// <summary>Normalizes a bare name to the <c>minecraft:</c> namespace so curated lists and datasets compare.</summary>
    private static string Qualify(string name)
        => name.Contains(':', StringComparison.Ordinal) ? name : "minecraft:" + name;
}
