using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umpk.DataGen;

/// <summary>In-memory model of the canonical <c>data/java</c> dataset. Loaded from JSON by <see cref="DatasetLoader"/>; consumed by the validator, the differ, and the emitter. The model is deliberately loose (nullable fields, raw lists) because the dataset includes per-version shape differences (legacy 47 vs modern 770/776); the validator is what enforces the invariants.</summary>
internal sealed class Dataset
{
    public required IReadOnlyList<VersionCatalogEntry> Versions { get; init; }
    public required IReadOnlyDictionary<int, VersionData> ByProtocol { get; init; }
    public required SharedData Shared { get; init; }
}

internal sealed record VersionCatalogEntry(string Name, int Protocol, string DatasetDir, string Identity);

internal sealed class VersionData
{
    public required int Protocol { get; init; }
    public required string VersionName { get; init; }
    public required string Identity { get; init; } // "flat" or "legacy"
    public required PacketTables Packets { get; init; }
    public required IReadOnlyList<RegistryItem> Items { get; init; }
    public required IReadOnlyList<ComponentEntry> Components { get; init; }
    public required IReadOnlyList<RegistryItem> ArgumentTypes { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<RegistryItem>> Registries { get; init; }
    public required IReadOnlyList<EntityEntry> Entities { get; init; }
    public required IReadOnlyList<EntityEntry> ObjectEntities { get; init; }
    public required IReadOnlyList<MenuEntry> Menus { get; init; }
    public required BlockTable Blocks { get; init; }
    public required ShapeTable Shapes { get; init; }

    /// <summary>The per-block piston facts, present only for measured protocols. Null means the dataset carries no measurement for this version, which the emitter turns into an EMPTY table and the client reads as "do not model pushed blocks here".</summary>
    public BlockPushTable? Push { get; init; }

    public required MetadataTable Metadata { get; init; }

    /// <summary>The tier-2 semantic metadata key indices for this protocol (<c>metadata-keys.json</c>): which synched-data index each named key occupies. Separate from <see cref="Metadata"/>, which is the serializer table: one says how a value is encoded, the other says where it sits.</summary>
    public required MetadataKeyTable MetadataKeys { get; init; }
    public required FeatureFlags Features { get; init; }

    /// <summary>The version's <c>en_us</c> translation table (key -> format template). Kept per protocol rather than shared because argument arity drifts across eras for the same key (see that script's module docstring), so resolving a band's chat against another era's template can silently misrender.</summary>
    public required IReadOnlyDictionary<string, string> Lang { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> RawFiles { get; init; }
}

internal sealed class PacketTables
{
    // phase -> flow -> ordered packet list
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>>> Phases { get; init; }
}

internal sealed record PacketEntry(string Id, int ProtocolId, string? Codec);

internal sealed record RegistryItem(string Name, int Id);

/// <summary>One entity-type entry. Flat era (1.14+) has a single id space in <c>entries</c>; the pre-1.14 datasets carry TWO disjoint spaces, <c>mob_entries</c> (SpawnMob / add_mob) and <c>object_entries</c> (SpawnObject / add_entity), and the same number means different things in each. Both are loaded: the mob/flat space into <c>VersionData.Entities</c> and the object space into <c>VersionData.ObjectEntities</c>. Width/Height are null in the current data and default at read time in JavaGameData.</summary>
internal sealed record EntityEntry(string Name, int IdNum, float? Width, float? Height);

internal sealed record ComponentEntry(string Id, int IdNum, string? Codec);

/// <summary>One <c>minecraft:menu</c> entry, i.e. a kind of container window.</summary>
/// <param name="Name">The registry identifier, e.g. <c>minecraft:furnace</c>.</param>
/// <param name="IdNum">The wire id on 1.14+, where the menu registry is real and open_screen carries the number. On the pre-1.14 datasets there is no numeric wire id at all, so this is a synthetic dense index and <paramref name="WireId"/> is the only key that means anything on the wire.</param>
/// <param name="WireId">The literal open_screen window-type string for the pre-1.14 era, or null on 1.14+ where the wire carries the number instead. Equal to <paramref name="Name"/> for every legacy entry except the horse window, whose wire spelling ("EntityHorse") is not a well-formed identifier.</param>
/// <param name="Slots">The menu's own static slot count when the data records one. Null everywhere today: pre-1.14 the count rides on the open_screen packet, and later registry data does not carry it.</param>
internal sealed record MenuEntry(string Name, int IdNum, string? WireId, int? Slots);

internal sealed class BlockTable
{
    public required string Identity { get; init; }
    // Modern: one entry per block with a state range. Legacy: one entry per (id, meta).
    public required IReadOnlyList<BlockEntry> Blocks { get; init; }
}

internal sealed record BlockEntry(
    string? Name,
    int? BlockId,
    int? Meta,
    int? StateId,
    int? DefaultState,
    int? MinState,
    int? MaxState,
    int? NumStates,
    IReadOnlyList<string>? Properties);

internal sealed class ShapeTable
{
    public required IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> Shapes { get; init; }
    // Modern refs (block name -> per-state shape index) live in block-shape-refs.json.
    public IReadOnlyDictionary<string, IReadOnlyList<int>>? Collision { get; init; }
    // Legacy: block id -> shape index.
    public IReadOnlyDictionary<string, int>? CollisionByBlockId { get; init; }
    // Legacy supplement: (id << 4 | meta) state id -> shape index, for the states whose collision differs from their block's meta-0 shape. Absent on datasets that carry no meta variants.
    public IReadOnlyDictionary<string, int>? CollisionByBlockState { get; init; }
}

/// <summary>One version's <c>block-push.json</c>: the three per-block constants consumed by the piston movement rules. Everything not named here takes <see cref="DefaultReaction"/>, is breakable, and has no block entity.</summary>
internal sealed class BlockPushTable
{
    public required string DefaultReaction { get; init; }

    /// <summary>Block name -> push reaction, for the blocks whose reaction is NOT the default.</summary>
    public required IReadOnlyDictionary<string, string> Reactions { get; init; }

    /// <summary>Blocks whose <c>getDestroySpeed</c> is <c>-1.0F</c>.</summary>
    public required IReadOnlySet<string> Unbreakable { get; init; }

    /// <summary>Blocks with a block entity, which vanilla refuses to push whatever their reaction.</summary>
    public required IReadOnlySet<string> BlockEntity { get; init; }
}

internal sealed record MetadataEntry(int Id, string Field, string Codec);

/// <summary>One protocol's <c>metadata-keys.json</c>. <see cref="EntityKeys"/> covers the fields every entity carries (they are declared on <c>Entity</c> or <c>LivingEntity</c>); <see cref="ItemEntityKeys"/> covers the ones a single entity type declares and that therefore only resolve for it.</summary>
internal sealed class MetadataKeyTable
{
    public required IReadOnlyDictionary<string, int> EntityKeys { get; init; }

    public required IReadOnlyDictionary<string, int> ItemEntityKeys { get; init; }
}

internal sealed class MetadataTable
{
    public required string Terminator { get; init; }
    public required IReadOnlyList<MetadataEntry> Serializers { get; init; }
}

internal sealed class FeatureFlags
{
    public required IReadOnlyDictionary<string, JsonElement> Flags { get; init; }
}

internal sealed class SharedData
{
    public required CodecEras CodecEras { get; init; }
    public required IReadOnlyDictionary<string, JsonElement> RawFiles { get; init; }

    /// <summary>The per-block attribute table: physics scalars, semantic flags, and property value domains.</summary>
    public required BlockAttributeTable BlockAttributes { get; init; }

    /// <summary>Default, minimum, and maximum values for every entity attribute, keyed by raw registry name.</summary>
    public required AttributeDefaultTable AttributeDefaults { get; init; }
}

/// <summary>One attribute definition: default, range, and ranged flag.</summary>
internal readonly record struct AttributeDefaultRow(double Default, double Min, double Max, bool IsRanged);

/// <summary>Attribute defaults keyed by raw registry name.</summary>
/// <remarks>The key is deliberately raw. <c>minecraft:horse.jump_strength</c> (protocols 735-765) has default <c>0.7</c> and range <c>[0,2]</c>. <c>minecraft:generic.jump_strength</c> (766-767) has default <c>0.42F</c> and range <c>[0,32]</c>. Both canonicalize to the same string, but the client accepts both values. A canonical key would emit the wrong row for one era.</remarks>
internal sealed class AttributeDefaultTable
{
    private readonly IReadOnlyDictionary<string, AttributeDefaultRow> _rows;

    private AttributeDefaultTable(IReadOnlyDictionary<string, AttributeDefaultRow> rows) => _rows = rows;

    /// <summary>The rows keyed by raw name.</summary>
    public IReadOnlyDictionary<string, AttributeDefaultRow> Rows => _rows;

    /// <summary>The row for a raw attribute name. A missing row is a hard failure: a zero-filled definition would clamp that attribute to zero on every session, which is worse than the empty registry this table replaces. A new protocol that introduces an attribute must add its vanilla numbers.</summary>
    /// <exception cref="DatasetException">No curated row exists for the name.</exception>
    public AttributeDefaultRow Require(string rawName, int protocol) =>
        _rows.TryGetValue(rawName, out AttributeDefaultRow row)
            ? row
            : throw new DatasetException(
                $"shared/attribute-defaults.json has no row for '{rawName}' (needed by protocol {protocol}). "
                + "Add its vanilla RangedAttribute default/min/max from the matching decompiled Attributes.java; "
                + "a zero-filled definition would clamp the attribute to zero and is never the answer.");

    public static AttributeDefaultTable Load(SharedData shared)
    {
        if (!shared.RawFiles.TryGetValue("attribute-defaults.json", out JsonElement root))
            throw new DatasetException("shared/attribute-defaults.json is missing");

        if (!root.TryGetProperty("attributes", out JsonElement table) || table.ValueKind != JsonValueKind.Object)
            throw new DatasetException("shared/attribute-defaults.json is missing the attributes block");

        Dictionary<string, AttributeDefaultRow> rows = [];
        foreach (JsonProperty prop in table.EnumerateObject())
            rows[prop.Name] = new AttributeDefaultRow(
                prop.Value.GetProperty("default").GetDouble(),
                prop.Value.GetProperty("min").GetDouble(),
                prop.Value.GetProperty("max").GetDouble(),
                !prop.Value.TryGetProperty("ranged", out JsonElement ranged) || ranged.GetBoolean());

        return new AttributeDefaultTable(rows);
    }
}

internal sealed class CodecEras
{
    public required IReadOnlyList<string> PacketOrder { get; init; }
    public required IReadOnlyList<string> PayloadOrder { get; init; }
}
