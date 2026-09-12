using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Umpk.DataGen;

/// <summary>
/// Dataset emission: turns the dataset into C# sources and binary blobs. The output is exercised by golden-text tests and compiles as part of the Umpk.Data.Java library.
///
/// Emission rules:
///  * <c>JavaVersions</c> has NO static constructor and NO static field initializers;
///    every V* property and All/TryGetBy* is lazy (normative trimming shape).
///  * Large mechanical tables (registry name lists, shape AABBs) are emitted as binary
///    blobs exposed via <c>ReadOnlySpan&lt;byte&gt;</c> properties with index structs.
///  * Small tables (packet lists, metadata codecs, components) are readable C#.
///  * Identical tables of the same semantic kind are deduplicated: one shared class,
///    referenced by multiple descriptors. Each member is named for the earliest protocol
///    that owns its content, so implementation details never leak into generated identifiers.
/// </summary>
internal sealed partial class Emitter
{
    private readonly Dataset _dataset;
    private readonly Dictionary<string, EmittedFile> _files = [];

    // (semantic kind, full content hash) -> readable member name. A second content-only index keeps the embedded bytes globally deduplicated while unrelated table kinds receive semantic aliases.
    private readonly Dictionary<(string Kind, string Hash), string> _dedup = [];
    private readonly Dictionary<string, string> _contentDedup = [];
    private int _sharedCounter;

    internal Emitter(Dataset dataset) => _dataset = dataset;

    internal readonly record struct EmittedFile(string Path, string Text);

    public IReadOnlyDictionary<string, EmittedFile> Emit()
    {
        foreach (VersionData version in _dataset.ByProtocol.Values.OrderBy(v => v.Protocol))
            EmitDescriptor(version);

        EmitCatalog();
        EmitGameDataDispatch();
        EmitMetadataKeys();
        EmitSharedTables();
        return _files;
    }

    private static string Member(int protocol) => $"V{protocol}";

    private static string VersionField(VersionCatalogEntry e) =>
        "s_v" + e.Protocol.ToString(CultureInfo.InvariantCulture);

    // JavaVersions: the normative lazy no-cctor shape

    private void EmitCatalog()
    {
        StringBuilder sb = new();
        Header(sb, "Umpk.Data.Java");
        sb.AppendLine("using Umpk.Protocol.Java;");
        sb.AppendLine();
        sb.AppendLine("namespace Umpk.Data.Java;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Generated version catalog. Implements the IVersionCatalog seam.</summary>");
        sb.AppendLine("/// <remarks>Trimming-load-bearing shape (normative): no static constructor and no static field initializers. Every V* property lazily constructs through its own backing field so the trimmer can drop unreferenced versions. All and TryGetBy* obey the same rule: lazy bodies, no static initializers, so touching one V* property never roots the whole catalog.</remarks>");
        sb.AppendLine("public static class JavaVersions");
        sb.AppendLine("{");

        List<VersionCatalogEntry> ordered = [.. _dataset.Versions.OrderBy(v => v.Protocol)];
        // Names map N:1 onto protocol numbers (1.21.2/1.21.3 are both 768, 1.20.3/1.20.4 are both 765). Every name gets an ergonomic public V* property, but all names of one protocol share a single backing field declared once, and the All/BuildAll roots are emitted once per protocol, so N:1 catalogs never produce duplicate field definitions.
        HashSet<int> fieldsDeclared = [];
        foreach (VersionCatalogEntry e in ordered)
        {
            string field = VersionField(e);
            string propName = NameToProperty(e.Name);
            sb.AppendLine($"    /// <summary>{e.Name} (protocol {e.Protocol}).</summary>");
            sb.AppendLine($"    public static JavaVersion {propName} => {field} ??= global::Umpk.Data.Java.{Member(e.Protocol)}.Descriptor.Build();");
            if (fieldsDeclared.Add(e.Protocol))
                sb.AppendLine($"    private static JavaVersion? {field};");

            sb.AppendLine();
        }

        // One catalog entry per protocol (first name), so All has no duplicate protocols.
        List<VersionCatalogEntry> distinct = [.. ordered
            .GroupBy(v => v.Protocol)
            .Select(g => g.First())
            .OrderBy(v => v.Protocol)];

        sb.AppendLine("    /// <summary>All supported versions. References every version and therefore roots the full data set.</summary>");
        sb.AppendLine("    public static IReadOnlyList<JavaVersion> All => s_all ??= BuildAll();");
        sb.AppendLine("    private static IReadOnlyList<JavaVersion>? s_all;");
        sb.AppendLine();
        sb.AppendLine("    private static IReadOnlyList<JavaVersion> BuildAll() =>");
        sb.Append("        new JavaVersion[] { ");
        sb.Append(string.Join(", ", distinct.Select(e => NameToProperty(e.Name))));
        sb.AppendLine(" };");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Look up a version by protocol number. Walks All lazily.</summary>");
        sb.AppendLine("    public static bool TryGetByProtocol(int protocol, out JavaVersion version)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (JavaVersion candidate in All)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (candidate.Version.Protocol == protocol)");
        sb.AppendLine("            {");
        sb.AppendLine("                version = candidate;");
        sb.AppendLine("                return true;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        version = default!;");
        sb.AppendLine("        return false;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Look up a version by name (\"1.21.5\"). Resolves N:1 aliases (\"1.20.3\" and \"1.20.4\" both map to protocol 765) via the generated name table, which the single-name <c>JavaVersion.HasName</c> cannot express.</summary>");
        sb.AppendLine("    public static bool TryGetByName(string name, out JavaVersion version)");
        sb.AppendLine("    {");
        sb.AppendLine("        int protocol = name switch");
        sb.AppendLine("        {");
        foreach (VersionCatalogEntry e in ordered)
            sb.AppendLine($"            \"{e.Name}\" => {e.Protocol},");

        sb.AppendLine("            _ => -1,");
        sb.AppendLine("        };");
        sb.AppendLine("        return protocol >= 0 && TryGetByProtocol(protocol, out version) || Fail(out version);");
        sb.AppendLine("        static bool Fail(out JavaVersion v) { v = default!; return false; }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        Add("JavaVersions.g.cs", sb.ToString());
    }

    private static string NameToProperty(string name) => "V" + name.Replace('.', '_').Replace('-', '_');

    // Per-version descriptor

    private void EmitDescriptor(VersionData version)
    {
        string ns = $"Umpk.Data.Java.{Member(version.Protocol)}";
        StringBuilder sb = new();
        Header(sb, ns);
        sb.AppendLine("using Umpk;");
        sb.AppendLine("using Umpk.Protocol.Java;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>Generated descriptor for protocol {version.Protocol} ({version.VersionName}).</summary>");
        sb.AppendLine("internal static class Descriptor");
        sb.AppendLine("{");
        sb.AppendLine("    public static JavaVersion Build()");
        sb.AppendLine("    {");
        sb.AppendLine($"        var gameVersion = new GameVersion(GameEdition.Java, \"{version.VersionName}\", {version.Protocol});");
        sb.AppendLine("        ProtocolFeatures features = BuildFeatures();");
        sb.AppendLine("        var builder = new ProtocolDescriptorBuilder(gameVersion, features);");
        EmitPacketRegistration(version, sb);
        sb.AppendLine("        return new JavaVersion(gameVersion, builder.Build(), features);");
        sb.AppendLine("    }");
        sb.AppendLine();

        EmitMetadataRegistration(version, sb);
        EmitFeatures(version, sb);
        EmitTableReferences(version, sb);

        sb.AppendLine("}");
        Add($"{Member(version.Protocol)}/Descriptor.g.cs", sb.ToString());
    }

    private static void EmitPacketRegistration(VersionData version, StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("        // Registered packet ids, per phase and flow. PacketRegistrar resolves each to the codec its timeline binds at this protocol, or to a NotImplemented marker.");
        foreach ((string phase, IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>> flows)
                 in version.Packets.Phases.OrderBy(kv => kv.Key))
            foreach ((string flow, IReadOnlyList<PacketEntry> packets) in flows.OrderBy(kv => kv.Key))
                foreach (PacketEntry p in packets.OrderBy(p => p.ProtocolId))
                    sb.AppendLine($"        PacketRegistrar.Register(builder, ProtocolPhase.{Pascal(phase)}, PacketFlow.{Pascal(flow)}, 0x{p.ProtocolId:X2}, \"{p.Id}\");");

    }

    private static void EmitMetadataRegistration(VersionData version, StringBuilder sb)
    {
        sb.AppendLine("    // Entity metadata serializer wire order (readable table).");
        sb.AppendLine($"    public static readonly string MetadataTerminator = \"{version.Metadata.Terminator}\";");
        sb.AppendLine("    public static readonly (int Id, string Codec)[] MetadataSerializers =");
        sb.AppendLine("    {");
        foreach (MetadataEntry s in version.Metadata.Serializers)
            sb.AppendLine($"        ({s.Id}, \"{s.Codec}\"),");

        sb.AppendLine("    };");
        sb.AppendLine();
    }

    // The subset of features.json flags the ProtocolFeatures record models. Unknown/extra flags in the dataset (for example physics-only toggles) are intentionally not emitted here.
    private static readonly Dictionary<string, string> FeatureProperties = new(StringComparer.Ordinal)
    {
        ["configurationPhase"] = "ConfigurationPhase",
        ["chatSigning"] = "ChatSigning",
        ["nbtWireFormat"] = "NbtWireFormat",
        ["blockPosLayout"] = "BlockPosLayout",
        ["fluidMovement"] = "FluidMovement",
        ["waterTravel"] = "WaterTravel",
        ["waterClimbBump"] = "WaterClimbBump",
        ["elytra"] = "Elytra",
        ["swimPose"] = "SwimPose",
        ["crawlPose"] = "CrawlPose",
        ["argumentTypeEra"] = "ArgumentTypeEra",
        ["componentInteractionEra"] = "ComponentInteractionEra",
    };

    private static void EmitFeatures(VersionData version, StringBuilder sb)
    {
        sb.AppendLine("    private static ProtocolFeatures BuildFeatures() => new ProtocolFeatures");
        sb.AppendLine("    {");
        foreach ((string flag, System.Text.Json.JsonElement value) in version.Features.Flags.OrderBy(kv => kv.Key))
        {
            if (!FeatureProperties.TryGetValue(flag, out string? property))
                continue;

            string literal = value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                System.Text.Json.JsonValueKind.String => $"\"{value.GetString()}\"",
                System.Text.Json.JsonValueKind.Number => value.GetRawText(),
                _ => "default",
            };
            sb.AppendLine($"        {property} = {literal},");
        }
        sb.AppendLine("    };");
        sb.AppendLine();
    }

    private void EmitTableReferences(VersionData version, StringBuilder sb)
    {
        // Registry name lists and shape blobs are emitted as shared, content-deduplicated members;
        // the descriptor just references them by the earliest protocol that owns each blob.
        string itemsMember = InternTable("items", version.Protocol, version.Items.Select(i => i.Name));
        string blocksMember = InternTable("blocks", version.Protocol,
            version.Blocks.Blocks.Select(b => b.Name ?? "").Where(n => n.Length > 0));
        string blockDefsMember = InternBlockDefs(version.Protocol, version.Blocks);
        string entitiesMember = InternEntities(version.Protocol, version.Entities);
        // (object-space table emitted below, after the flat/mob one)
        string shapesMember = InternShapes(version.Protocol, version.Shapes);

        sb.AppendLine("    // Table references (shared across versions by content; Vn names the earliest owning protocol).");
        sb.AppendLine($"    public static ReadOnlySpan<byte> ItemNames => SharedTables.{itemsMember};");
        if (version.Identity == "legacy")
        {
            // Pre-flattening item identity is a composite, not a position, so those versions carry a second table alongside the flat name list. Flattened versions have no such table.
            string legacyItemsMember = InternLegacyItemDefs(version.Protocol, version.Items);
            sb.AppendLine($"    public static ReadOnlySpan<byte> LegacyItemDefs => SharedTables.{legacyItemsMember};");
        }
        sb.AppendLine($"    public static ReadOnlySpan<byte> BlockNames => SharedTables.{blocksMember};");
        sb.AppendLine($"    public static ReadOnlySpan<byte> BlockDefs => SharedTables.{blockDefsMember};");
        sb.AppendLine($"    public static ReadOnlySpan<byte> EntityNames => SharedTables.{entitiesMember};");

        // Sound-event names, indexed by network id, for the id-addressed sound packets. Empty for the protocols whose dataset carries no minecraft:sound_event registry (1.8 through 1.13.2): those eras address sounds by NAME on the wire anyway, so nothing needs the table to resolve them.
        string soundsMember = InternTable("sounds", version.Protocol, SoundNamesByNetworkId(version));
        sb.AppendLine($"    public static ReadOnlySpan<byte> SoundNames => SharedTables.{soundsMember};");

        // Pre-1.14 versions spawn objects through a SECOND, disjoint id space (SpawnObject), so those datasets carry a second packed table alongside the mob one. Flat versions merged the spaces into the single entity_type registry, so their table is empty; the blob interner folds every empty table within this semantic kind onto one shared member, and emitting it unconditionally keeps the accessor uniform across protocols.
        string objectsMember = InternEntities(version.Protocol, version.ObjectEntities, "objectentities");
        sb.AppendLine($"    public static ReadOnlySpan<byte> LegacyObjectEntityNames => SharedTables.{objectsMember};");

        // The minecraft:menu table. Emitted for every protocol: 1.14+ carries numeric registry ids,
        // while pre-1.14 carries the string window types the
        // open_screen packet names. Both eras land in one registry keyed the era's own way.
        string menusMember = InternMenus(version.Protocol, version.Menus);
        sb.AppendLine($"    public static ReadOnlySpan<byte> MenuDefs => SharedTables.{menusMember};");
        sb.AppendLine($"    public static ReadOnlySpan<byte> CollisionShapes => SharedTables.{shapesMember};");

        // The pool above is inert on its own: this is the table that maps a block state (flattened bands) or a block network id (pre-flattening bands) onto a pool index. Emitted for every protocol so the accessor is uniform.
        string shapeRefsMember = InternShapeRefs(version);
        sb.AppendLine($"    public static ReadOnlySpan<byte> BlockShapeRefs => SharedTables.{shapeRefsMember};");

        // The per-block attribute table: physics scalars, per-state semantic flags, and block-state property names with their value domains where the resolver could prove them.
        string attrsMember = InternBlockAttrs(version, _dataset.Shared.BlockAttributes);
        sb.AppendLine($"    public static ReadOnlySpan<byte> BlockAttrs => SharedTables.{attrsMember};");

        // The per-block piston facts. Emitted for every protocol so the accessor is uniform, but EMPTY wherever the dataset carries no measurement: the reader turns an empty table into "no data", and the client then models only the piston's own head and base.
        string pushMember = InternBlockPush(version);
        sb.AppendLine($"    public static ReadOnlySpan<byte> BlockPush => SharedTables.{pushMember};");

        // The two identity registries the client resolves wire holder ids against. Emitted for every protocol so the accessor is uniform, and EMPTY wherever the dataset carries no entries for that registry, which the reader treats as unresolvable.
        string enchantmentsMember = InternRegistryDefs(version, "minecraft:enchantment", "enchantments");
        sb.AppendLine($"    public static ReadOnlySpan<byte> EnchantmentDefs => SharedTables.{enchantmentsMember};");
        string mobEffectsMember = InternRegistryDefs(version, "minecraft:mob_effect", "mobeffects");
        sb.AppendLine($"    public static ReadOnlySpan<byte> MobEffectDefs => SharedTables.{mobEffectsMember};");

        // minecraft:attribute, the third such registry, and the only one carrying VALUES rather than pure identity. See InternAttributeDefs for the value join and why it is keyed by raw name.
        string attributesMember = InternAttributeDefs(version);
        sb.AppendLine($"    public static ReadOnlySpan<byte> AttributeDefs => SharedTables.{attributesMember};");
    }

    /// <summary>Packs one registry as an identity table: <c>[VarInt count][per entry: VarInt id, string name]</c>, the same layout <see cref="InternEntities"/> uses. The dataset carries only <c>{name, id}</c> for these registries, so identity is all there is to pack; the client builds definitions at their declared defaults, which both <c>EnchantmentDefinition</c> and <c>MobEffectDefinition</c> supply.</summary>
    /// <remarks>
    /// <para>A protocol with no such registry emits an empty table, which the blob interner folds onto the first empty member of the same semantic kind. Coverage over the 49-protocol dataset: <c>minecraft:enchantment</c> exists on 477-766 only, <c>minecraft:mob_effect</c> on 47 and 477-776.</para>
    /// <para>The enchantment gap at 767+ follows a vanilla protocol change, not missing data. From 1.21, the server sends the enchantment registry during configuration. A generated static table would therefore be wrong, so the generator emits none.</para>
    /// <para><c>minecraft:attribute</c> is the third registry of this shape but does NOT ride this method: <c>AttributeDefinition(double DefaultValue, double MinValue, double MaxValue, bool IsRanged)</c> carries values the client genuinely consumes (<c>AttributeInstance</c> seeds <c>BaseValue</c> from <c>DefaultValue</c> and clamps into <c>[MinValue, MaxValue]</c>). Those values come from the attribute-default table; see <see cref="InternAttributeDefs"/>.</para>
    /// </remarks>
    private string InternRegistryDefs(VersionData version, string registryId, string kind)
    {
        IReadOnlyList<RegistryItem> entries =
            version.Registries.TryGetValue(registryId, out IReadOnlyList<RegistryItem>? found) ? found : [];

        using MemoryStream ms = new();
        WriteVarInt(ms, entries.Count);
        foreach (RegistryItem entry in entries)
        {
            WriteVarInt(ms, entry.Id);
            byte[] utf8 = Encoding.UTF8.GetBytes(entry.Name);
            WriteVarInt(ms, utf8.Length);
            ms.Write(utf8);
        }
        return InternBlob(kind, version.Protocol, ms.ToArray());
    }

    /// <summary>Packs <c>minecraft:attribute</c> as an identity table WITH VALUES: <c>[VarInt count][per entry: VarInt id, string name, double default, double min, double max, byte isRanged]</c>. The id and the name come from the protocol registry; the three doubles come from the attribute-default table, joined by the raw registry name.</summary>
    /// <remarks>
    /// <para>The join key is the raw name (<c>minecraft:generic.movement_speed</c> on 766/767, <c>minecraft:movement_speed</c> from 768), never a prefix-stripped canonical one, because two vanilla attributes collide under canonicalisation ACROSS eras with different numbers: <c>minecraft:horse.jump_strength</c> is <c>0.7</c> in <c>[0,2]</c>, while <c>minecraft:generic.jump_strength</c> is <c>0.42F</c> in <c>[0,32]</c>. A canonically-keyed table would emit one of those two wrongly on every protocol of the other era, and the verify gate below would stay green while it did, because it would be asserting over the same collapsed key. Canonicalisation belongs at LOOKUP time in the client (<c>Umpk.Game.Entities.AttributeIds</c>), not at emit time.</para>
    /// <para>A raw name with no definition row is a hard failure, not a zero-filled definition: emitting <c>[0,0]</c> would clamp that attribute to zero forever, which is strictly worse than the empty unresolved registry result. A protocol that adds an attribute therefore breaks the build and requires its value range to be supplied explicitly.</para>
    /// <para>Protocols 735-776 carry <c>minecraft:attribute</c>; the rest emit an empty table that the interner folds onto the first empty attribute member. That is correct rather than missing - 47-578's <c>update_attributes</c> names its attributes by STRING and needs no registry at all.</para>
    /// </remarks>
    private string InternAttributeDefs(VersionData version)
    {
        IReadOnlyList<RegistryItem> entries =
            version.Registries.TryGetValue("minecraft:attribute", out IReadOnlyList<RegistryItem>? found) ? found : [];

        using MemoryStream ms = new();
        Span<byte> buf = stackalloc byte[8];
        WriteVarInt(ms, entries.Count);
        foreach (RegistryItem entry in entries)
        {
            AttributeDefaultRow row = _dataset.Shared.AttributeDefaults.Require(entry.Name, version.Protocol);
            WriteVarInt(ms, entry.Id);
            byte[] utf8 = Encoding.UTF8.GetBytes(entry.Name);
            WriteVarInt(ms, utf8.Length);
            ms.Write(utf8);
            BinaryPrimitives.WriteDoubleBigEndian(buf, row.Default);
            ms.Write(buf);
            BinaryPrimitives.WriteDoubleBigEndian(buf, row.Min);
            ms.Write(buf);
            BinaryPrimitives.WriteDoubleBigEndian(buf, row.Max);
            ms.Write(buf);
            ms.WriteByte(row.IsRanged ? (byte)1 : (byte)0);
        }
        return InternBlob("attributes", version.Protocol, ms.ToArray());
    }

    // Content-hash dedup + blob packing

    /// <summary>The sound-event registry as a POSITIONAL name list: index == network id. The registry is emitted dense, with any gap filled by an empty name, because the reader indexes straight into it.</summary>
    private static IEnumerable<string> SoundNamesByNetworkId(VersionData version)
    {
        if (!version.Registries.TryGetValue("minecraft:sound_event", out IReadOnlyList<RegistryItem>? sounds)
            || sounds.Count == 0)
            return [];

        int max = sounds.Max(s => s.Id);
        string[] table = new string[max + 1];
        Array.Fill(table, string.Empty);
        foreach (RegistryItem sound in sounds)
            table[sound.Id] = sound.Name;

        return table;
    }

    private string InternTable(string kind, int protocol, IEnumerable<string> names)
    {
        // Length-prefixed UTF-8 name list, packed into a byte blob.
        List<string> list = [.. names];
        byte[] blob = PackStringList(list);
        return InternBlob(kind, protocol, blob);
    }

    private string InternBlockDefs(int protocol, BlockTable table)
    {
        // Packed block-definition table: [VarInt count][per block: string name, VarInt blockId, VarInt minState, VarInt maxState, VarInt defaultState]. Handles both eras:
        //   flat   -> name/blockId/min/max/default present on the entry.
        //   legacy -> {block_id, meta, state_id, material}: min=max=default=state_id, name=material.
        // One row per block_id in the data (legacy block_ids are unique); JavaGameData de-dups on read.
        List<(string Name, int BlockId, int Min, int Max, int Default)> defs = [];
        foreach (BlockEntry b in table.Blocks)
        {
            if (b.Name is not { Length: > 0 } name || b.BlockId is not int blockId)
                continue;

            if (b.MinState is int min && b.MaxState is int max && b.DefaultState is int def)
                defs.Add((name, blockId, min, max, def));

            else if (b.StateId is int state)
                defs.Add((name, blockId, state, state, state));

        }

        using MemoryStream ms = new();
        WriteVarInt(ms, defs.Count);
        foreach ((string name, int blockId, int min, int max, int def) in defs)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(name);
            WriteVarInt(ms, utf8.Length);
            ms.Write(utf8);
            WriteVarInt(ms, blockId);
            WriteVarInt(ms, min);
            WriteVarInt(ms, max);
            WriteVarInt(ms, def);
        }
        return InternBlob("blockdefs", protocol, ms.ToArray());
    }

    /// <summary>Packs the per-block attribute table that answers everything <c>BlockDefs</c> does not: physics scalars, per-state semantic flags, and the block-state property names with their VALUE domains where those could be proven. Kept as a second table rather than folded into <c>BlockDefs</c> so the existing table's shape (and every consumer of it) is untouched.</summary>
    /// <remarks>Layout: [VarInt poolCount][pooled UTF-8 strings][VarInt blockCount][per block: VarInt blockId, VarInt friction*1000, VarInt speedFactor*1000, VarInt jumpFactor*1000, VarInt baseFlags, VarInt propertyCount, [VarInt nameIndex, VarInt valueCount, VarInt valueIndex * valueCount]*, VarInt perStateMode, [VarInt stateCount, VarInt flags * stateCount]?]. The strings are pooled because property names and values repeat across almost every block. A property with valueCount 0 means "this name is real, its values could not be proven": the honest degradation the resolver falls back to. perStateMode 0 means every state carries baseFlags, which is the common case; mode 1 carries the per-state list for blocks whose collision or waterlogging varies between states.</remarks>
    private string InternBlockAttrs(VersionData version, BlockAttributeTable attributes)
    {
        List<string> pool = [];
        Dictionary<string, int> poolIndex = new(StringComparer.Ordinal);
        int Intern(string s)
        {
            if (poolIndex.TryGetValue(s, out int existing))
                return existing;

            poolIndex[s] = pool.Count;
            pool.Add(s);
            return pool.Count - 1;
        }

        List<BlockAttributeRow> rows = [.. ResolveBlockAttributes(version, attributes)];
        foreach (BlockAttributeRow row in rows)
            foreach ((string name, IReadOnlyList<string> values) in row.Properties)
            {
                Intern(name);
                foreach (string value in values)
                    Intern(value);

            }

        using MemoryStream ms = new();
        WriteVarInt(ms, pool.Count);
        foreach (string s in pool)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(s);
            WriteVarInt(ms, utf8.Length);
            ms.Write(utf8);
        }

        WriteVarInt(ms, rows.Count);
        foreach (BlockAttributeRow row in rows)
        {
            WriteVarInt(ms, row.BlockId);
            WriteVarInt(ms, Milli(row.Friction));
            WriteVarInt(ms, Milli(row.SpeedFactor));
            WriteVarInt(ms, Milli(row.JumpFactor));
            WriteVarInt(ms, row.BaseFlags);
            WriteVarInt(ms, row.Properties.Count);
            foreach ((string name, IReadOnlyList<string> values) in row.Properties)
            {
                WriteVarInt(ms, poolIndex[name]);
                WriteVarInt(ms, values.Count);
                foreach (string value in values)
                    WriteVarInt(ms, poolIndex[value]);

            }

            if (row.StateFlags is { Count: > 0 } stateFlags)
            {
                WriteVarInt(ms, 1);
                WriteVarInt(ms, stateFlags.Count);
                foreach (int flags in stateFlags)
                    WriteVarInt(ms, flags);

            }
            else
                WriteVarInt(ms, 0);

        }

        return InternBlob("blockattrs", version.Protocol, ms.ToArray());
    }

    /// <summary>The piston-push table: <c>[VarInt blockCount][blockCount VarInt rows]</c>, one row per block in the version's own block order, so the reader indexes it by block network id.</summary>
    /// <remarks>
    /// <para>A row is <c>reaction | (unbreakable &lt;&lt; 3) | (hasBlockEntity &lt;&lt; 4)</c>, and the reaction numbering is: NORMAL, DESTROY, BLOCK, IGNORE, PUSH_ONLY.</para>
    /// <para><c>blockCount</c> ZERO is the "no measurement for this protocol" marker, and it is not the same statement as "every block is normal". Only protocols with data carry a <c>block-push.json</c>; the rest emit the empty table and the client keeps modeling only the piston's own head and base there.</para>
    /// </remarks>
    private string InternBlockPush(VersionData version)
    {
        using MemoryStream ms = new();
        if (version.Push is not { } push)
        {
            WriteVarInt(ms, 0);
            return InternBlob("blockpush", version.Protocol, ms.ToArray());
        }

        List<BlockEntry> blocks = [.. version.Blocks.Blocks];
        WriteVarInt(ms, blocks.Count);
        foreach (BlockEntry block in blocks)
        {
            string name = block.Name ?? string.Empty;
            int reaction = push.Reactions.TryGetValue(name, out string? value)
                ? ReactionOrdinal(value)
                : ReactionOrdinal(push.DefaultReaction);
            int packed = reaction
                | (push.Unbreakable.Contains(name) ? 1 << 3 : 0)
                | (push.BlockEntity.Contains(name) ? 1 << 4 : 0);
            WriteVarInt(ms, packed);
        }

        return InternBlob("blockpush", version.Protocol, ms.ToArray());
    }

    /// <summary>Vanilla <c>PushReaction</c>'s own declaration order.</summary>
    private static int ReactionOrdinal(string name) => name switch
    {
        "NORMAL" => 0,
        "DESTROY" => 1,
        "BLOCK" => 2,
        "IGNORE" => 3,
        "PUSH_ONLY" => 4,
        _ => throw new DatasetException($"unknown PushReaction '{name}'"),
    };

    private static int Milli(double value) => (int)Math.Round(value * 1000.0, MidpointRounding.AwayFromZero);

    private string InternLegacyItemDefs(int protocol, IReadOnlyList<RegistryItem> items)
    {
        // Packed legacy item table: [VarInt count][per item: string name, VarInt compositeKey]. Pre-flattening item identity is the (item_id << 16) | damage composite, which the flat, positionally indexed ItemNames table cannot express (position != composite). DatasetLoader already carries the composite in RegistryItem.Id for legacy datasets, so this table is the same list with the key kept. JavaGameData de-dups composites and identifiers on read, the way it does for the both-era block table.
        using MemoryStream ms = new();
        WriteVarInt(ms, items.Count);
        foreach (RegistryItem item in items)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(item.Name);
            WriteVarInt(ms, utf8.Length);
            ms.Write(utf8);
            WriteVarInt(ms, item.Id);
        }
        return InternBlob("legacyitems", protocol, ms.ToArray());
    }

    private string InternEntities(int protocol, IReadOnlyList<EntityEntry> entities, string kind = "entities")
    {
        // Packed entity-type table: [VarInt count][per entity: VarInt idNum, string name]. Width/Height are null in the data and defaulted at read time, so they are not packed.
        using MemoryStream ms = new();
        WriteVarInt(ms, entities.Count);
        foreach (EntityEntry e in entities)
        {
            WriteVarInt(ms, e.IdNum);
            byte[] utf8 = Encoding.UTF8.GetBytes(e.Name);
            WriteVarInt(ms, utf8.Length);
            ms.Write(utf8);
        }
        return InternBlob(kind, protocol, ms.ToArray());
    }

    private string InternMenus(int protocol, IReadOnlyList<MenuEntry> menus)
    {
        // Packed menu-type table: [VarInt count][per menu: VarInt idNum, string name, string wireId, VarInt slots+1]. wireId is the empty string when the wire carries the number instead of a string (1.14+), and slots is packed biased by one so 0 encodes "no static slot count" without needing a separate presence flag.
        using MemoryStream ms = new();
        WriteVarInt(ms, menus.Count);
        foreach (MenuEntry m in menus)
        {
            WriteVarInt(ms, m.IdNum);
            byte[] name = Encoding.UTF8.GetBytes(m.Name);
            WriteVarInt(ms, name.Length);
            ms.Write(name);
            byte[] wire = Encoding.UTF8.GetBytes(m.WireId ?? "");
            WriteVarInt(ms, wire.Length);
            ms.Write(wire);
            WriteVarInt(ms, (m.Slots ?? -1) + 1);
        }
        return InternBlob("menus", protocol, ms.ToArray());
    }

    private string InternShapes(int protocol, ShapeTable shapes)
    {
        // Pack the AABB pool: [count][per-shape: boxCount][6 doubles each]. Coordinates are written BIG-endian so the runtime reads them with the same PacketReader every other table uses. Explicit byte order keeps generated blobs identical across host architectures.
        using MemoryStream ms = new();
        Span<byte> buf = stackalloc byte[8];
        WriteVarInt(ms, shapes.Shapes.Count);
        foreach (IReadOnlyList<IReadOnlyList<double>> shape in shapes.Shapes)
        {
            WriteVarInt(ms, shape.Count);
            foreach (IReadOnlyList<double> box in shape)
                foreach (double coord in box)
                {
                    BinaryPrimitives.WriteDoubleBigEndian(buf, coord);
                    ms.Write(buf);
                }

        }
        return InternBlob("shapes", protocol, ms.ToArray());
    }

    /// <summary>Packs the table that says WHICH pooled shape each block state uses. Without it the emitted <c>CollisionShapes</c> pool is unusable: a bag of AABB lists with nothing pointing into it, which is why every consumer fell back to a unit cube.</summary>
    /// <remarks>
    /// Layout: <c>[VarInt kind][VarInt count][VarInt value * count]</c>.
    /// <list type="bullet">
    /// <item><c>kind 0</c> (flattened, 1.13+): the array is indexed by BLOCK STATE id. The flat state
    /// space is gapless, so the array spans 0..maxState.</item>
    /// <item><c>kind 1</c> (pre-flattening, no meta supplement): the array is indexed by BLOCK NETWORK
    /// id, because the dataset carries one shape per block id (its meta-0 shape) and not one per <c>(id &lt;&lt; 4) | meta</c> state. The reader resolves it with <c>stateId &gt;&gt; 4</c>, so all sixteen metas of a block share one box.</item>
    /// <item><c>kind 2</c> (pre-flattening WITH a meta supplement): the per-block-id array exactly as
    /// kind 1, followed by <c>[VarInt overrideCount][VarInt stateId][VarInt value]*</c> naming the individual <c>(id &lt;&lt; 4) | meta</c> states whose collision differs from their block's meta-0 shape. Every other meta inherits the block's value, so no state can become uncovered by the supplement. Kinds 1 and 2 are distinct numbers rather than a presence heuristic: a reader that does not know kind 2 must reject it outright instead of silently reading the override block as more base entries.</item>
    /// </list>
    /// A value of 0 means "this dataset names no shape for this index" and the reader degrades to its flag-derived fallback; any other value is <c>poolIndex + 1</c>. The bias is what keeps "uncovered" distinguishable from "covered by the empty shape at pool index 0", which is the difference between a block nobody measured and a flower you can walk through.
    /// </remarks>
    private string InternShapeRefs(VersionData version)
    {
        int kind;
        List<int> values = [];
        List<(int StateId, int Value)> overrides = [];
        if (version.Shapes.Collision is { } collision)
        {
            kind = 0;
            int maxState = -1;
            foreach (BlockEntry b in version.Blocks.Blocks)
                if (b.MaxState is int max && max > maxState)
                    maxState = max;

            for (int i = 0; i <= maxState; i++)
                values.Add(0);

            foreach (BlockEntry b in version.Blocks.Blocks)
            {
                if (b.Name is not { Length: > 0 } name
                    || b.MinState is not int minState
                    || !collision.TryGetValue(name, out IReadOnlyList<int>? indices))
                    continue;

                for (int i = 0; i < indices.Count; i++)
                {
                    int state = minState + i;
                    if (state >= 0 && state < values.Count)
                        values[state] = indices[i] + 1;

                }
            }
        }
        else if (version.Shapes.CollisionByBlockId is { } byBlockId)
        {
            kind = 1;
            int maxBlockId = -1;
            foreach (string key in byBlockId.Keys)
                if (int.TryParse(key, CultureInfo.InvariantCulture, out int blockId) && blockId > maxBlockId)
                    maxBlockId = blockId;

            for (int i = 0; i <= maxBlockId; i++)
                values.Add(0);

            foreach ((string key, int index) in byBlockId)
                if (int.TryParse(key, CultureInfo.InvariantCulture, out int blockId)
                    && blockId >= 0
                    && blockId < values.Count)
                    values[blockId] = index + 1;

            if (version.Shapes.CollisionByBlockState is { Count: > 0 } byBlockState)
            {
                kind = 2;
                foreach ((string key, int index) in byBlockState)
                    if (int.TryParse(key, CultureInfo.InvariantCulture, out int stateId) && stateId >= 0)
                        overrides.Add((stateId, index + 1));

                overrides.Sort(static (a, b) => a.StateId.CompareTo(b.StateId));
            }
        }
        else
            kind = 0;

        using MemoryStream ms = new();
        WriteVarInt(ms, kind);
        WriteVarInt(ms, values.Count);
        foreach (int value in values)
            WriteVarInt(ms, value);

        if (kind == 2)
        {
            WriteVarInt(ms, overrides.Count);
            foreach ((int stateId, int value) in overrides)
            {
                WriteVarInt(ms, stateId);
                WriteVarInt(ms, value);
            }
        }

        return InternBlob("shaperefs", version.Protocol, ms.ToArray());
    }

    private string InternBlob(string kind, int protocol, byte[] blob)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(blob));
        var key = (kind, hash);
        if (_dedup.TryGetValue(key, out string? existing))
        {
            if (!BlobFor(existing).AsSpan().SequenceEqual(blob))
                throw new InvalidOperationException($"SHA-256 collision while interning {kind} for protocol {protocol}.");

            return existing;
        }

        string member = $"{SemanticTableName(kind)}V{protocol.ToString(CultureInfo.InvariantCulture)}";
        if (_blobs.ContainsKey(member) || _aliases.ContainsKey(member))
            throw new InvalidOperationException($"Duplicate shared-table member name '{member}'.");

        _dedup[key] = member;
        if (_contentDedup.TryGetValue(hash, out string? canonical))
        {
            if (!_blobs[canonical].AsSpan().SequenceEqual(blob))
                throw new InvalidOperationException($"SHA-256 collision while interning {kind} for protocol {protocol}.");

            _aliases[member] = canonical;
        }
        else
        {
            _contentDedup[hash] = member;
            _blobs[member] = blob;
            _sharedCounter++;
        }

        return member;
    }

    private byte[] BlobFor(string member) =>
        _blobs.TryGetValue(member, out byte[]? blob) ? blob : _blobs[_aliases[member]];

    private static string SemanticTableName(string kind) => kind switch
    {
        "items" => "Items",
        "legacyitems" => "LegacyItems",
        "blocks" => "Blocks",
        "blockdefs" => "BlockDefinitions",
        "entities" => "Entities",
        "sounds" => "Sounds",
        "objectentities" => "LegacyObjectEntities",
        "menus" => "Menus",
        "shapes" => "CollisionShapes",
        "shaperefs" => "BlockShapeReferences",
        "blockattrs" => "BlockAttributes",
        "blockpush" => "BlockPush",
        "enchantments" => "Enchantments",
        "mobeffects" => "MobEffects",
        "attributes" => "Attributes",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown shared-table kind."),
    };

    private readonly Dictionary<string, byte[]> _blobs = [];
    private readonly Dictionary<string, string> _aliases = [];

    private void EmitSharedTables()
    {
        StringBuilder sb = new();
        Header(sb, "Umpk.Data.Java");
        sb.AppendLine("namespace Umpk.Data.Java;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Content-deduplicated binary tables. Each member is a ReadOnlySpan&lt;byte&gt; over an embedded blob; identical tables of one semantic kind across versions collapse to the member named for their earliest owning protocol. Identical bytes used by different semantic kinds retain readable alias members but share one embedded blob. Index structs over these blobs live in Umpk.Data.Java.</summary>");
        sb.AppendLine("internal static class SharedTables");
        sb.AppendLine("{");
        foreach ((string member, byte[] blob) in _blobs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append($"    public static ReadOnlySpan<byte> {member} => new byte[] {{ ");
            sb.Append(string.Join(", ", blob.Select(b => "0x" + b.ToString("X2", CultureInfo.InvariantCulture))));
            sb.AppendLine(" };");
        }
        foreach ((string member, string canonical) in _aliases.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.AppendLine($"    public static ReadOnlySpan<byte> {member} => {canonical};");

        sb.AppendLine("}");
        Add("SharedTables.g.cs", sb.ToString());
    }

    public int SharedTableCount => _sharedCounter;

    // helpers

    private static byte[] PackStringList(IReadOnlyList<string> list)
    {
        using MemoryStream ms = new();
        WriteVarInt(ms, list.Count);
        foreach (string s in list)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(s);
            WriteVarInt(ms, utf8.Length);
            ms.Write(utf8);
        }
        return ms.ToArray();
    }

    private static void WriteVarInt(Stream stream, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            stream.WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        stream.WriteByte((byte)v);
    }

    private static string Pascal(string snakeOrCamel)
    {
        string[] parts = snakeOrCamel.Split('_');
        if (parts.Length == 1)
            return char.ToUpperInvariant(snakeOrCamel[0]) + snakeOrCamel[1..];

        return string.Concat(parts.Select(p => p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static void Header(StringBuilder sb, string _)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated by Umpk.DataGen from data/java. Do not edit by hand.");
        sb.AppendLine("// Regenerate: dotnet run --project tools/Umpk.DataGen -- generate --data data/java --out src/Umpk.Data.Java");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
    }

    private void Add(string path, string text) => _files[path] = new EmittedFile(path, text);
}
