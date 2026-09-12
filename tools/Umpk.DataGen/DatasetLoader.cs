using System.Text.Json;

namespace Umpk.DataGen;

/// <summary>Reads the <c>data/java</c> tree into a <see cref="Dataset"/>. Tolerant of the supported file variants; structural problems surface as clear exceptions, semantic problems are the validator's job.</summary>
internal static class DatasetLoader
{
    private static readonly JsonDocumentOptions DocOptions = new() { CommentHandling = JsonCommentHandling.Skip };

    public static Dataset Load(string dataRoot)
    {
        string versionsPath = Path.Combine(dataRoot, "versions.json");
        if (!File.Exists(versionsPath))
            throw new DatasetException($"versions.json not found under {dataRoot}");

        using JsonDocument versionsDoc = Parse(versionsPath);
        List<VersionCatalogEntry> catalog = [];
        foreach (JsonElement v in versionsDoc.RootElement.GetProperty("versions").EnumerateArray())
            catalog.Add(new VersionCatalogEntry(
                v.GetProperty("name").GetString()!,
                v.GetProperty("protocol").GetInt32(),
                v.GetProperty("dataset").GetString()!,
                v.GetProperty("identity").GetString()!));

        Dictionary<int, VersionData> byProtocol = [];
        foreach (VersionCatalogEntry entry in catalog)
        {
            string dir = Path.Combine(dataRoot, entry.DatasetDir);
            byProtocol[entry.Protocol] = LoadVersion(dir, entry);
        }

        SharedData shared = LoadShared(Path.Combine(dataRoot, "shared"));
        return new Dataset { Versions = catalog, ByProtocol = byProtocol, Shared = shared };
    }

    private static JsonDocument Parse(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonDocument.Parse(stream, DocOptions);
    }

    private static VersionData LoadVersion(string dir, VersionCatalogEntry entry)
    {
        Dictionary<string, JsonElement> raw = [];
        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            using JsonDocument doc = Parse(file);
            raw[Path.GetFileName(file)] = doc.RootElement.Clone();
        }

        JsonElement Require(string name) =>
            raw.TryGetValue(name, out JsonElement el)
                ? el
                : throw new DatasetException($"protocol {entry.Protocol}: missing {name}");

        return new VersionData
        {
            Protocol = entry.Protocol,
            VersionName = entry.Name,
            Identity = entry.Identity,
            Packets = LoadPackets(Require("packets.json")),
            Items = LoadRegistryEntries(Require("items.json")),
            Components = LoadComponents(Require("components.json")),
            ArgumentTypes = LoadRegistryEntries(Require("argument_types.json")),
            Registries = LoadRegistries(Require("registries.json")),
            Entities = raw.TryGetValue("entities.json", out JsonElement entitiesEl) ? LoadEntities(entitiesEl) : [],
            ObjectEntities = raw.TryGetValue("entities.json", out JsonElement objectsEl) ? LoadObjectEntities(objectsEl) : [],
            Menus = LoadMenus(Require("menus.json")),
            Blocks = LoadBlocks(Require("blocks.json")),
            Shapes = LoadShapes(Require("shapes.json"),
                raw.TryGetValue("block-shape-refs.json", out JsonElement refs) ? refs : null),
            Push = raw.TryGetValue("block-push.json", out JsonElement push) ? LoadPush(push) : null,
            Metadata = LoadMetadata(Require("metadata.json")),
            MetadataKeys = LoadMetadataKeys(Require("metadata-keys.json")),
            Features = LoadFeatures(Require("features.json")),
            Lang = LoadLang(Require("lang.json")),
            RawFiles = raw,
        };
    }

    // metadata-keys.json: the tier-2 index each semantic key occupies on this protocol. Read as two flat name -> index maps rather than a shape with optional members, because a key that is absent on an era (no-gravity before 1.10, pose before 1.14, ticks-frozen before 1.17) is absent from the file, and "missing" is the answer the resolver wants.
    private static MetadataKeyTable LoadMetadataKeys(JsonElement el) => new()
    {
        EntityKeys = LoadIndexMap(el, "entity_keys"),
        ItemEntityKeys = LoadIndexMap(el, "item_entity_keys"),
    };

    private static IReadOnlyDictionary<string, int> LoadIndexMap(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out JsonElement map) || map.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, int>();

        Dictionary<string, int> indices = [];
        foreach (JsonProperty entry in map.EnumerateObject())
            if (entry.Value.ValueKind == JsonValueKind.Number)
                indices[entry.Name] = entry.Value.GetInt32();

        return indices;
    }

    private static PacketTables LoadPackets(JsonElement el)
    {
        Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<PacketEntry>>> phases = [];
        foreach (JsonProperty phase in el.GetProperty("phases").EnumerateObject())
        {
            Dictionary<string, IReadOnlyList<PacketEntry>> flows = [];
            foreach (JsonProperty flow in phase.Value.EnumerateObject())
            {
                List<PacketEntry> packets = [];
                foreach (JsonElement p in flow.Value.EnumerateArray())
                    packets.Add(new PacketEntry(
                        p.GetProperty("id").GetString()!,
                        p.GetProperty("protocol_id").GetInt32(),
                        p.TryGetProperty("codec", out JsonElement c) && c.ValueKind == JsonValueKind.String
                            ? c.GetString()
                            : null));

                flows[flow.Name] = packets;
            }
            phases[phase.Name] = flows;
        }
        return new PacketTables { Phases = phases };
    }

    private static IReadOnlyList<RegistryItem> LoadRegistryEntries(JsonElement el)
    {
        List<RegistryItem> items = [];
        if (el.TryGetProperty("entries", out JsonElement entries))
        {
            foreach (JsonElement e in entries.EnumerateArray())
            {
                // Three shapes:
                //   {name,id}                (registry identity)
                //   {id:"...",id_num}        (curated modern list)
                //   {composite_key,name,...} (legacy 47 composite items)
                if (e.TryGetProperty("composite_key", out JsonElement ck) && ck.ValueKind == JsonValueKind.Number)
                {
                    string legacyName = e.TryGetProperty("name", out JsonElement ln) ? ln.GetString()! : ck.GetInt32().ToString();
                    items.Add(new RegistryItem(legacyName, ck.GetInt32()));
                }
                else if (e.TryGetProperty("name", out JsonElement n) && e.TryGetProperty("id", out JsonElement idEl)
                    && idEl.ValueKind == JsonValueKind.Number)
                    items.Add(new RegistryItem(n.GetString()!, idEl.GetInt32()));

                else if (e.TryGetProperty("id", out JsonElement idStr) && idStr.ValueKind == JsonValueKind.String)
                {
                    int num = e.TryGetProperty("id_num", out JsonElement idn) ? idn.GetInt32() : items.Count;
                    items.Add(new RegistryItem(idStr.GetString()!, num));
                }
            }
        }
        return items;
    }

    // The mob/flat space: "entries" on a flat (1.14+) dataset, "mob_entries" on a pre-1.14 one. This is the space add_mob resolves against (and, on 1.14+, add_entity too, since the spaces merged).
    private static IReadOnlyList<EntityEntry> LoadEntities(JsonElement el) =>
        el.TryGetProperty("entries", out JsonElement flat) ? ReadEntityList(flat)
        : el.TryGetProperty("mob_entries", out JsonElement mobs) ? ReadEntityList(mobs)
        : [];

    // The pre-1.14 SpawnObject space, which add_entity resolves against on those versions. It is DISJOINT from the mob space: object 1 is a boat while mob 1 is a dropped item, so resolving an object id against the mob table yields a confidently wrong name rather than a miss. Absent on flat datasets, where the two spaces merged into the single entity_type registry.
    private static IReadOnlyList<EntityEntry> LoadObjectEntities(JsonElement el) =>
        el.TryGetProperty("object_entries", out JsonElement objects) ? ReadEntityList(objects) : [];

    private static IReadOnlyList<EntityEntry> ReadEntityList(JsonElement source)
    {
        List<EntityEntry> entities = [];
        foreach (JsonElement e in source.EnumerateArray())
        {
            if (!e.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;

            int idNum = e.TryGetProperty("id_num", out JsonElement n) && n.ValueKind == JsonValueKind.Number
                ? n.GetInt32()
                : entities.Count;
            float? width = e.TryGetProperty("width", out JsonElement w) && w.ValueKind == JsonValueKind.Number ? w.GetSingle() : null;
            float? height = e.TryGetProperty("height", out JsonElement h) && h.ValueKind == JsonValueKind.Number ? h.GetSingle() : null;
            entities.Add(new EntityEntry(idEl.GetString()!, idNum, width, height));
        }
        return entities;
    }

    // The minecraft:menu table. Both eras use one shape: "entries" of {id, id_num, slots} plus, on pre-1.14 datasets only, a "wire_id" carrying the literal open_screen window-type string (the numeric id_num is synthetic there). An empty or malformed table is a validator failure.
    private static IReadOnlyList<MenuEntry> LoadMenus(JsonElement el)
    {
        if (!el.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
            return [];

        List<MenuEntry> menus = [];
        foreach (JsonElement e in entries.EnumerateArray())
        {
            if (!e.TryGetProperty("id", out JsonElement idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;

            int idNum = e.TryGetProperty("id_num", out JsonElement n) && n.ValueKind == JsonValueKind.Number
                ? n.GetInt32()
                : menus.Count;
            string? wireId = e.TryGetProperty("wire_id", out JsonElement w) && w.ValueKind == JsonValueKind.String
                ? w.GetString()
                : null;
            int? slots = e.TryGetProperty("slots", out JsonElement s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32()
                : null;
            menus.Add(new MenuEntry(idEl.GetString()!, idNum, wireId, slots));
        }

        return menus;
    }

    private static IReadOnlyList<ComponentEntry> LoadComponents(JsonElement el)
    {
        List<ComponentEntry> items = [];
        foreach (JsonElement e in el.GetProperty("entries").EnumerateArray())
            items.Add(new ComponentEntry(
                e.GetProperty("id").GetString()!,
                e.TryGetProperty("id_num", out JsonElement n) ? n.GetInt32() : items.Count,
                e.TryGetProperty("codec", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null));

        return items;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<RegistryItem>> LoadRegistries(JsonElement el)
    {
        Dictionary<string, IReadOnlyList<RegistryItem>> registries = [];
        foreach (JsonProperty prop in el.GetProperty("registries").EnumerateObject())
        {
            List<RegistryItem> items = [];
            foreach (JsonElement e in prop.Value.EnumerateArray())
                items.Add(new RegistryItem(e.GetProperty("name").GetString()!, e.GetProperty("id").GetInt32()));

            registries[prop.Name] = items;
        }
        return registries;
    }

    private static BlockTable LoadBlocks(JsonElement el)
    {
        string identity = el.GetProperty("identity").GetString()!;
        List<BlockEntry> blocks = [];
        foreach (JsonElement b in el.GetProperty("blocks").EnumerateArray())
            blocks.Add(new BlockEntry(
                Name: GetOptString(b, "name") ?? GetOptString(b, "material"),
                BlockId: GetOptInt(b, "block_id"),
                Meta: GetOptInt(b, "meta"),
                StateId: GetOptInt(b, "state_id"),
                DefaultState: GetOptInt(b, "default_state"),
                MinState: GetOptInt(b, "min_state"),
                MaxState: GetOptInt(b, "max_state"),
                NumStates: GetOptInt(b, "num_states"),
                Properties: GetOptStringList(b, "properties")));

        return new BlockTable { Identity = identity, Blocks = blocks };
    }

    /// <summary>Reads <c>block-push.json</c>. The file lists only the blocks that DEPART from the default, so a missing name is a real answer ("normal, breakable, no block entity") and an absent FILE is not: see <see cref="BlockPushTable"/>.</summary>
    private static BlockPushTable LoadPush(JsonElement el)
    {
        Dictionary<string, string> reactions = new(StringComparer.Ordinal);
        foreach (JsonProperty reaction in el.GetProperty("reactions").EnumerateObject())
            foreach (JsonElement block in reaction.Value.EnumerateArray())
                reactions[block.GetString()!] = reaction.Name;

        static IReadOnlySet<string> Names(JsonElement parent, string property)
        {
            HashSet<string> set = new(StringComparer.Ordinal);
            foreach (JsonElement block in parent.GetProperty(property).EnumerateArray())
                set.Add(block.GetString()!);

            return set;
        }

        return new BlockPushTable
        {
            DefaultReaction = el.GetProperty("default_reaction").GetString()!,
            Reactions = reactions,
            Unbreakable = Names(el, "unbreakable"),
            BlockEntity = Names(el, "block_entity"),
        };
    }

    private static ShapeTable LoadShapes(JsonElement shapesEl, JsonElement? refsEl)
    {
        List<IReadOnlyList<IReadOnlyList<double>>> shapes = [];
        foreach (JsonElement shape in shapesEl.GetProperty("shapes").EnumerateArray())
        {
            List<IReadOnlyList<double>> boxes = [];
            foreach (JsonElement box in shape.EnumerateArray())
            {
                List<double> coords = [];
                foreach (JsonElement d in box.EnumerateArray())
                    coords.Add(d.GetDouble());

                boxes.Add(coords);
            }
            shapes.Add(boxes);
        }

        Dictionary<string, IReadOnlyList<int>>? collision = null;
        if (refsEl is { ValueKind: JsonValueKind.Object } refs && refs.TryGetProperty("collision", out JsonElement collEl))
        {
            collision = [];
            foreach (JsonProperty prop in collEl.EnumerateObject())
            {
                List<int> indices = [];
                foreach (JsonElement i in prop.Value.EnumerateArray())
                    indices.Add(i.GetInt32());

                collision[prop.Name] = indices;
            }
        }

        Dictionary<string, int>? byBlockId = null;
        if (shapesEl.TryGetProperty("collision_by_block_id", out JsonElement byIdEl))
        {
            byBlockId = [];
            foreach (JsonProperty prop in byIdEl.EnumerateObject())
                byBlockId[prop.Name] = prop.Value.GetInt32();

        }

        Dictionary<string, int>? byBlockState = null;
        if (shapesEl.TryGetProperty("collision_by_block_state", out JsonElement byStateEl))
        {
            byBlockState = [];
            foreach (JsonProperty prop in byStateEl.EnumerateObject())
                byBlockState[prop.Name] = prop.Value.GetInt32();

        }

        return new ShapeTable
        {
            Shapes = shapes,
            Collision = collision,
            CollisionByBlockId = byBlockId,
            CollisionByBlockState = byBlockState,
        };
    }

    private static MetadataTable LoadMetadata(JsonElement el)
    {
        List<MetadataEntry> serializers = [];
        foreach (JsonElement e in el.GetProperty("serializers").EnumerateArray())
            serializers.Add(new MetadataEntry(
                e.GetProperty("id").GetInt32(),
                e.GetProperty("field").GetString()!,
                e.GetProperty("codec").GetString()!));

        return new MetadataTable { Terminator = el.GetProperty("terminator").GetString()!, Serializers = serializers };
    }

    /// <summary>Reads a version's <c>lang.json</c>: a flat key -&gt; format-template map under <c>entries</c>. The file is captured in <c>RawFiles</c> so the validator enforces its required metadata.</summary>
    private static IReadOnlyDictionary<string, string> LoadLang(JsonElement el)
    {
        Dictionary<string, string> entries = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in el.GetProperty("entries").EnumerateObject())
        {
            // A null (or otherwise non-string) value violates the {key: string} shape every consumer assumes, so it surfaces here as a structural exception rather than as a Validator "problem" string. An EMPTY string is not this: it is a real, legitimate template (potion.potency.0, for one), so it is loaded and left for the validator to see, not rejected here.
            entries[entry.Name] = entry.Value.ValueKind == JsonValueKind.String
                ? entry.Value.GetString()!
                : throw new DatasetException($"lang.json entry '{entry.Name}' has a non-string template");
        }
        return entries;
    }

    private static FeatureFlags LoadFeatures(JsonElement el)
    {
        Dictionary<string, JsonElement> flags = [];
        foreach (JsonProperty prop in el.GetProperty("flags").EnumerateObject())
            flags[prop.Name] = prop.Value.Clone();

        return new FeatureFlags { Flags = flags };
    }

    private static SharedData LoadShared(string dir)
    {
        Dictionary<string, JsonElement> raw = [];
        foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
        {
            using JsonDocument doc = Parse(file);
            raw[Path.GetFileName(file)] = doc.RootElement.Clone();
        }

        JsonElement eras = raw["codec-eras.json"];
        List<string> packetOrder = [];
        foreach (JsonElement s in eras.GetProperty("packet").GetProperty("order").EnumerateArray())
            packetOrder.Add(s.GetString()!);

        List<string> payloadOrder = [];
        foreach (JsonElement s in eras.GetProperty("payload").GetProperty("order").EnumerateArray())
            payloadOrder.Add(s.GetString()!);

        var shared = new SharedData
        {
            CodecEras = new CodecEras { PacketOrder = packetOrder, PayloadOrder = payloadOrder },
            RawFiles = raw,
            BlockAttributes = null!,
            AttributeDefaults = null!,
        };
        return new SharedData
        {
            CodecEras = shared.CodecEras,
            RawFiles = raw,
            BlockAttributes = BlockAttributeTable.Load(shared),
            AttributeDefaults = AttributeDefaultTable.Load(shared),
        };
    }

    private static string? GetOptString(JsonElement el, string name) =>
        el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Reads an optional string array. The 1.13 datasets (393/401/404) carry the <c>properties</c> key with a null value on every block, so an absent key and an explicit null must read the same.</summary>
    private static IReadOnlyList<string>? GetOptStringList(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array)
            return null;

        List<string> values = [];
        foreach (JsonElement item in v.EnumerateArray())
            if (item.GetString() is { Length: > 0 } s)
                values.Add(s);

        return values;
    }

    private static int? GetOptInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}

internal sealed class DatasetException(string message) : Exception(message);
