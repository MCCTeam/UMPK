using Xunit;
namespace Umpk.DataGen.Tests;

public sealed class EmitterTests
{
    [Fact]
    public void JavaVersions_has_no_static_constructor_and_no_field_initializers()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        string catalog = EmitCatalog(builder);

        // Normative lazy no-cctor shape: no static constructor.
        Assert.DoesNotContain("static JavaVersions()", catalog, StringComparison.Ordinal);
        // Backing fields must be declared without initializers (no "= " on the field line).
        Assert.Contains("private static JavaVersion? s_v770;", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain("private static JavaVersion? s_v770 =", catalog, StringComparison.Ordinal);
        // Each version is a lazy property, not a field initializer. The descriptor is referenced by its fully-qualified name so the emitted catalog never depends on a using directive for the per-version sub-namespace.
        Assert.Contains("=> s_v770 ??= global::Umpk.Data.Java.V770.Descriptor.Build();", catalog, StringComparison.Ordinal);
        // All and lookups are lazy.
        Assert.Contains("public static IReadOnlyList<JavaVersion> All => s_all ??= BuildAll();", catalog, StringComparison.Ordinal);
        Assert.Contains("private static IReadOnlyList<JavaVersion>? s_all;", catalog, StringComparison.Ordinal);
        Assert.Contains("TryGetByProtocol", catalog, StringComparison.Ordinal);
        Assert.Contains("TryGetByName", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_exposes_one_property_per_version()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        string catalog = EmitCatalog(builder);
        Assert.Contains("public static JavaVersion V1_8 =>", catalog, StringComparison.Ordinal);
        Assert.Contains("public static JavaVersion V1_21_5 =>", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptor_registers_packets_as_readable_code()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();
        string descriptor = files["V770/Descriptor.g.cs"].Text;
        // Generated descriptors carry only (phase, flow, wire id, identifier, era key); the codec family is resolved by PacketRegistrar, not referenced directly in generated code.
        Assert.Contains("PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, 0x00, \"minecraft:keep_alive\");", descriptor, StringComparison.Ordinal);
        Assert.Contains("BuildFeatures()", descriptor, StringComparison.Ordinal);
        Assert.Contains("MetadataTerminator", descriptor, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_table_members_use_semantic_protocol_names()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();

        string descriptor = files["V47/Descriptor.g.cs"].Text;
        Assert.Contains("ItemNames => SharedTables.ItemsV47;", descriptor, StringComparison.Ordinal);
        Assert.Contains("BlockAttrs => SharedTables.BlockAttributesV47;", descriptor, StringComparison.Ordinal);
        Assert.Contains("EnchantmentDefs => SharedTables.EnchantmentsV47;", descriptor, StringComparison.Ordinal);
        Assert.DoesNotMatch("_[0-9a-f]{16}\\b", files["SharedTables.g.cs"].Text);
    }

    [Fact]
    public void Unrelated_empty_tables_keep_distinct_semantic_members()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();

        string descriptor = files["V47/Descriptor.g.cs"].Text;
        Assert.Contains("EntityNames => SharedTables.EntitiesV47;", descriptor, StringComparison.Ordinal);
        Assert.Contains("SoundNames => SharedTables.SoundsV47;", descriptor, StringComparison.Ordinal);
        Assert.Contains("EnchantmentDefs => SharedTables.EnchantmentsV47;", descriptor, StringComparison.Ordinal);
    }

    [Fact]
    public void Identical_tables_deduplicate_under_the_earliest_protocol_name()
    {
        // Two versions with byte-identical item lists must share one blob member.
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        // Make 47's item names identical to 770's so the packed blobs collide. 770 items are air, stone (names). Rewrite 47 items to the same names.
        builder.Mutate("47/items.json", root =>
        {
            var arr = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject { ["name"] = "minecraft:air", ["id"] = 0 },
                new System.Text.Json.Nodes.JsonObject { ["name"] = "minecraft:stone", ["id"] = 1 });
            root["identity"] = "legacy-composite";
            root["entries"] = arr;
        });

        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();
        string v47 = files["V47/Descriptor.g.cs"].Text;
        string v770 = files["V770/Descriptor.g.cs"].Text;

        string member47 = ExtractItemMember(v47);
        string member770 = ExtractItemMember(v770);
        Assert.Equal(member770, member47);
        Assert.Equal("ItemsV47", member47);
    }

    [Fact]
    public void Distinct_tables_do_not_deduplicate()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();
        string member47 = ExtractItemMember(files["V47/Descriptor.g.cs"].Text);
        string member770 = ExtractItemMember(files["V770/Descriptor.g.cs"].Text);
        // Baseline 47 items are air/stone (no minecraft: prefix) vs 770 minecraft:air/stone.
        Assert.NotEqual(member770, member47);
    }

    [Fact]
    public void Legacy_versions_emit_a_composite_item_table_and_flat_versions_do_not()
    {
        // Pre-flattening item identity is (item_id << 16) | damage, which the positional ItemNames table cannot express, so legacy versions carry a second table pairing each name with its composite.
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();

        Assert.Contains("LegacyItemDefs => SharedTables.LegacyItemsV47", files["V47/Descriptor.g.cs"].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyItemDefs", files["V770/Descriptor.g.cs"].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_item_table_packs_the_composite_key_with_each_name()
    {
        // Baseline 47 items: air (composite 0) and stone (composite 65536 = 1 << 16). The packed form is [VarInt count][per item: VarInt-length UTF-8 name, VarInt compositeKey].
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();
        string member = ExtractMember(files["V47/Descriptor.g.cs"].Text, "LegacyItemDefs => SharedTables.");
        byte[] blob = ExtractBlob(files["SharedTables.g.cs"].Text, member);

        Assert.Equal(
            new byte[]
            {
                0x02,                          // count
                0x03, 0x61, 0x69, 0x72, 0x00,  // "air", composite 0
                0x05, 0x73, 0x74, 0x6F, 0x6E, 0x65, 0x80, 0x80, 0x04, // "stone", composite 65536
            },
            blob);
    }

    [Fact]
    public void Registry_identity_table_packs_the_wire_id_with_each_name()
    {
        // The mob-effect and enchantment registries are emitted as identity tables: [VarInt count][per entry: VarInt wire id, VarInt-length UTF-8 name]. The baseline modern fixture declares minecraft:speed = 0 and minecraft:slowness = 1 under minecraft:mob_effect.
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();
        string member = ExtractMember(files["V770/Descriptor.g.cs"].Text, "MobEffectDefs => SharedTables.");
        byte[] blob = ExtractBlob(files["SharedTables.g.cs"].Text, member);

        Assert.Equal(
            new byte[]
            {
                0x02,                                                       // count
                0x00, 0x0F, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, // id 0, "minecraft:speed"
                0x74, 0x3A, 0x73, 0x70, 0x65, 0x65, 0x64,
                0x01, 0x12, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, // id 1, "minecraft:slowness"
                0x74, 0x3A, 0x73, 0x6C, 0x6F, 0x77, 0x6E, 0x65, 0x73, 0x73,
            },
            blob);
    }

    /// <summary>The attribute table packs values as well as identity: <c>[VarInt count][per entry: VarInt id, VarInt-length UTF-8 name, double default, double min, double max, byte isRanged]</c>. The three doubles are joined from the curated <c>shared/attribute-defaults.json</c> by RAW name, which is why the baseline fixture declares one unprefixed and one <c>generic.</c>-prefixed attribute: both spellings must resolve, and the prefixed one deliberately carries the numbers its own era has rather than its canonical twin's.</summary>
    [Fact]
    public void Attribute_table_packs_the_curated_vanilla_range_with_each_name()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();
        string member = ExtractMember(files["V770/Descriptor.g.cs"].Text, "AttributeDefs => SharedTables.");
        byte[] blob = ExtractBlob(files["SharedTables.g.cs"].Text, member);

        Assert.Equal(
            new byte[]
            {
                0x02,                                                       // count
                0x00, 0x18, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, // id 0, "minecraft:movement_speed"
                0x74, 0x3A, 0x6D, 0x6F, 0x76, 0x65, 0x6D, 0x65, 0x6E, 0x74,
                0x5F, 0x73, 0x70, 0x65, 0x65, 0x64,
                0x3F, 0xE6, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66,             // default 0.7
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // min 0.0
                0x40, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // max 1024.0
                0x01,                                                       // ranged
                0x01, 0x1F, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, // id 1, "minecraft:generic.jump_strength"
                0x74, 0x3A, 0x67, 0x65, 0x6E, 0x65, 0x72, 0x69, 0x63, 0x2E,
                0x6A, 0x75, 0x6D, 0x70, 0x5F, 0x73, 0x74, 0x72, 0x65, 0x6E,
                0x67, 0x74, 0x68,
                0x3F, 0xDA, 0xE1, 0x47, 0xAE, 0x14, 0x7A, 0xE1,             // default 0.42
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // min 0.0
                0x40, 0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // max 32.0
                0x01,                                                       // ranged
            },
            blob);
    }

    [Fact]
    public void Registry_with_no_report_emits_an_empty_identity_table()
    {
        // The baseline fixture declares no minecraft:enchantment on any version, which is exactly the real dataset's shape for 47-404 and 767+. An absent registry must emit an EMPTY table (a bare zero count) rather than be skipped, so the descriptor accessor stays uniform across protocols and the reader turns it into "cannot resolve" instead of a missing member.
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();

        foreach (string descriptor in new[] { "V47/Descriptor.g.cs", "V770/Descriptor.g.cs" })
        {
            string member = ExtractMember(files[descriptor].Text, "EnchantmentDefs => SharedTables.");
            Assert.Equal([0x00], ExtractBlob(files["SharedTables.g.cs"].Text, member));
        }
    }

    [Fact]
    public void Generation_is_deterministic()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        string a = EmitAll(builder);
        string b = EmitAll(builder);
        Assert.Equal(a, b);
    }

    private static string EmitCatalog(DatasetBuilder builder)
    {
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        return emitter.Emit()["JavaVersions.g.cs"].Text;
    }

    private static string EmitAll(DatasetBuilder builder)
    {
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.Emit();
        return string.Join("\n----\n", files.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "\n" + kv.Value.Text));
    }

    private static string ExtractItemMember(string descriptor) =>
        ExtractMember(descriptor, "ItemNames => SharedTables.");

    private static string ExtractMember(string descriptor, string marker)
    {
        foreach (string line in descriptor.Split('\n'))
        {
            int idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
                return line[(idx + marker.Length)..].TrimEnd(';', ' ', '\r');

        }
        throw new InvalidOperationException($"no '{marker}' member in descriptor");
    }

    private static byte[] ExtractBlob(string sharedTables, string member)
    {
        foreach (string line in sharedTables.Split('\n'))
        {
            int idx = line.IndexOf($"{member} => new byte[] {{ ", StringComparison.Ordinal);
            if (idx < 0)
                continue;

            string body = line[(idx + $"{member} => new byte[] {{ ".Length)..];
            body = body[..body.IndexOf('}', StringComparison.Ordinal)].Trim();
            return [.. body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Convert.ToByte(t[2..], 16))];
        }

        string aliasMarker = $"{member} => ";
        foreach (string line in sharedTables.Split('\n'))
        {
            int idx = line.IndexOf(aliasMarker, StringComparison.Ordinal);
            if (idx >= 0)
            {
                string canonical = line[(idx + aliasMarker.Length)..].TrimEnd(';', ' ', '\r');
                return ExtractBlob(sharedTables, canonical);
            }
        }

        throw new InvalidOperationException($"no blob member '{member}' in SharedTables");
    }
}
