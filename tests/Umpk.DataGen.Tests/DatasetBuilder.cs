using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Umpk.DataGen.Tests;

/// <summary>Builds a minimal-but-valid on-disk dataset in a temp directory, then lets a test mutate individual files to seed a specific bad case. Implements IDisposable so the temp tree is cleaned up. The "valid" baseline exercises both a modern and a legacy version so legacy-mode invariants are covered.</summary>
internal sealed class DatasetBuilder : IDisposable
{
    public string Root { get; }

    private DatasetBuilder(string root) => Root = root;

    public static DatasetBuilder ValidBaseline()
    {
        string root = Path.Combine(Path.GetTempPath(), "umpk-datagen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var builder = new DatasetBuilder(root);
        builder.WriteBaseline();
        return builder;
    }

    private void WriteBaseline()
    {
        WriteJson("versions.json", new JsonObject
        {
            ["_provenance"] = new JsonObject { ["kind"] = "curated" },
            ["versions"] = new JsonArray
            {
                Version("1.8", 47, "47", "legacy"),
                Version("1.21.5", 770, "770", "flat"),
            },
        });

        WriteShared();
        WriteModern("770", 770, "1.21.5");
        WriteLegacy("47");
    }

    /// <summary>A metadata-keys.json the validator accepts: the nine keys every protocol carries, the modern band's conditional trio, and the dropped-item stack index.</summary>
    private static JsonObject MetadataKeys(int carriedItem, bool ticksFrozen)
    {
        var keys = new JsonObject
        {
            ["shared_flags"] = 0,
            ["air_supply"] = 1,
            ["custom_name"] = 2,
            ["custom_name_visible"] = 3,
            ["silent"] = 4,
        };
        if (ticksFrozen)
        {
            keys["no_gravity"] = 5;
            keys["pose"] = 6;
            keys["ticks_frozen"] = 7;
        }

        int living = ticksFrozen ? 9 : 6;
        keys["health"] = living;
        keys["effect_particles"] = living + 1;
        keys["effect_ambience"] = living + 2;
        keys["arrow_count"] = living + 3;
        return new JsonObject
        {
            ["_provenance"] = new JsonObject { ["kind"] = "curated" },
            ["entity_keys"] = keys,
            ["item_entity_keys"] = new JsonObject { ["carried_item"] = carriedItem },
        };
    }

    private static JsonObject Version(string name, int protocol, string dir, string identity) => new()
    {
        ["name"] = name,
        ["protocol"] = protocol,
        ["dataset"] = dir,
        ["identity"] = identity,
    };

    private void WriteShared()
    {
        WriteJson("shared/codec-eras.json", new JsonObject
        {
            ["packet"] = new JsonObject { ["order"] = new JsonArray("V1_8", "V1_21_5") },
            ["payload"] = new JsonObject { ["order"] = new JsonArray("V1_8", "V1_21_5") },
        });

        // The curated semantic-flag and block-attribute tables. Both are required by the loader, so a real dataset that lost one fails loudly instead of quietly emitting default physics for every block. The fixture carries a small but structurally complete copy: one climbable, one replaceable, one non-default friction, and the two property kinds (bool and open-ended int) the resolver has to tell apart. The pre_flattening block is required, not optional: the same identifier names a different block in each era (before 1.13, minecraft:grass is the grass BLOCK and the replaceable plant is minecraft:tallgrass), so a dataset that lost it would emit the wrong era's semantics.
        WriteJson("shared/curated-flags.json", new JsonObject
        {
            ["climbable"] = new JsonArray("minecraft:ladder"),
            ["replaceable_by_placement"] = new JsonArray("minecraft:air"),
            ["pre_flattening"] = new JsonObject
            {
                ["climbable"] = new JsonArray("minecraft:ladder"),
                ["replaceable_by_placement"] = new JsonArray("minecraft:air"),
            },
        });
        // Attribute default/min/max values keyed by raw registry name. Required by the loader, so a dataset that lost it fails loudly rather than emitting zero-filled definitions that would clamp every attribute to zero. The two jump_strength rows are the era collision the raw keying exists for: they canonicalise to one name and carry different defaults and different ranges.
        WriteJson("shared/attribute-defaults.json", new JsonObject
        {
            ["attributes"] = new JsonObject
            {
                ["minecraft:generic.movement_speed"] = AttributeRow(0.7, 0.0, 1024.0),
                ["minecraft:movement_speed"] = AttributeRow(0.7, 0.0, 1024.0),
                ["minecraft:horse.jump_strength"] = AttributeRow(0.7, 0.0, 2.0),
                ["minecraft:generic.jump_strength"] = AttributeRow(0.42, 0.0, 32.0),
            },
        });
        WriteJson("shared/block-attributes.json", new JsonObject
        {
            ["air"] = new JsonArray("minecraft:air"),
            ["fluid"] = new JsonArray("minecraft:water"),
            ["friction"] = new JsonObject { ["minecraft:ice"] = 0.98 },
            ["speed_factor"] = new JsonObject { ["minecraft:soul_sand"] = 0.4 },
            ["jump_factor"] = new JsonObject { ["minecraft:honey_block"] = 0.5 },
            ["properties"] = new JsonObject
            {
                ["waterlogged"] = new JsonObject { ["kind"] = "bool" },
                ["age"] = new JsonObject { ["kind"] = "int", ["open"] = new JsonObject { ["min"] = 0 } },
            },
            ["property_overrides"] = new JsonArray(),
            ["default_state_checks"] = new JsonArray(),
        });
    }

    private void WriteModern(string dir, int protocol, string name)
    {
        WriteJson($"{dir}/packets.json", Packets("V1_21_5"));
        WriteJson($"{dir}/items.json", ModernRegistry("minecraft:air", "minecraft:stone"));
        WriteJson($"{dir}/components.json", new JsonObject
        {
            ["entries"] = new JsonArray
            {
                new JsonObject { ["id"] = "minecraft:custom_data", ["id_num"] = 0, ["codec"] = "VarInt" },
                new JsonObject { ["id"] = "minecraft:damage", ["id_num"] = 1, ["codec"] = "VarInt" },
            },
        });
        WriteJson($"{dir}/argument_types.json", ModernRegistry("brigadier:bool"));
        WriteJson($"{dir}/registries.json", new JsonObject
        {
            ["registries"] = new JsonObject
            {
                ["minecraft:mob_effect"] = new JsonArray(
                    new JsonObject { ["name"] = "minecraft:speed", ["id"] = 0 },
                    new JsonObject { ["name"] = "minecraft:slowness", ["id"] = 1 }),
                // Two attributes, deliberately one PREFIXED and one not, so the emitted table exercises the raw-name join on both spellings of the 1.21.2 rename.
                ["minecraft:attribute"] = new JsonArray(
                    new JsonObject { ["name"] = "minecraft:movement_speed", ["id"] = 0 },
                    new JsonObject { ["name"] = "minecraft:generic.jump_strength", ["id"] = 1 }),
            },
        });
        WriteJson($"{dir}/blocks.json", new JsonObject
        {
            ["identity"] = "flat",
            ["blocks"] = new JsonArray
            {
                Block("minecraft:air", 0, 0, 0, 0, 1),
                Block("minecraft:stone", 1, 1, 1, 1, 1),
            },
        });
        WriteJson($"{dir}/shapes.json", new JsonObject
        {
            ["shapes"] = new JsonArray(
                new JsonArray(),
                new JsonArray(new JsonArray(0.0, 0.0, 0.0, 1.0, 1.0, 1.0))),
        });
        WriteJson($"{dir}/block-shape-refs.json", new JsonObject
        {
            ["collision"] = new JsonObject
            {
                ["minecraft:air"] = new JsonArray(0),
                ["minecraft:stone"] = new JsonArray(1),
            },
        });
        WriteJson($"{dir}/metadata.json", new JsonObject
        {
            ["terminator"] = "0xff",
            ["serializers"] = new JsonArray(
                new JsonObject { ["id"] = 0, ["field"] = "BYTE", ["codec"] = "Byte" },
                new JsonObject { ["id"] = 1, ["field"] = "INT", ["codec"] = "VarInt" }),
        });
        WriteJson($"{dir}/features.json", Features());
        WriteJson($"{dir}/metadata-keys.json", MetadataKeys(carriedItem: 8, ticksFrozen: true));
        WriteJson($"{dir}/menus.json", new JsonObject
        {
            ["entries"] = new JsonArray(
                new JsonObject { ["id"] = "minecraft:generic_9x3", ["id_num"] = 0, ["slots"] = null },
                new JsonObject { ["id"] = "minecraft:furnace", ["id_num"] = 1, ["slots"] = null }),
        });
        // protocol 770 >= 759 (the first chat-type protocol), so Validator.ValidateLang requires the seven built-in chat-type keys here; WriteLang(includeChatTypeKeys: true) supplies them.
        WriteJson($"{dir}/lang.json", new JsonObject { ["entries"] = LangEntries(includeChatTypeKeys: true) });
    }

    private void WriteLegacy(string dir)
    {
        WriteJson($"{dir}/packets.json", Packets("V1_8"));
        WriteJson($"{dir}/items.json", new JsonObject
        {
            ["identity"] = "legacy-composite",
            ["entries"] = new JsonArray(
                new JsonObject { ["composite_key"] = 0, ["item_id"] = 0, ["damage"] = 0, ["name"] = "air" },
                new JsonObject { ["composite_key"] = 65536, ["item_id"] = 1, ["damage"] = 0, ["name"] = "stone" }),
        });
        WriteJson($"{dir}/components.json", new JsonObject { ["entries"] = new JsonArray() });
        WriteJson($"{dir}/argument_types.json", new JsonObject { ["entries"] = new JsonArray() });
        WriteJson($"{dir}/registries.json", new JsonObject
        {
            ["registries"] = new JsonObject
            {
                ["minecraft:mob_effect"] = new JsonArray(
                    new JsonObject { ["name"] = "minecraft:speed", ["id"] = 1 }),
            },
        });
        WriteJson($"{dir}/blocks.json", new JsonObject
        {
            ["identity"] = "legacy",
            ["blocks"] = new JsonArray(
                LegacyBlock(0, 0, 0, "air"),
                LegacyBlock(1, 0, 16, "stone")),
        });
        WriteJson($"{dir}/shapes.json", new JsonObject
        {
            ["shapes"] = new JsonArray(
                new JsonArray(),
                new JsonArray(new JsonArray(0.0, 0.0, 0.0, 1.0, 1.0, 1.0))),
            ["collision_by_block_id"] = new JsonObject { ["0"] = 0, ["1"] = 1 },
        });
        WriteJson($"{dir}/metadata.json", new JsonObject
        {
            ["terminator"] = "0x7f",
            ["serializers"] = new JsonArray(
                new JsonObject { ["id"] = 0, ["field"] = "BYTE", ["codec"] = "Byte" }),
        });
        WriteJson($"{dir}/features.json", Features());
        WriteJson($"{dir}/metadata-keys.json", MetadataKeys(carriedItem: 10, ticksFrozen: false));
        WriteJson($"{dir}/menus.json", new JsonObject
        {
            ["entries"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "minecraft:chest",
                    ["id_num"] = 0,
                    ["wire_id"] = "minecraft:chest",
                    ["slots"] = null,
                },
                new JsonObject
                {
                    ["id"] = "umpk:entity_horse",
                    ["id_num"] = 1,
                    ["wire_id"] = "EntityHorse",
                    ["slots"] = null,
                }),
        });
        // protocol 47 < 759, so the chat-type keys are not required here.
        WriteJson($"{dir}/lang.json", new JsonObject { ["entries"] = LangEntries(includeChatTypeKeys: false) });
    }

    /// <summary>A structurally valid synthetic lang table: enough entries to clear <c>Validator.MinLangEntries</c>, every template parseable under vanilla's format-group pattern, and (optionally) the seven built-in chat-type keys a protocol &gt;= 759 must carry.</summary>
    private static JsonObject LangEntries(bool includeChatTypeKeys)
    {
        var entries = new JsonObject();
        for (int i = 0; i < 2100; i++)
        {
            string padded = i.ToString("D4", CultureInfo.InvariantCulture);
            entries[$"test.entry.{padded}"] = $"Test entry {padded} with %s";
        }

        if (includeChatTypeKeys)
        {
            entries["chat.type.text"] = "<%s> %s";
            entries["chat.type.announcement"] = "[%s] %s";
            entries["chat.type.emote"] = "* %s %s";
            entries["chat.type.team.text"] = "%s <%s> %s";
            entries["chat.type.team.sent"] = "-> %s <%s> %s";
            entries["commands.message.display.incoming"] = "%s whispers to you: %s";
            entries["commands.message.display.outgoing"] = "You whisper to %s: %s";
        }

        return entries;
    }

    private static JsonObject Packets(string codec) => new()
    {
        ["phases"] = new JsonObject
        {
            ["handshake"] = new JsonObject
            {
                ["serverbound"] = new JsonArray(Packet("minecraft:intention", 0, codec)),
            },
            ["play"] = new JsonObject
            {
                ["clientbound"] = new JsonArray(
                    Packet("minecraft:keep_alive", 0, codec),
                    Packet("minecraft:login", 1, codec)),
            },
        },
    };

    private static JsonObject Packet(string id, int protocolId, string codec) => new()
    {
        ["id"] = id,
        ["protocol_id"] = protocolId,
        ["codec"] = codec,
    };

    private static JsonObject AttributeRow(double def, double min, double max) => new()
    {
        ["default"] = def,
        ["min"] = min,
        ["max"] = max,
    };

    private static JsonObject ModernRegistry(params string[] names)
    {
        var arr = new JsonArray();
        for (int i = 0; i < names.Length; i++)
            arr.Add(new JsonObject { ["name"] = names[i], ["id"] = i });

        return new JsonObject { ["entries"] = arr };
    }

    private static JsonObject Block(string name, int blockId, int min, int max, int def, int num) => new()
    {
        ["name"] = name,
        ["block_id"] = blockId,
        ["min_state"] = min,
        ["max_state"] = max,
        ["default_state"] = def,
        ["num_states"] = num,
    };

    private static JsonObject LegacyBlock(int blockId, int meta, int stateId, string material) => new()
    {
        ["block_id"] = blockId,
        ["meta"] = meta,
        ["state_id"] = stateId,
        ["material"] = material,
    };

    private static JsonObject Features() => new()
    {
        ["flags"] = new JsonObject
        {
            ["configurationPhase"] = true,
            ["hashedSlots"] = false,
            ["chatSigning"] = "v3",
        },
    };

    public void Mutate(string relativePath, Action<JsonObject> mutation)
    {
        string path = Path.Combine(Root, relativePath);
        JsonObject obj = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        mutation(obj);
        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WriteJson(string relativePath, JsonObject content)
    {
        // Every dataset file must carry _provenance (validator enforces it). Inject a default for the baseline unless the caller supplied its own, so the baseline is valid.
        if (!content.ContainsKey("_provenance"))
            content["_provenance"] = new JsonObject { ["kind"] = "test-curated" };

        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);

        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }
}
