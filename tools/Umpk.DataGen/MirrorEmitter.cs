using System.Globalization;
using System.Text;

namespace Umpk.DataGen;

/// <summary>Emits the per-era tables that used to be transcribed by hand next to the codecs that read them: the component-id orderings, the entity-metadata serializer tables, the particle-type id lists and the command argument-type tables (all four into <c>src/Umpk.Protocol.Java</c> through <c>--protocol-out</c>), plus <c>JavaGameData</c>'s per-protocol dispatch and the tier-2 metadata-key indices (into <c>src/Umpk.Data.Java</c> with the rest of <see cref="Emit"/>).</summary>
/// <remarks>Each table is a positional projection of one dataset file, so the only thing this emitter adds is the era NAME, which the dataset does not carry: the maps below pin a member name to the protocol whose file it is emitted from. Every table then gets a coverage check, and a protocol whose own table matches no emitted era fails generation rather than quietly decoding against a neighbour's.</remarks>
internal sealed partial class Emitter
{
    // Item data components. Registration order IS the wire id (see the dataset's own provenance: reports/registries.json#minecraft:data_component_type), so the array index is the id.
    private static readonly (string Member, int Protocol)[] ComponentIdEras =
    [
        ("V1_20_5", 766),
        ("V1_21", 767),
        ("V1_21_2", 768),
        ("V1_21_5", 770),
        ("V1_21_11", 774),
        ("V26_1", 775),
        ("V26_2", 776),
        ("V26_3", 777),
    ];

    // Entity-metadata serializer tables, one per distinct EntityDataSerializers registration order.
    private static readonly (string Member, int Protocol)[] MetadataEras =
    [
        ("V1_9", 107),
        ("V1_12", 335),
        ("V1_13", 393),
        ("V1_14", 477),
        ("V1_19", 759),
        ("V1_19_3", 761),
        ("V1_19_4", 762),
        ("V1_21_2", 766),
        ("V1_21_5", 770),
        ("V1_21_9", 773),
        ("V1_21_11", 774),
        ("V26_1", 775),
        ("V777", 777),
    ];

    // minecraft:particle_type orderings, for the eras whose particles carry a modern option payload.
    private static readonly (string Member, int Protocol)[] ParticleIdEras =
    [
        ("V1_20_5", 766),
        ("V1_21_2", 768),
        ("V1_21_4", 769),
        ("V1_21_5", 770),
        ("V1_21_9", 773),
        ("V26_1", 775),
        ("V26_2", 776),
        ("V26_3", 777),
    ];

    // minecraft:command_argument_type orderings, from 1.19 onward where an argument node names its parser by registry id rather than by resource-location string.
    private static readonly (string Member, int Protocol)[] ArgumentTypeEras =
    [
        ("V759", 759),
        ("V761", 761),
        ("V764", 764),
        ("V765", 765),
        ("V766", 766),
        ("V770", 770),
        ("V771", 771),
        ("V776", 776),
        ("V777", 777),
    ];

    /// <summary>The <c>metadata.json</c> <c>codec</c> value for each <c>ModernMetadataSerializer</c> member. Everything Holder-based or enum-shaped is a plain VarInt registry id on the wire and collapses onto one kind; an unmapped codec name is a hard failure, because guessing would bind a new serializer to a byte width nobody chose.</summary>
    private static readonly Dictionary<string, string> MetadataSerializerKinds = new(StringComparer.Ordinal)
    {
        ["Byte"] = "Byte",
        ["VarInt"] = "Int",
        ["VarLong"] = "Long",
        ["Float"] = "Float",
        ["String"] = "String",
        ["Component"] = "Component",
        ["OptionalComponent"] = "OptionalComponent",
        ["ItemStack"] = "ItemStack",
        ["Boolean"] = "Boolean",
        ["Rotations"] = "Rotations",
        ["BlockPos"] = "BlockPos",
        ["OptionalBlockPos"] = "OptionalBlockPos",
        ["Direction"] = "Direction",
        ["OptionalUuid"] = "OptionalUuid",
        ["OptionalLivingEntityReference"] = "OptionalUuid",
        ["BlockState"] = "BlockState",
        ["OptionalBlockState"] = "OptionalBlockState",
        ["CompoundTag"] = "CompoundTag",
        ["Particle"] = "Particle",
        ["Particles"] = "Particles",
        ["ResolvableProfile"] = "ResolvableProfile",
        ["VillagerData"] = "VillagerData",
        ["OptionalUnsignedInt"] = "OptionalUnsignedInt",
        ["Pose"] = "Pose",
        ["OptionalGlobalPos"] = "OptionalGlobalPos",
        ["Vector3"] = "Vector3",
        ["Quaternion"] = "Quaternion",
        ["ArmadilloState"] = "VarIntHolder",
        ["CatSoundVariant"] = "VarIntHolder",
        ["CatVariant"] = "VarIntHolder",
        ["ChickenSoundVariant"] = "VarIntHolder",
        ["ChickenVariant"] = "VarIntHolder",
        ["CopperGolemState"] = "VarIntHolder",
        ["CowSoundVariant"] = "VarIntHolder",
        ["CowVariant"] = "VarIntHolder",
        ["DyeColor"] = "VarIntHolder",
        ["FrogVariant"] = "VarIntHolder",
        ["HumanoidArm"] = "VarIntHolder",
        ["PaintingVariant"] = "VarIntHolder",
        ["PigSoundVariant"] = "VarIntHolder",
        ["PigVariant"] = "VarIntHolder",
        ["SnifferState"] = "VarIntHolder",
        ["WeatheringCopperState"] = "VarIntHolder",
        ["WolfSoundVariant"] = "VarIntHolder",
        ["WolfVariant"] = "VarIntHolder",
        ["ZombieNautilusVariant"] = "VarIntHolder",
    };

    /// <summary>The <c>EntityMetadataKeys</c> member each <c>metadata-keys.json</c> name refers to. The dataset names keys the way vanilla names the fields; the runtime addresses them by singleton.</summary>
    private static readonly Dictionary<string, string> MetadataKeyMembers = new(StringComparer.Ordinal)
    {
        ["shared_flags"] = "SharedFlags",
        ["air_supply"] = "AirSupply",
        ["custom_name"] = "CustomName",
        ["custom_name_visible"] = "CustomNameVisible",
        ["silent"] = "Silent",
        ["no_gravity"] = "NoGravity",
        ["pose"] = "Pose",
        ["ticks_frozen"] = "TicksFrozen",
        ["health"] = "Health",
        ["effect_particles"] = "EffectParticles",
        ["effect_ambience"] = "EffectAmbience",
        ["arrow_count"] = "ArrowCount",
    };

    /// <summary>1.8's metadata terminator. Its serializer table is a different wire format entirely.</summary>
    private const string LegacyMetadataTerminator = "0x7f";

    /// <summary>The tables that live beside the codecs rather than in <c>Umpk.Data.Java</c>. Written only when <c>generate</c> is given <c>--protocol-out</c>, so an <c>--out</c>-only invocation emits exactly what it emitted before this method existed.</summary>
    public IReadOnlyDictionary<string, EmittedFile> EmitProtocol()
    {
        Dictionary<string, EmittedFile> files = [];
        EmitComponentIds(files);
        EmitMetadataTables(files);
        EmitParticleIds(files);
        EmitArgumentTypeTables(files);
        return files;
    }

    private void EmitComponentIds(Dictionary<string, EmittedFile> files)
    {
        StringBuilder sb = new();
        ProtocolHeader(sb);
        sb.AppendLine("namespace Umpk.Protocol.Java.Codecs;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The per-era item data-component wire-id orderings: the registration order Mojang assigns, which IS the numeric wire id. Consumed by <see cref=\"ItemComponentTable\"/> to map a wire id to its identifier, so a stack decoded against a neighbouring era's ordering dispatches every component at or past the first divergence to the wrong codec.</summary>");
        sb.AppendLine("internal static class ComponentIds");
        sb.AppendLine("{");

        Dictionary<string, string> emitted = [];
        bool first = true;
        foreach ((string member, int protocol) in ComponentIdEras)
        {
            if (!_dataset.ByProtocol.TryGetValue(protocol, out VersionData? version))
                continue;

            List<string> ids = [.. version.Components.Select(c => c.Id)];
            emitted[member] = string.Join('\0', ids);
            if (!first)
                sb.AppendLine();

            first = false;
            sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>Protocol {protocol} ({version.VersionName}) component id ordering ({ids.Count} entries).</summary>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    public static string[] {member} {{ get; }} =");
            sb.AppendLine("    [");
            foreach (string id in ids)
                sb.AppendLine(CultureInfo.InvariantCulture, $"        \"{id}\",");

            sb.AppendLine("    ];");
        }

        sb.AppendLine("}");
        files["Wire/Components/ComponentIds.g.cs"] =
            new EmittedFile("Wire/Components/ComponentIds.g.cs", sb.ToString());

        CheckEraCoverage(
            "component id",
            ComponentIdEras,
            emitted,
            v => v.Components.Count == 0 ? null : string.Join('\0', v.Components.Select(c => c.Id)));
    }

    private void EmitMetadataTables(Dictionary<string, EmittedFile> files)
    {
        StringBuilder sb = new();
        ProtocolHeader(sb);
        sb.AppendLine("namespace Umpk.Protocol.Java.Codecs;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The per-era entity-metadata serializer tables: wire serializer id to value shape, in vanilla's own <c>EntityDataSerializers</c> registration order. An id resolved against the wrong era's table reads the wrong number of bytes and desynchronizes the rest of the metadata list.</summary>");
        sb.AppendLine("internal sealed partial class ModernMetadataTable");
        sb.AppendLine("{");

        Dictionary<string, string> emitted = [];
        bool first = true;
        foreach ((string member, int protocol) in MetadataEras)
        {
            if (!_dataset.ByProtocol.TryGetValue(protocol, out VersionData? version))
                continue;

            List<MetadataEntry> serializers = [.. version.Metadata.Serializers];
            emitted[member] = MetadataSignature(protocol, serializers);
            if (!first)
                sb.AppendLine();

            first = false;
            sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>Protocol {protocol} ({version.VersionName}) serializer order (ids 0-{serializers.Count - 1}).</summary>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    public static ModernMetadataTable {member} {{ get; }} = new(");
            sb.AppendLine("    [");
            foreach (MetadataEntry entry in serializers)
            {
                string kind = MetadataKind(protocol, entry);
                sb.AppendLine(CultureInfo.InvariantCulture, $"        ModernMetadataSerializer.{kind}, // {entry.Id} {entry.Field}");
            }
            sb.AppendLine("    ]);");
        }

        sb.AppendLine("}");
        files["Wire/Metadata/ModernMetadataTable.g.cs"] =
            new EmittedFile("Wire/Metadata/ModernMetadataTable.g.cs", sb.ToString());

        CheckEraCoverage(
            "entity-metadata serializer",
            MetadataEras,
            emitted,
            v => v.Metadata.Terminator == LegacyMetadataTerminator
                ? null
                : MetadataSignature(v.Protocol, v.Metadata.Serializers));
    }

    private string MetadataSignature(int protocol, IReadOnlyList<MetadataEntry> serializers) =>
        string.Join('\0', serializers.Select(e => MetadataKind(protocol, e)));

    private string MetadataKind(int protocol, MetadataEntry entry) =>
        MetadataSerializerKinds.TryGetValue(entry.Codec, out string? kind)
            ? kind
            : throw new DatasetException(
                $"protocol {protocol}: metadata serializer {entry.Id} ({entry.Field}) has codec '{entry.Codec}', "
                + "which no ModernMetadataSerializer kind is mapped to. Add the mapping in MirrorEmitter "
                + "after reading that serializer's stream codec in the matching decompiled EntityDataSerializers.");

    private void EmitParticleIds(Dictionary<string, EmittedFile> files)
    {
        StringBuilder sb = new();
        ProtocolHeader(sb);
        sb.AppendLine("namespace Umpk.Protocol.Java.Codecs;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The per-era <c>minecraft:particle_type</c> registry orderings, index = wire id. This is only half of what a particle decode needs: the option PAYLOAD each type carries is read off that version's option classes and is not in any registry report, so it stays hand-written in <see cref=\"ParticleCodec\"/> and is keyed by name against these tables.</summary>");
        sb.AppendLine("internal static class ParticleIds");
        sb.AppendLine("{");

        Dictionary<string, string> emitted = [];
        bool first = true;
        foreach ((string member, int protocol) in ParticleIdEras)
        {
            if (!_dataset.ByProtocol.TryGetValue(protocol, out VersionData? version)
                || ParticleNames(version) is not { Count: > 0 } names)
                continue;

            emitted[member] = string.Join('\0', names);
            if (!first)
                sb.AppendLine();

            first = false;
            sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>Protocol {protocol} ({version.VersionName}) particle_type ordering ({names.Count} entries).</summary>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    public static string[] {member} {{ get; }} =");
            sb.AppendLine("    [");
            foreach (string name in names)
                sb.AppendLine(CultureInfo.InvariantCulture, $"        \"{name}\",");

            sb.AppendLine("    ];");
        }

        sb.AppendLine("}");
        files["Wire/Particles/ParticleIds.g.cs"] =
            new EmittedFile("Wire/Particles/ParticleIds.g.cs", sb.ToString());

        CheckEraCoverage(
            "particle id",
            ParticleIdEras,
            emitted,
            v => ParticleNames(v) is { Count: > 0 } names ? string.Join('\0', names) : null);
    }

    private static List<string> ParticleNames(VersionData version) =>
        version.Registries.TryGetValue("minecraft:particle_type", out IReadOnlyList<RegistryItem>? particles)
            ? [.. particles.Select(p => p.Name)]
            : [];

    private void EmitArgumentTypeTables(Dictionary<string, EmittedFile> files)
    {
        StringBuilder sb = new();
        ProtocolHeader(sb);
        sb.AppendLine("using System.Collections.Immutable;");
        sb.AppendLine();
        sb.AppendLine("namespace Umpk.Protocol.Java.Packets;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The per-era command argument-type tables, index = the wire registry id. Before 1.19 an argument node named its parser with a resource-location string, so the first table is 759's. The registry's contents moved repeatedly afterwards and a single displaced id desynchronizes every node behind it, which is why no era can borrow a neighbour's table.</summary>");
        sb.AppendLine("internal static class ArgumentTypeTables");
        sb.AppendLine("{");

        Dictionary<string, string> emitted = [];
        bool first = true;
        foreach ((string member, int protocol) in ArgumentTypeEras)
        {
            if (!_dataset.ByProtocol.TryGetValue(protocol, out VersionData? version)
                || version.ArgumentTypes.Count == 0)
                continue;

            List<string> names = [.. version.ArgumentTypes.Select(a => a.Name)];
            emitted[member] = string.Join('\0', names);
            if (!first)
                sb.AppendLine();

            first = false;
            sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>Protocol {protocol} ({version.VersionName}) argument-type table ({names.Count} entries).</summary>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    public static ImmutableArray<string> {member} {{ get; }} =");
            sb.AppendLine("    [");
            foreach (string name in names)
                sb.AppendLine(CultureInfo.InvariantCulture, $"        \"{name}\",");

            sb.AppendLine("    ];");
        }

        sb.AppendLine("}");
        files["Wire/ArgumentTypes/ArgumentTypeTables.g.cs"] =
            new EmittedFile("Wire/ArgumentTypes/ArgumentTypeTables.g.cs", sb.ToString());

        CheckEraCoverage(
            "argument-type",
            ArgumentTypeEras,
            emitted,
            v => v.ArgumentTypes.Count == 0 ? null : string.Join('\0', v.ArgumentTypes.Select(a => a.Name)));
    }

    /// <summary>Fails generation when a protocol's own table matches none of the emitted eras. This is the check that makes adding a protocol loud: without it a new version simply decodes against whichever neighbouring era someone happened to bind, and nothing anywhere says so.</summary>
    /// <param name="table">The table's name, for the failure message.</param>
    /// <param name="eras">The era map, whose lowest protocol is the floor the check applies above.</param>
    /// <param name="emitted">The signature of every era actually emitted, by member name.</param>
    /// <param name="signature">The protocol's own signature, or null when the table does not apply to it.</param>
    private void CheckEraCoverage(
        string table,
        (string Member, int Protocol)[] eras,
        IReadOnlyDictionary<string, string> emitted,
        Func<VersionData, string?> signature)
    {
        int floor = eras.Min(e => e.Protocol);
        HashSet<string> known = [.. emitted.Values];
        foreach (VersionData version in _dataset.ByProtocol.Values.OrderBy(v => v.Protocol))
        {
            if (version.Protocol < floor || signature(version) is not { } own || known.Contains(own))
                continue;

            throw new DatasetException(
                $"protocol {version.Protocol}: its {table} table matches no emitted era. "
                + $"Add ({version.Protocol}) to the {table} era map in MirrorEmitter and bind the new table, "
                + "or confirm the protocol really does share an existing era's ordering.");
        }
    }

    private static void ProtocolHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated by Umpk.DataGen from data/java. Do not edit by hand.");
        sb.AppendLine("// Regenerate: dotnet run --project tools/Umpk.DataGen -- generate --data data/java --protocol-out src/Umpk.Protocol.Java");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }
}

/// <summary>The <c>Umpk.Data.Java</c> half of the same idea: <see cref="Emitter.EmitGameDataDispatch"/> writes the per-protocol table dispatch <c>JavaGameData</c> used to carry as fourteen hand-maintained switches, and <see cref="Emitter.EmitMetadataKeys"/> writes the tier-2 index tables that used to be four raw protocol comparisons in the same file.</summary>
internal sealed partial class Emitter
{
    // The packed per-protocol tables every descriptor carries, with the noun each refusal names. A protocol without one is not supported, which is a different answer from an empty table.
    private static readonly (string Member, string Noun)[] GameDataTables =
    [
        ("ItemNames", "item-name"),
        ("SoundNames", "sound-event name"),
        ("BlockDefs", "block-definition"),
        ("BlockAttrs", "block-attribute"),
        ("EntityNames", "entity-name"),
        ("LegacyObjectEntityNames", "legacy object-entity name"),
        ("MenuDefs", "menu-definition"),
        ("CollisionShapes", "collision-shape"),
        ("BlockShapeRefs", "block shape-reference"),
        ("BlockPush", "block-push"),
        ("BlockMining", "block-mining"),
        ("BlockInteractions", "block-interactions"),
        ("EntityGeometry", "entity-geometry"),
        ("BlockTargeting", "block-targeting"),
        ("EnchantmentDefs", "enchantment-definition"),
        ("MobEffectDefs", "mob-effect definition"),
        ("AttributeDefs", "attribute-definition"),
    ];

    private void EmitGameDataDispatch()
    {
        List<VersionData> versions = [.. _dataset.ByProtocol.Values.OrderBy(v => v.Protocol)];
        StringBuilder sb = new();
        Header(sb, "Umpk.Data.Java");
        sb.AppendLine("namespace Umpk.Data.Java;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Per-protocol access to the packed tables the generated descriptors carry. Emitted rather than written, so a protocol added to the dataset cannot reach the runtime with a table nobody wired up: the arm is either here or the dataset does not have the version.</summary>");
        sb.AppendLine("public static partial class JavaGameData");
        sb.AppendLine("{");

        bool first = true;
        foreach ((string member, string noun) in GameDataTables)
        {
            if (!first)
                sb.AppendLine();

            first = false;
            sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>The packed {noun} table for a protocol. Unsupported protocols are refused.</summary>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    private static ReadOnlySpan<byte> {member}(int protocol) => protocol switch");
            sb.AppendLine("    {");
            foreach (VersionData version in versions)
                sb.AppendLine(CultureInfo.InvariantCulture, $"        {version.Protocol} => global::Umpk.Data.Java.V{version.Protocol}.Descriptor.{member},");

            sb.AppendLine(CultureInfo.InvariantCulture, $"        _ => throw Unsupported(protocol, \"{noun}\"),");
            sb.AppendLine("    };");
        }

        sb.AppendLine("    /// <summary>Exact collision grids for measured protocols. An absent table retains the source's existing bounding-box path.</summary>");
        sb.AppendLine("    private static ReadOnlySpan<byte> BlockCollisionGrids(int protocol) => protocol switch");
        sb.AppendLine("    {");
        foreach (VersionData version in versions.Where(version => CollisionGridTable.Load(version) is not null))
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {version.Protocol} => global::Umpk.Data.Java.V{version.Protocol}.Descriptor.BlockCollisionGrids,");
        sb.AppendLine("        _ => default,");
        sb.AppendLine("    };");
        sb.AppendLine();

        // Only the pre-flattening descriptors declare LegacyItemDefs, so a full protocol switch would not compile. An empty span is the answer for a flattened protocol.
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The packed legacy composite item table for a pre-flattening protocol, or an empty span for the flattened protocols (which have no such table) and for unsupported protocols.</summary>");
        sb.AppendLine("    /// <remarks>The one table here that answers rather than refuses, because the generated descriptors carry a <c>LegacyItemDefs</c> member only for the pre-flattening versions: an empty span IS the answer for a flattened protocol, and a full switch over every protocol would not compile.</remarks>");
        sb.AppendLine("    private static ReadOnlySpan<byte> LegacyItemDefs(int protocol) => protocol switch");
        sb.AppendLine("    {");
        foreach (VersionData version in versions.Where(v => v.Identity == "legacy"))
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {version.Protocol} => global::Umpk.Data.Java.V{version.Protocol}.Descriptor.LegacyItemDefs,");

        sb.AppendLine("        _ => default,");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    private static ReadOnlySpan<byte> ItemAttackDefaults(int protocol) => protocol switch");
        sb.AppendLine("    {");
        foreach (VersionData version in versions.Where(version => ItemAttackTable.Load(version) is not null))
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {version.Protocol} => global::Umpk.Data.Java.V{version.Protocol}.Descriptor.ItemAttackDefaults,");
        sb.AppendLine("        _ => default,");
        sb.AppendLine("    };");
        sb.AppendLine("}");

        Add("JavaGameData.Dispatch.g.cs", sb.ToString());
    }

    private void EmitMetadataKeys()
    {
        List<VersionData> versions = [.. _dataset.ByProtocol.Values.OrderBy(v => v.Protocol)];

        // Identical bands collapse onto one instance: there are five distinct index layouts across the whole range, and emitting forty-nine copies of five tables would hide that.
        Dictionary<string, string> bandBySignature = [];
        List<(string Member, VersionData Sample)> bands = [];
        Dictionary<int, string> memberByProtocol = [];
        foreach (VersionData version in versions)
        {
            string signature = MetadataKeySignature(version);
            if (!bandBySignature.TryGetValue(signature, out string? member))
            {
                member = "Band" + version.Protocol.ToString(CultureInfo.InvariantCulture);
                bandBySignature[signature] = member;
                bands.Add((member, version));
            }
            memberByProtocol[version.Protocol] = member;
        }

        StringBuilder sb = new();
        Header(sb, "Umpk.Data.Java");
        sb.AppendLine("using Umpk.Game.Entities;");
        sb.AppendLine();
        sb.AppendLine("namespace Umpk.Data.Java;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>The tier-2 metadata index tables, one per band of protocols that share a synched-data layout. Vanilla allocates an index per class in the order the <c>DATA_*</c> fields initialise, seeding from the nearest superclass's high-water mark, so a band boundary is exactly a protocol where <c>Entity</c> or <c>LivingEntity</c> gained a field. Each band's anchor is in its own <c>metadata-keys.json</c> provenance.</summary>");
        sb.AppendLine("public sealed partial class JavaEntityMetadataKeys");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The era table for a protocol number. An unsupported protocol is refused.</summary>");
        sb.AppendLine("    internal static JavaEntityMetadataKeys For(int protocol) => protocol switch");
        sb.AppendLine("    {");
        foreach ((string member, VersionData sample) in bands)
        {
            string arms = string.Join(
                " or ",
                memberByProtocol.Where(kv => kv.Value == member)
                    .Select(kv => kv.Key.ToString(CultureInfo.InvariantCulture)));
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {arms} => {member},");
            _ = sample;
        }
        sb.AppendLine("        _ => throw Unsupported(protocol),");
        sb.AppendLine("    };");

        foreach ((string member, VersionData sample) in bands)
        {
            IReadOnlyList<int> covered =
                [.. memberByProtocol.Where(kv => kv.Value == member).Select(kv => kv.Key).Order()];
            string span = covered.Count == 1
                ? $"Protocol {covered[0]} ({sample.VersionName})"
                : $"Protocols {covered[0]} through {covered[^1]} ({sample.VersionName} through {_dataset.ByProtocol[covered[^1]].VersionName})";
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>{span}.</summary>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    private static JavaEntityMetadataKeys {member} {{ get; }} = new(");
            sb.AppendLine(CultureInfo.InvariantCulture, $"        {sample.MetadataKeys.ItemEntityKeys["carried_item"].ToString(CultureInfo.InvariantCulture)},");
            sb.AppendLine("        [");
            foreach ((string name, int index) in sample.MetadataKeys.EntityKeys.OrderBy(kv => kv.Value))
            {
                if (!MetadataKeyMembers.TryGetValue(name, out string? keyMember))
                    throw new DatasetException(
                        $"protocol {sample.Protocol}: metadata-keys.json names '{name}', which is not an "
                        + "EntityMetadataKeys member. Add the key to Umpk.Game first, then map it here.");

                sb.AppendLine(CultureInfo.InvariantCulture, $"            new(EntityMetadataKeys.{keyMember}, {index.ToString(CultureInfo.InvariantCulture)}),");
            }
            sb.AppendLine("        ]);");
        }

        sb.AppendLine("}");
        Add("JavaEntityMetadataKeys.g.cs", sb.ToString());
    }

    private static string MetadataKeySignature(VersionData version) =>
        version.MetadataKeys.ItemEntityKeys["carried_item"].ToString(CultureInfo.InvariantCulture)
        + "|"
        + string.Join(
            ' ',
            version.MetadataKeys.EntityKeys.OrderBy(kv => kv.Value)
                .Select(kv => kv.Key + "=" + kv.Value.ToString(CultureInfo.InvariantCulture)));
}
