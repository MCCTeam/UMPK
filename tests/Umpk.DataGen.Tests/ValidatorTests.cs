using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

public sealed class ValidatorTests
{
    [Fact]
    public void Baseline_is_valid()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        Dataset dataset = DatasetLoader.Load(builder.Root);
        IReadOnlyList<string> problems = Validator.Validate(dataset);
        Assert.Empty(problems);
    }

    [Fact]
    public void Rejects_missing_component_codec_key_via_unknown_packet_codec()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/packets.json", root =>
        {
            JsonNode packet = root["phases"]!["play"]!["clientbound"]![0]!;
            packet["codec"] = "V_DOES_NOT_EXIST";
        });
        AssertHasProblemContaining(builder, "unknown codec key");
    }

    [Fact]
    public void Rejects_packet_id_gap()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/packets.json", root =>
        {
            // Shift the second packet's id from 1 to 2, leaving a gap at 1.
            root["phases"]!["play"]!["clientbound"]![1]!["protocol_id"] = 2;
        });
        AssertHasProblemContaining(builder, "gap");
    }

    [Fact]
    public void Rejects_gapped_registry()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/registries.json", root =>
        {
            root["registries"]!["minecraft:mob_effect"]![1]!["id"] = 5; // gap: 0 then 5
        });
        AssertHasProblemContaining(builder, "not gapless");
    }

    [Fact]
    public void Rejects_dangling_shape_ref()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/block-shape-refs.json", root =>
        {
            root["collision"]!["minecraft:stone"] = new JsonArray(99); // pool has 2 entries
        });
        AssertHasProblemContaining(builder, "dangling");
    }

    [Fact]
    public void Rejects_component_registry_gap()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/components.json", root =>
        {
            root["entries"]![1]!["id_num"] = 7; // gap: 0 then 7
        });
        AssertHasProblemContaining(builder, "component registry not gapless");
    }

    [Fact]
    public void Rejects_legacy_block_identity_violation()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("47/blocks.json", root =>
        {
            // stone: block_id 1 meta 0 must have state (1<<4)|0 = 16. Break it.
            root["blocks"]![1]!["state_id"] = 99;
        });
        AssertHasProblemContaining(builder, "(id<<4)|meta");
    }

    [Fact]
    public void Rejects_legacy_components_present()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("47/components.json", root =>
        {
            root["entries"] = new JsonArray(
                new JsonObject { ["id"] = "minecraft:damage", ["id_num"] = 0, ["codec"] = "VarInt" });
        });
        AssertHasProblemContaining(builder, "legacy version must have no data components");
    }

    [Fact]
    public void Rejects_metadata_serializer_gap()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/metadata.json", root =>
        {
            root["serializers"]![1]!["id"] = 5; // gap: 0 then 5
        });
        AssertHasProblemContaining(builder, "metadata serializer ids not gapless");
    }

    [Fact]
    public void Rejects_shape_pool_without_empty_at_index_zero()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/shapes.json", root =>
        {
            // Make index 0 a non-empty shape.
            root["shapes"]![0] = new JsonArray(new JsonArray(0.0, 0.0, 0.0, 1.0, 1.0, 1.0));
        });
        AssertHasProblemContaining(builder, "index 0 must be the empty shape");
    }

    [Fact]
    public void Rejects_missing_provenance_on_version_file()
    {
        // A version file without required origin metadata must be rejected.
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("47/registries.json", root => root.Remove("_provenance"));
        AssertHasProblemContaining(builder, "_provenance.kind");
    }

    [Fact]
    public void Rejects_empty_provenance_kind_on_shared_file()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("shared/codec-eras.json", root =>
            root["_provenance"] = new JsonObject { ["kind"] = "" });
        AssertHasProblemContaining(builder, "_provenance.kind");
    }

    /// <summary>An attribute a protocol declares but the curated table does not describe must be REJECTED, not defaulted. A zero-filled definition would clamp that attribute to zero on every session, which would clamp every value to zero, so a new protocol that adds an attribute has to fail the gate loudly and get its vanilla numbers written down.</summary>
    [Fact]
    public void Rejects_an_attribute_with_no_curated_default_row()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/registries.json", root =>
            root["registries"]!["minecraft:attribute"]!.AsArray()
                .Add(new JsonObject { ["name"] = "minecraft:invented_attribute", ["id"] = 2 }));
        AssertHasProblemContaining(builder, "has no row in shared/attribute-defaults.json");
    }

    /// <summary>The gate asserts over RAW names, so the two <c>jump_strength</c> spellings are two separate requirements. Dropping the <c>generic.</c>-prefixed row must fail even though the unprefixed one canonicalises to the same string - which is exactly what a canonically-keyed gate would miss, while the emitter went on writing 0.42 in [0,32] where vanilla says 0.7 in [0,2].</summary>
    [Fact]
    public void Rejects_a_missing_row_even_when_its_canonical_twin_is_present()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("shared/attribute-defaults.json", root =>
            root["attributes"]!.AsObject().Remove("minecraft:generic.jump_strength"));
        AssertHasProblemContaining(builder, "minecraft:generic.jump_strength");
    }

    /// <summary>A curated row whose default falls outside its own range is a transcription error.</summary>
    [Fact]
    public void Rejects_a_curated_default_outside_its_own_range()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("shared/attribute-defaults.json", root =>
            root["attributes"]!["minecraft:movement_speed"]!["default"] = 4096.0);
        AssertHasProblemContaining(builder, "is outside");
    }

    [Fact]
    public void Rejects_era_inappropriate_codec_key()
    {
        // Protocol 47 is MC 1.8. V1_21_5 is a valid vocabulary key and a member of codec-eras packet order (so it passes the vocabulary and packet-order checks), but it names a newer era than 1.8 and must be rejected as era-inappropriate.
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("47/packets.json", root =>
        {
            root["phases"]!["play"]!["clientbound"]![0]!["codec"] = "V1_21_5";
        });
        AssertHasProblemContaining(builder, "newer era");
    }

    [Fact]
    public void Rejects_non_chronological_codec_era_order()
    {
        // Packet era order must be chronological. A descending order must be rejected.
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("shared/codec-eras.json", root =>
        {
            root["packet"]!["order"] = new JsonArray("V1_21_5", "V1_8");
        });
        AssertHasProblemContaining(builder, "not chronological");
    }

    /// <summary>Two block ids sharing an identifier is not a cosmetic duplicate. The runtime registry is keyed by identifier as well as by network id and <c>JavaGameData.BuildBlocks</c> keeps the FIRST occurrence, so the second id gets no entry and every one of its states resolves to <c>minecraft:air</c>. Protocol 47 has thirteen of these: three double slabs and ten more that came from naming a block after the flattened identifier of its meta-0 variant, which folds still/flowing, lit/unlit and powered/unpowered pairs onto one name.</summary>
    [Fact]
    public void Rejects_two_block_ids_sharing_an_identifier()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("47/blocks.json", root =>
        {
            root["blocks"]![1]!["material"] = "air"; // block id 1 now collides with block id 0
        });
        AssertHasProblemContaining(builder, "share the identifier");
    }

    [Fact]
    public void Rejects_flat_block_ids_sharing_an_identifier()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/blocks.json", root =>
        {
            root["blocks"]![1]!["name"] = root["blocks"]![0]!["name"]!.GetValue<string>();
        });
        AssertHasProblemContaining(builder, "share the identifier");
    }

    /// <summary>The pre-flattening semantic-flag mirror is load-bearing, so its absence has to be loud. Without it the legacy bands would silently fall back to an empty set and lose every curated flag.</summary>
    [Fact]
    public void Rejects_curated_flags_without_the_pre_flattening_mirror()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("shared/curated-flags.json", root =>
        {
            root.AsObject().Remove("pre_flattening");
        });

        DatasetException failure = Assert.Throws<DatasetException>(() => DatasetLoader.Load(builder.Root));
        Assert.Contains("pre_flattening", failure.Message, StringComparison.Ordinal);
    }

    private static void AssertHasProblemContaining(DatasetBuilder builder, string fragment)
    {
        Dataset dataset = DatasetLoader.Load(builder.Root);
        IReadOnlyList<string> problems = Validator.Validate(dataset);
        Assert.Contains(problems, p => p.Contains(fragment, StringComparison.Ordinal));
    }
}
