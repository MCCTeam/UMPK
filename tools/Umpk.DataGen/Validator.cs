using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Umpk.DataGen;

/// <summary>Enforces the dataset invariants. Returns the full list of problems (empty == valid) so <c>verify</c> can report every failure at once.</summary>
internal static partial class Validator
{
    /// <summary>The first protocol (1.14) with a real <c>minecraft:menu</c> registry, i.e. where open_screen starts carrying a numeric menu id instead of a window-type string. This is NOT the same boundary as the dataset's flat/legacy identity, which splits at the 1.13 flattening (protocol 393).</summary>
    private const int FirstMenuRegistryProtocol = 477;

    /// <summary>The lowest entry count any of the 49 shipped lang tables measures (protocol 47 / 1.8.9: 2553). A table this small or smaller is a truncated or wrongly-pathed extraction, not a real vanilla table; every later era only grows from here.</summary>
    private const int MinLangEntries = 2000;

    /// <summary>The first protocol (1.19) whose chat packets carry a bare message body plus a chat-type id instead of a pre-composed line, making decoration (and therefore these seven keys) the client's job. See <c>Umpk.Client.Internal.ChatTypeDecoration</c>.</summary>
    private const int FirstChatTypeProtocol = 759;

    /// <summary>The seven built-in chat-type translation keys vanilla's client decorates with.</summary>
    private static readonly string[] ChatTypeKeys =
    [
        "chat.type.text",
        "chat.type.announcement",
        "chat.type.emote",
        "chat.type.team.text",
        "chat.type.team.sent",
        "commands.message.display.incoming",
        "commands.message.display.outgoing",
    ];

    // \G anchors the match to where the caller starts searching, so each pass validates one '%'
    // group instead of finding a later matching '%' elsewhere in the string.
    [GeneratedRegex(@"\G%(?:(\d+)\$)?([A-Za-z%]|$)")]
    private static partial Regex FormatGroupPattern();

    /// <summary>
    /// Keys that intentionally do not satisfy <see cref="FormatGroupPattern"/>:
    /// <list type="bullet">
    /// <item><c>commands.debug.stop</c>, <c>commands.generic.double.tooBig</c>,
    /// <c>commands.generic.double.tooSmall</c> (protocols 47-210 only; later eras rephrase them without a bare float specifier) use <c>%.2f</c>. They are substituted before transmission, so the translatable format pattern does not apply.</item>
    /// <item><c>translation.test.invalid2</c> = <c>"hi %  s"</c>, present unchanged on every protocol.
    /// This is vanilla's OWN deliberately-broken fixture for exercising the translation format parser's error path (alongside the deliberately-VALID <c>translation.test.invalid</c> = "hi %", which parses fine here because a bare trailing '%' matches this pattern's end-of-string branch). It is meant to fail parsing; a real client never resolves it as a chat argument list.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> LangFormatCheckExemptKeys = new(StringComparer.Ordinal)
    {
        "commands.debug.stop",
        "commands.generic.double.tooBig",
        "commands.generic.double.tooSmall",
        "translation.test.invalid2",
    };

    public static IReadOnlyList<string> Validate(Dataset dataset)
    {
        List<string> problems = [];

        // The era vocabulary is the dataset's own codec-eras manifest (packet + payload order). This is exactly the set of era-member keys PacketRegistrar in Umpk.Protocol.Java resolves per packet family. Driving the vocabulary from the manifest means adding or removing an era member is tracked automatically. A key being in the vocabulary does NOT assert the codec is implemented for every packet: PacketRegistrar registers a NotImplemented marker for families it has not implemented yet, which is legal.
        HashSet<string> knownCodecKeys =
        [
            .. dataset.Shared.CodecEras.PacketOrder,
            .. dataset.Shared.CodecEras.PayloadOrder,
        ];

        ValidateCatalogContiguity(dataset, problems);
        ValidateProvenance(dataset, problems);

        foreach (VersionData version in dataset.ByProtocol.Values.OrderBy(v => v.Protocol))
        {
            bool legacy = version.Identity == "legacy";
            ValidatePacketCodecs(version, knownCodecKeys, problems);
            ValidateRegistryGapless(version, legacy, problems);
            ValidateItems(version, legacy, problems);
            ValidateBlocks(version, legacy, problems);
            ValidateBlockPropertyDomains(version, dataset.Shared.BlockAttributes, problems);
            ValidateShapeRefs(version, legacy, problems);
            ValidateBlockPush(version, legacy, problems);
            ValidateMetadata(version, problems);
            ValidateComponents(version, legacy, problems);
            ValidateProjectedRegistryOrder(version, problems);
            ValidateMetadataKeys(version, problems);
            ValidateMenus(version, problems);
            ValidateLang(version, problems);
            ValidateAttributeDefaults(version, dataset.Shared.AttributeDefaults, problems);
        }

        ValidateCodecEras(dataset, problems);
        ValidateCodecEraAppropriateness(dataset, problems);
        return problems;
    }

    /// <summary>Pins the decode of a handful of named block states against values known from vanilla.</summary>
    /// <remarks>
    /// <para>The resolver's own safety net is arithmetic: a candidate assignment is accepted only when it is the unique one reproducing the block's state count. That catches a wrong domain SIZE but is blind to a wrong domain ORDER, and order is exactly where a plausible-looking lie would come from (booleans are ordered true-then-false, and <c>facing</c> is not in Direction's own enum order). These checks close that hole: they decode a state whose vanilla property values are known independently, and fail the dataset if any value disagrees. A block the band does not have, or whose domains did not resolve, is skipped rather than failed: the 1.13 datasets carry no property names at all, and the checks are about correctness of what IS emitted, not about coverage.</para>
    /// <para>Checking only DEFAULT states did NOT close it. Every one of vanilla's 255 four-valued <c>facing</c> blocks defaults to <c>north</c>, index 0 in both the right order and the wrong one, so seven green facing checks sat on top of a permuted horizontal-facing domain. A check may therefore name a <c>state_offset</c> from the block's MinState instead.</para>
    /// </remarks>
    private static void ValidateBlockPropertyDomains(VersionData version, BlockAttributeTable attributes, List<string> problems)
    {
        Dictionary<string, BlockEntry> byName = new(StringComparer.Ordinal);
        foreach (BlockEntry entry in version.Blocks.Blocks)
            if (entry.Name is { Length: > 0 } name)
                byName[name.Contains(':', StringComparison.Ordinal) ? name : "minecraft:" + name] = entry;

        foreach (BlockAttributeTable.StateDecodeCheck check in attributes.StateDecodeChecks)
        {
            if (version.Protocol < check.MinProtocol
                || version.Protocol > check.MaxProtocol
                || !byName.TryGetValue(check.Block, out BlockEntry? entry)
                || entry.Properties is not { Count: > 0 } names
                || entry.MinState is not int min
                || entry.MaxState is not int max
                || entry.DefaultState is not int def)
                continue;

            IReadOnlyList<IReadOnlyList<string>>? domains =
                attributes.ResolveDomains(check.Block, names, (max - min) + 1);
            if (domains is null)
                continue;

            int offset = check.StateOffset ?? (def - min);
            if (offset < 0 || offset > max - min)
            {
                problems.Add(
                    $"proto {version.Protocol}: {check.Block} state check offset {offset} is outside its state range");
                continue;
            }

            IReadOnlyList<string> values = BlockAttributeTable.Decompose(domains, offset);
            foreach ((string property, string expected) in check.Values)
            {
                int index = -1;
                for (int i = 0; i < names.Count; i++)
                    if (names[i] == property)
                    {
                        index = i;
                        break;
                    }

                if (index < 0)
                {
                    problems.Add($"proto {version.Protocol}: {check.Block} has no '{property}' property, but a default-state check names one");
                    continue;
                }

                if (values[index] != expected)
                {
                    string which = check.StateOffset is int named
                        ? $"state +{named}"
                        : "default state";
                    problems.Add(
                        $"proto {version.Protocol}: {check.Block} {which} decodes '{property}' as '{values[index]}', expected '{expected}'");
                }
            }
        }
    }

    /// <summary>Every RAW attribute name in every protocol's <c>minecraft:attribute</c> identity list must have a row in the curated <c>shared/attribute-defaults.json</c>, and that row's default must lie inside its own range.</summary>
    /// <remarks>
    /// <para>The assertion is over RAW names, deliberately. If it asserted over canonicalised ones it would pass while the emitter wrote the wrong numbers: <c>minecraft:horse.jump_strength</c> (0.7 in [0,2]) and <c>minecraft:generic.jump_strength</c> (0.42 in [0,32]) collapse to the same canonical key with different values, so a canonical gate would be satisfied by whichever of the two happened to be in the table. Raw keys make an era collision a build failure instead.</para>
    /// <para>A protocol that adds an attribute without a definition row fails here rather than emitting a zero-filled definition that would clamp that attribute to zero forever.</para>
    /// </remarks>
    private static void ValidateAttributeDefaults(
        VersionData version, AttributeDefaultTable table, List<string> problems)
    {
        if (!version.Registries.TryGetValue("minecraft:attribute", out IReadOnlyList<RegistryItem>? entries))
            return;

        foreach (RegistryItem entry in entries)
        {
            if (!table.Rows.TryGetValue(entry.Name, out AttributeDefaultRow row))
            {
                problems.Add(
                    $"proto {version.Protocol}: attribute '{entry.Name}' has no row in "
                    + "shared/attribute-defaults.json (add its vanilla RangedAttribute default/min/max)");
                continue;
            }

            if (row.Min > row.Max)
                problems.Add($"shared/attribute-defaults.json: '{entry.Name}' has min {row.Min} above max {row.Max}");

            else if (row.Default < row.Min || row.Default > row.Max)
                problems.Add(
                    $"shared/attribute-defaults.json: '{entry.Name}' default {row.Default} is outside "
                    + $"[{row.Min}, {row.Max}]");

        }
    }

    /// <summary>Every dataset file must carry a non-empty origin marker. Every per-version file and every <c>shared/*.json</c> must declare a non-empty <c>_provenance.kind</c>.</summary>
    private static void ValidateProvenance(Dataset dataset, List<string> problems)
    {
        foreach (VersionData version in dataset.ByProtocol.Values.OrderBy(v => v.Protocol))
            foreach ((string file, JsonElement root) in version.RawFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                CheckProvenance($"proto {version.Protocol} file {file}", root, problems);

        foreach ((string file, JsonElement root) in dataset.Shared.RawFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            CheckProvenance($"shared/{file}", root, problems);

    }

    private static void CheckProvenance(string where, JsonElement root, List<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("_provenance", out JsonElement prov)
            || prov.ValueKind != JsonValueKind.Object
            || !prov.TryGetProperty("kind", out JsonElement kind)
            || kind.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(kind.GetString()))
            problems.Add($"{where}: missing or empty _provenance.kind");

    }

    private static void ValidateCatalogContiguity(Dataset dataset, List<string> problems)
    {
        if (dataset.Versions.Count == 0)
        {
            problems.Add("versions.json is empty");
            return;
        }

        HashSet<int> seen = [];
        foreach (VersionCatalogEntry entry in dataset.Versions)
        {
            if (!seen.Add(entry.Protocol) && dataset.Versions.Count(v => v.Protocol == entry.Protocol) > 1)
            {
                // N:1 name->protocol is allowed; only flag if the dataset dir differs.
            }
            if (!dataset.ByProtocol.ContainsKey(entry.Protocol))
                problems.Add($"versions.json references protocol {entry.Protocol} with no dataset directory");

        }
    }

    private static void ValidatePacketCodecs(VersionData version, HashSet<string> knownCodecKeys, List<string> problems)
    {
        foreach ((string phase, IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>> flows) in version.Packets.Phases)
        {
            foreach ((string flow, IReadOnlyList<PacketEntry> packets) in flows)
            {
                // Protocol ids must be gapless and start at 0 within a phase+flow.
                HashSet<int> ids = [];
                foreach (PacketEntry p in packets)
                {
                    if (!ids.Add(p.ProtocolId))
                        problems.Add($"proto {version.Protocol} {phase}/{flow}: duplicate packet id {p.ProtocolId}");

                    if (p.Codec is null)
                        problems.Add($"proto {version.Protocol} {phase}/{flow}: packet {p.Id} has no codec key");

                    else if (!knownCodecKeys.Contains(p.Codec))
                        problems.Add($"proto {version.Protocol} {phase}/{flow}: packet {p.Id} references unknown codec key '{p.Codec}'");

                }
                for (int i = 0; i < packets.Count; i++)
                    if (!ids.Contains(i))
                        problems.Add($"proto {version.Protocol} {phase}/{flow}: packet id table has a gap at {i}");

            }
        }
    }

    private static void ValidateRegistryGapless(VersionData version, bool legacy, List<string> problems)
    {
        foreach ((string name, IReadOnlyList<RegistryItem> items) in version.Registries)
        {
            if (items.Count == 0)
                continue;

            List<int> ids = items.Select(i => i.Id).OrderBy(i => i).ToList();
            // Legacy registries (e.g. 1.8 mob effects) are 1-based and may be sparse; only enforce uniqueness + monotonicity there. minecraft:mob_effect kept its legacy 1-based numbering (no id 0, speed == 1) through 1.20.1; it only became 0-based at 1.20.2 (protocol 764). Treat the pre-764 effect registry as 1-based-gapless rather than gapless-from-0 (wire truth).
            bool oneBasedEffects =
                name == "minecraft:mob_effect" && version.Protocol < 764;
            if (legacy)
            {
                if (ids.Distinct().Count() != ids.Count)
                    problems.Add($"proto {version.Protocol} registry {name}: duplicate ids (legacy)");

                continue;
            }
            int start = oneBasedEffects ? 1 : 0;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] != i + start)
                {
                    problems.Add($"proto {version.Protocol} registry {name}: not gapless from {start} (gap near id {i + start})");
                    break;
                }

        }
    }

    private static void ValidateItems(VersionData version, bool legacy, List<string> problems)
    {
        if (version.Items.Count == 0)
        {
            problems.Add($"proto {version.Protocol}: item registry is empty");
            return;
        }
        if (legacy)
        {
            // Composite (id<<16|damage) keys: only require uniqueness and that the base air key (0) is present, in legacy mode.
            HashSet<int> keys = [];
            foreach (RegistryItem it in version.Items)
                if (!keys.Add(it.Id))
                    problems.Add($"proto {version.Protocol}: duplicate legacy item key {it.Id}");

            if (!keys.Contains(0))
                problems.Add($"proto {version.Protocol}: legacy item table missing air (key 0)");

            return;
        }
        // Modern: gapless from 0.
        List<int> ids = version.Items.Select(i => i.Id).OrderBy(i => i).ToList();
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] != i)
            {
                problems.Add($"proto {version.Protocol}: item registry not gapless from 0 (gap near {i})");
                break;
            }

    }

    private static void ValidateBlocks(VersionData version, bool legacy, List<string> problems)
    {
        if (version.Blocks.Blocks.Count == 0)
        {
            problems.Add($"proto {version.Protocol}: block table is empty");
            return;
        }

        ValidateBlockNamesAreUnique(version, problems);

        if (legacy)
        {
            if (version.Blocks.Identity != "legacy")
                problems.Add($"proto {version.Protocol}: legacy version must declare blocks identity 'legacy'");

            // Legacy state id must equal (block_id << 4) | meta.
            foreach (BlockEntry b in version.Blocks.Blocks)
                if (b.BlockId is int bid && b.Meta is int meta && b.StateId is int sid)
                {
                    int expected = (bid << 4) | meta;
                    if (sid != expected)
                        problems.Add($"proto {version.Protocol}: block id {bid} meta {meta} has state {sid}, expected {expected} ((id<<4)|meta)");

                }
                else
                    problems.Add($"proto {version.Protocol}: legacy block entry missing block_id/meta/state_id");

            return;
        }
        // Modern: state ranges must be contiguous and non-overlapping, default in range.
        List<BlockEntry> ordered = version.Blocks.Blocks
            .Where(b => b.MinState is not null)
            .OrderBy(b => b.MinState!.Value).ToList();
        int expectedNext = 0;
        foreach (BlockEntry b in ordered)
        {
            if (b.MinState != expectedNext)
                problems.Add($"proto {version.Protocol}: block {b.Name} min_state {b.MinState} breaks contiguity (expected {expectedNext})");

            if (b.DefaultState is int def && (def < b.MinState || def > b.MaxState))
                problems.Add($"proto {version.Protocol}: block {b.Name} default_state {def} outside [{b.MinState},{b.MaxState}]");

            expectedNext = (b.MaxState ?? b.MinState ?? expectedNext) + 1;
        }
    }

    /// <summary>A block identifier must be unique within a version. This is not a tidiness rule: the runtime registry is keyed by identifier as well as by network id, and <c>JavaGameData.BuildBlocks</c> SKIPS an entry whose identifier it has already seen, so a shared name silently deletes a block id from the registry and every one of its states then resolves to <c>minecraft:air</c>. Flattened identifiers can otherwise collapse still/flowing, lit/unlit, and powered/unpowered pairs.</summary>
    private static void ValidateBlockNamesAreUnique(VersionData version, List<string> problems)
    {
        Dictionary<string, int> firstById = new(version.Blocks.Blocks.Count, StringComparer.Ordinal);
        foreach (BlockEntry b in version.Blocks.Blocks)
        {
            if (b.Name is not { Length: > 0 } name || b.BlockId is not int blockId)
                continue;

            if (firstById.TryGetValue(name, out int first))
            {
                problems.Add(
                    $"proto {version.Protocol}: block ids {first} and {blockId} share the identifier '{name}'; "
                    + "the registry keeps the first and id "
                    + $"{blockId} would resolve to air");
                continue;
            }

            firstById[name] = blockId;
        }
    }

    /// <summary>Checks a version's <c>block-push.json</c> against its own block table.</summary>
    /// <remarks>The table is emitted positionally, one row per block in block order, so the one thing that can go wrong silently is a name that does not exist on this band: it would be dropped and the block it was meant for would be emitted as NORMAL, which is a plausible-looking wrong push rather than a refusal. Absence of the file is legal and is not the same as an empty file.</remarks>
    private static void ValidateBlockPush(VersionData version, bool legacy, List<string> problems)
    {
        if (version.Push is not { } push)
            return;

        if (legacy)
        {
            problems.Add(
                $"proto {version.Protocol}: block-push.json is present on a LEGACY dataset. The piston "
                + "model is gated off the pre-flattening band, where one shape per block id cannot "
                + "answer piston_head's six facings.");
            return;
        }

        HashSet<string> known = new(StringComparer.Ordinal);
        foreach (BlockEntry block in version.Blocks.Blocks)
            if (block.Name is { Length: > 0 } name)
                _ = known.Add(name);

        foreach (string name in push.Reactions.Keys.Concat(push.Unbreakable).Concat(push.BlockEntity))
            if (!known.Contains(name))
                problems.Add(
                    $"proto {version.Protocol}: block-push.json names '{name}', which is not a block on "
                    + "this band");

        foreach (string reaction in push.Reactions.Values.Append(push.DefaultReaction))
            if (reaction is not ("NORMAL" or "DESTROY" or "BLOCK" or "IGNORE" or "PUSH_ONLY"))
                problems.Add(
                    $"proto {version.Protocol}: block-push.json uses PushReaction '{reaction}', which is "
                    + "not one of vanilla's five");

    }

    private static void ValidateShapeRefs(VersionData version, bool legacy, List<string> problems)
    {
        int shapeCount = version.Shapes.Shapes.Count;
        if (shapeCount == 0 || version.Shapes.Shapes[0].Count != 0)
            problems.Add($"proto {version.Protocol}: shape pool index 0 must be the empty shape");

        if (legacy)
        {
            if (version.Shapes.CollisionByBlockId is { } byId)
            {
                foreach ((string blockId, int idx) in byId)
                    if (idx < 0 || idx >= shapeCount)
                        problems.Add($"proto {version.Protocol}: legacy shape ref for block {blockId} -> {idx} is dangling");

                if (version.Shapes.CollisionByBlockState is { } byState)
                {
                    foreach ((string stateId, int idx) in byState)
                    {
                        if (idx < 0 || idx >= shapeCount)
                        {
                            problems.Add($"proto {version.Protocol}: legacy meta shape ref for state {stateId} -> {idx} is dangling");
                            continue;
                        }

                        // A supplement entry that names a block the base table does not cover would be read against a base of "uncovered", which is how a meta could silently become non-solid. The supplement refines coverage; it never creates it.
                        if (!int.TryParse(stateId, CultureInfo.InvariantCulture, out int state) || state < 0)
                            problems.Add($"proto {version.Protocol}: legacy meta shape ref key '{stateId}' is not a state id");

                        else if (!byId.ContainsKey((state >> 4).ToString(CultureInfo.InvariantCulture)))
                            problems.Add($"proto {version.Protocol}: legacy meta shape ref for state {stateId} names block {state >> 4}, which the base table does not cover");

                    }
                }
            }
            else if (version.Shapes.CollisionByBlockState is not null)
                problems.Add($"proto {version.Protocol}: legacy meta shape refs present without a base collision_by_block_id table");

            return;
        }
        if (version.Shapes.Collision is { } collision)
        {
            foreach ((string block, IReadOnlyList<int> indices) in collision)
                foreach (int idx in indices)
                    if (idx < 0 || idx >= shapeCount)
                        problems.Add($"proto {version.Protocol}: shape ref for {block} -> {idx} is dangling");
        }
        else
            problems.Add($"proto {version.Protocol}: modern version missing block-shape-refs collision table");

    }

    private static void ValidateMetadata(VersionData version, List<string> problems)
    {
        List<int> ids = version.Metadata.Serializers.Select(s => s.Id).ToList();
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] != i)
            {
                problems.Add($"proto {version.Protocol}: metadata serializer ids not gapless from 0 (index {i})");
                break;
            }

        foreach (MetadataEntry s in version.Metadata.Serializers)
            if (string.IsNullOrEmpty(s.Codec))
                problems.Add($"proto {version.Protocol}: metadata serializer {s.Field} has no codec");

    }

    /// <summary>The data-component registry, checked in FILE ORDER rather than sorted. Registration order IS the wire id here, and the generated component-id tables are a positional projection of this list, so a file whose entries are permuted but whose id set is still gapless would emit a table that decodes every stack against the wrong component.</summary>
    private static void ValidateComponents(VersionData version, bool legacy, List<string> problems)
    {
        if (legacy)
        {
            if (version.Components.Count != 0)
                problems.Add($"proto {version.Protocol}: legacy version must have no data components");

            return;
        }

        for (int i = 0; i < version.Components.Count; i++)
            if (version.Components[i].IdNum != i)
            {
                problems.Add($"proto {version.Protocol}: component registry not gapless from 0 in file order (index {i} carries id {version.Components[i].IdNum})");
                break;
            }

        HashSet<string> seen = [];
        foreach (ComponentEntry entry in version.Components)
            if (!seen.Add(entry.Id))
                problems.Add($"proto {version.Protocol}: duplicate component id {entry.Id}");

    }

    /// <summary>The two other registries a generated table projects positionally: the command argument types and <c>minecraft:particle_type</c>. Same reason as <see cref="ValidateComponents"/>: the emitter writes the list in file order and calls the index the wire id, so file order has to BE the id order or the projection is a lie the emitter cannot see.</summary>
    private static void ValidateProjectedRegistryOrder(VersionData version, List<string> problems)
    {
        CheckOrder(version.ArgumentTypes, "argument_types.json");
        if (version.Registries.TryGetValue("minecraft:particle_type", out IReadOnlyList<RegistryItem>? particles))
            CheckOrder(particles, "registries.json minecraft:particle_type");

        void CheckOrder(IReadOnlyList<RegistryItem> entries, string where)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Id != i)
                {
                    problems.Add($"proto {version.Protocol}: {where} not gapless from 0 in file order (index {i} carries id {entries[i].Id})");
                    return;
                }

        }
    }

    /// <summary><c>metadata-keys.json</c>: which synched-data index each semantic key occupies. Every protocol must name the five base entity fields, the four living-entity fields, and the dropped-item stack. An era-conditional key (no-gravity, pose, ticks-frozen) is absent where the era has none, which is the answer, so only its ORDER is checked when it is present.</summary>
    private static void ValidateMetadataKeys(VersionData version, List<string> problems)
    {
        IReadOnlyDictionary<string, int> keys = version.MetadataKeys.EntityKeys;
        foreach (string required in RequiredMetadataKeys)
            if (!keys.ContainsKey(required))
                problems.Add($"proto {version.Protocol}: metadata-keys.json is missing entity key '{required}'");

        foreach (string name in keys.Keys)
            if (!KnownMetadataKeys.Contains(name))
                problems.Add($"proto {version.Protocol}: metadata-keys.json names unknown entity key '{name}'");

        HashSet<int> used = [];
        foreach ((string name, int index) in keys)
            if (index < 0)
                problems.Add($"proto {version.Protocol}: metadata key '{name}' has negative index {index}");

            else if (!used.Add(index))
                problems.Add($"proto {version.Protocol}: metadata key '{name}' reuses index {index}");

        // Entity's own block is allocated in declaration order, so the five 1.8 fields are 0 through 4 on every protocol and the conditional ones follow in that order where they exist.
        int expected = 0;
        foreach (string name in OrderedMetadataKeys)
            if (keys.TryGetValue(name, out int index) && index != expected++)
            {
                problems.Add($"proto {version.Protocol}: metadata key '{name}' is at index {index}, not {expected - 1}");
                break;
            }

        if (!version.MetadataKeys.ItemEntityKeys.ContainsKey("carried_item"))
            problems.Add($"proto {version.Protocol}: metadata-keys.json is missing item_entity_keys.carried_item");

    }

    private static readonly string[] RequiredMetadataKeys =
    [
        "shared_flags", "air_supply", "custom_name", "custom_name_visible", "silent",
        "health", "effect_particles", "effect_ambience", "arrow_count",
    ];

    private static readonly string[] OrderedMetadataKeys =
    [
        "shared_flags", "air_supply", "custom_name", "custom_name_visible", "silent",
        "no_gravity", "pose", "ticks_frozen",
    ];

    private static readonly HashSet<string> KnownMetadataKeys =
    [
        .. RequiredMetadataKeys, "no_gravity", "pose", "ticks_frozen",
    ];

    /// <summary>
    /// The <c>minecraft:menu</c> table. Every protocol must declare one, both eras: the pre-1.14 dataset must be non-empty. The two eras key differently, so each is checked on its own terms: 1.14+ ids must be gapless from 0, while pre-1.14 has no numeric wire id at all and must instead carry a distinct wire string per entry, which is the only key an open_screen can be resolved by there.
    /// <para>The era split here is 1.14 (the protocol the <c>minecraft:menu</c> registry was introduced in), NOT the dataset's flat/legacy identity, which splits at the 1.13 flattening. The 1.13 datasets are "flat" for items and blocks while their open_screen still carries a string window type, so keying this check on the dataset identity would demand numeric menu ids from three protocols that have none.</para>
    /// </summary>
    private static void ValidateMenus(VersionData version, List<string> problems)
    {
        bool stringWindowTypes = version.Protocol < FirstMenuRegistryProtocol;

        if (version.Menus.Count == 0)
        {
            problems.Add($"proto {version.Protocol}: menu registry is empty");
            return;
        }

        HashSet<string> names = [];
        HashSet<int> ids = [];
        foreach (MenuEntry m in version.Menus)
        {
            if (!names.Add(m.Name))
                problems.Add($"proto {version.Protocol}: duplicate menu name {m.Name}");

            if (!ids.Add(m.IdNum))
                problems.Add($"proto {version.Protocol}: duplicate menu id {m.IdNum}");

            if (m.Slots is < 0)
                problems.Add($"proto {version.Protocol}: menu {m.Name} has a negative slot count");

        }

        if (stringWindowTypes)
        {
            HashSet<string> wires = [];
            foreach (MenuEntry m in version.Menus)
                if (string.IsNullOrEmpty(m.WireId))
                    problems.Add($"proto {version.Protocol}: legacy menu {m.Name} has no wire_id");

                else if (!wires.Add(m.WireId))
                    problems.Add($"proto {version.Protocol}: duplicate legacy menu wire_id {m.WireId}");

        }
        else
            foreach (MenuEntry m in version.Menus)
                if (m.WireId is not null)
                    problems.Add($"proto {version.Protocol}: flat menu {m.Name} must not declare a wire_id");

        List<int> sorted = [.. ids.Order()];
        for (int i = 0; i < sorted.Count; i++)
            if (sorted[i] != i)
            {
                problems.Add($"proto {version.Protocol}: menu ids not gapless from 0 (index {i})");
                break;
            }

    }

    /// <summary>The version's own <c>en_us</c> translation table. A separate table is required per protocol because templates and argument arity vary across eras.</summary>
    private static void ValidateLang(VersionData version, List<string> problems)
    {
        if (version.Lang.Count == 0)
        {
            problems.Add($"proto {version.Protocol}: lang table is empty");
            return;
        }

        if (version.Lang.Count < MinLangEntries)
            problems.Add(
                $"proto {version.Protocol}: lang table has only {version.Lang.Count} entries "
                + $"(minimum {MinLangEntries}); every shipped vanilla en_us table measures at least that many");

        foreach ((string key, string template) in version.Lang)
        {
            if (key.Length == 0)
                problems.Add($"proto {version.Protocol}: lang table has an empty key");

            // An EMPTY template is legitimate vanilla data (e.g. potion.potency.0 and selectWorld.gameMode.spectator.line2 are "" in every era: the empty potency suffix and an empty second line), so it is not flagged here. A NULL JSON value cannot reach this far at all: DatasetLoader.LoadLang throws at load time instead, because {key: string} is a structural invariant of the dataset, not a per-entry semantic one.
            if (!LangFormatCheckExemptKeys.Contains(key) && !FormatGroupsParse(template))
                problems.Add($"proto {version.Protocol}: lang key '{key}' has an unparseable format specifier in '{template}'");

        }

        if (version.Protocol >= FirstChatTypeProtocol)
            foreach (string key in ChatTypeKeys)
                if (!version.Lang.ContainsKey(key))
                    problems.Add($"proto {version.Protocol}: lang table is missing built-in chat-type key '{key}'");

    }

    /// <summary>True when every <c>%</c> in <paramref name="template"/> starts a group matching <c>%(?:(\d+)\$)?([A-Za-z%]|$)</c>. This checks syntax rather than arity: any conversion letter is accepted here, while unsupported conversions fail only when formatted. Wired chat arguments are components and therefore use string conversion.</summary>
    private static bool FormatGroupsParse(string template)
    {
        int i = 0;
        while (true)
        {
            int percent = template.IndexOf('%', i);
            if (percent < 0)
                return true;

            Match match = FormatGroupPattern().Match(template, percent);
            if (!match.Success)
                return false;

            // Advance past the WHOLE match, not just this '%'. A "%%" group is two characters long;
            // advancing by only one would land back on the group's own trailing '%' and re-examine it as if it were a fresh, unmatched group (it is not: it was already consumed above).
            i = percent + match.Length;
        }
    }

    private static void ValidateCodecEras(Dataset dataset, List<string> problems)
    {
        // Every codec key used by a packet must appear in the packet era order.
        HashSet<string> order = [.. dataset.Shared.CodecEras.PacketOrder];
        foreach (VersionData version in dataset.ByProtocol.Values)
            foreach (IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>> flows in version.Packets.Phases.Values)
                foreach (IReadOnlyList<PacketEntry> packets in flows.Values)
                    foreach (PacketEntry p in packets)
                        if (p.Codec is { } codec && !order.Contains(codec))
                            problems.Add($"proto {version.Protocol}: codec key '{codec}' used by {p.Id} is not in codec-eras.json packet order");

    }

    /// <summary>Era-appropriateness (codec-eras.json validates "neighbor consistency"). A version must not reference a codec-era key that is newer than the version itself ("forbid keys newer than the version"). The check is driven by the version each era key encodes: <c>V1_8</c> -&gt; 1.8, <c>V1_21_5</c> -&gt; 1.21.5, <c>V26_2</c> -&gt; 26.2. It also enforces that the packet era order is itself chronological. Membership and unknown-key problems are handled by <see cref="ValidatePacketCodecs"/> and <see cref="ValidateCodecEras"/>; this method adds the era-window constraint on top.</summary>
    private static void ValidateCodecEraAppropriateness(Dataset dataset, List<string> problems)
    {
        // Neighbor consistency: the packet era order must be non-decreasing by encoded version.
        int[]? previous = null;
        string? previousKey = null;
        foreach (string key in dataset.Shared.CodecEras.PacketOrder)
        {
            int[]? parsed = ParseCodecKeyVersion(key);
            if (parsed is null)
            {
                problems.Add($"codec-eras.json packet order key '{key}' does not encode a parseable version");
                continue;
            }
            if (previous is not null && CompareVersion(parsed, previous) < 0)
                problems.Add($"codec-eras.json packet order is not chronological: '{key}' is older than the preceding key '{previousKey}'");

            previous = parsed;
            previousKey = key;
        }

        foreach (VersionData version in dataset.ByProtocol.Values.OrderBy(v => v.Protocol))
        {
            int[]? versionVersion = ParseVersionName(version.VersionName);
            if (versionVersion is null)
            {
                problems.Add($"proto {version.Protocol}: version name '{version.VersionName}' does not parse as a version for era-appropriateness");
                continue;
            }
            foreach ((string phase, IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>> flows) in version.Packets.Phases)
            {
                foreach ((string flow, IReadOnlyList<PacketEntry> packets) in flows)
                {
                    foreach (PacketEntry p in packets)
                    {
                        if (p.Codec is not { } codec)
                            continue;

                        int[]? codecVersion = ParseCodecKeyVersion(codec);
                        // Unparseable / unknown keys are reported elsewhere; only era-check parseable ones.
                        if (codecVersion is not null && CompareVersion(codecVersion, versionVersion) > 0)
                            problems.Add(
                                $"proto {version.Protocol} {phase}/{flow}: packet {p.Id} uses codec key '{codec}' from a newer era than version {version.VersionName}");

                    }
                }
            }
        }
    }

    // Codec era keys name their anchor Minecraft version ("V1_21_5" -> 1.21.5).
    private static int[]? ParseCodecKeyVersion(string key) =>
        key.Length >= 2 && (key[0] == 'V' || key[0] == 'v') ? ParseComponents(key[1..], '_') : null;

    private static int[]? ParseVersionName(string name) => ParseComponents(name, '.');

    private static int[]? ParseComponents(string s, char separator)
    {
        string[] parts = s.Split(separator);
        int[] result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
                return null;

        return result;
    }

    private static int CompareVersion(int[] a, int[] b)
    {
        int length = Math.Max(a.Length, b.Length);
        for (int i = 0; i < length; i++)
        {
            int ai = i < a.Length ? a[i] : 0;
            int bi = i < b.Length ? b[i] : 0;
            if (ai != bi)
                return ai.CompareTo(bi);

        }
        return 0;
    }

    public static bool JsonFlagEquals(JsonElement el, string value) =>
        el.ValueKind == JsonValueKind.String && el.GetString() == value;
}
