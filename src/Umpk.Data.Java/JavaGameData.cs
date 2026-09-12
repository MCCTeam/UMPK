using System.Collections.Concurrent;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary>Per-version built-in game data exposed to composition roots (the client, tooling, tests). The generated per-version descriptors carry a packed item-name table (VarInt count, then VarInt-length prefixed UTF-8 identifiers indexed by network id); this type parses it into a populated item <see cref="Registry{T}"/> so the item-stack codecs can resolve ids at runtime. It also parses the per-band collision-shape tables into an <see cref="IBlockShapeSource"/> (see <see cref="BlockShapes"/>), which is what gives physics and pathfinding real block geometry.</summary>
/// <remarks>This is the runtime source of the static item registry that a session installs into the codec context at the config-to-play pause (see <c>JavaClientLogin</c> / <c>UmpkClientBuilder.UseStaticRegistries</c>). The dimension/biome registries stay empty here: the dynamic registries (biomes, dimension types) arrive as config-phase <c>registry_data</c> and are a separate concern. Item ids are index-based from 1.13 onward (protocol 393+); the pre-1.13 legacy item codec resolves a <c>(id&lt;&lt;16)|damage</c> composite key instead, which the positional name table cannot express, so those versions carry a second generated table (<c>LegacyItemDefs</c>) pairing each name with its composite key. Both eras populate the same item registry, keyed by whatever the era's wire id is.</remarks>
public static partial class JavaGameData
{
    private static readonly ConcurrentDictionary<int, RegistryAccess> Cache = new();

    private static readonly ConcurrentDictionary<int, IBlockShapeSource> ShapeCache = new();

    private static readonly ConcurrentDictionary<int, IMetadataKeySource> MetadataKeyCache = new();

    private static readonly ConcurrentDictionary<int, IBlockPushSource> PushCache = new();

    /// <summary>The lowest protocol whose item ids are index-based (1.13, the flattening).</summary>
    private const int FirstFlattenedProtocol = 393;

    /// <summary>Creates the consistent refusal returned by per-protocol tables for unsupported protocols.</summary>
    private static ArgumentOutOfRangeException Unsupported(int protocol, string table) =>
        new(nameof(protocol), protocol, $"No {table} table: protocol {protocol} is not supported.");

    /// <summary>Resolves a sound-event network id to its registry name for a protocol, or null when the id is out of range or the protocol carries no sound table.</summary>
    /// <remarks>The id-addressed sound packets (1.14+ <c>minecraft:sound</c>) carry only this index, so without the table a consumer that wants ONE specific sound has nothing to match on. The name-addressed packets (1.8 <c>sound_effect</c>, 1.9-1.19.2 <c>custom_sound</c>) carry the name on the wire and never need this.</remarks>
    public static string? SoundName(int protocol, int soundId)
    {
        if (soundId < 0)
            return null;

        ReadOnlySpan<byte> table = SoundNames(protocol);
        if (table.IsEmpty)
            return null;

        // Same packed shape as the item table: VarInt count, then that many length-prefixed UTF-8 names, positionally indexed by network id. Scanned rather than cached: a sound lookup happens once per sound packet a consumer actually cares about, not per tick.
        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        if (soundId >= count)
            return null;

        for (int id = 0; id < count; id++)
        {
            string name = reader.ReadString();
            if (id == soundId)
                return name.Length == 0 ? null : name;

        }

        return null;
    }

    /// <summary>Builds (and caches) the version-accurate <see cref="IBlockShapeSource"/> for a protocol from the generated shape pool and its per-state reference table. This is what makes physics and pathfinding collide against real slab, stair, fence, wall, pane and carpet geometry instead of a unit cube. Safe to call repeatedly. An unsupported protocol is refused rather than answered from block flags alone, which would be a plausible-looking world model built on no data.</summary>
    public static IBlockShapeSource BlockShapes(int protocol) =>
        ShapeCache.GetOrAdd(protocol, static p => new JavaBlockShapes(CollisionShapes(p), BlockShapeRefs(p)));

    /// <summary>Builds (and caches) the version-accurate <see cref="IBlockPushSource"/> for a protocol: the piston push reaction, unbreakability and block-entity status of every block. Safe to call repeatedly; an unsupported protocol is refused. A protocol the dataset carries no measurement for yields a source whose <see cref="IBlockPushSource.HasData"/> is false, which a caller must read as "unknown" and not as "normal".</summary>
    public static IBlockPushSource BlockPushData(int protocol) =>
        PushCache.GetOrAdd(protocol, static p => JavaBlockPush.Create(Registries(p).Blocks, BlockPush(p)));

    /// <summary>Builds (and caches) the version-accurate <see cref="IMetadataKeySource"/> for a protocol: which tier-1 entity-metadata index each semantic key occupies on that era. Without one, every tier-2 read misses and typed entity properties such as the custom name stay empty even though the raw value was decoded and stored. Safe to call repeatedly.</summary>
    public static IMetadataKeySource EntityMetadataKeys(int protocol) =>
        MetadataKeyCache.GetOrAdd(protocol, static p => JavaEntityMetadataKeys.For(p));

    /// <summary>The legacy item bridge's per-era NBT-key and numeric enchantment-id maps. Stateless and immutable, so one shared instance serves every session and protocol.</summary>
    public static ILegacyItemBridgeSource LegacyItemBridgeSource { get; } = new JavaLegacyItemBridgeSource();

    /// <summary>The sole era key <see cref="LegacyItemBridgeSource"/>'s numeric-id table serves (see <c>JavaLegacyItemBridgeSource</c>'s remarks for why one era covers the whole pre-1.13 band).</summary>
    public const string LegacyItemBridgeEra = JavaLegacyItemBridgeSource.V1_8;

    /// <summary>Builds (and caches) the static <see cref="RegistryAccess"/> for a protocol: a populated item registry for every supported protocol (index-based ids on 1.13+, <c>(id&lt;&lt;16)|damage</c> composite keys before that), plus populated block and entity-type registries. Safe to call repeatedly; an unsupported protocol is refused.</summary>
    public static RegistryAccess Registries(int protocol) => Cache.GetOrAdd(protocol, Build);

    private static RegistryAccess Build(int protocol)
    {
        var builder = new RegistrySnapshotBuilder()
            .Add(BuildBlocks(protocol))
            .Add(BuildItems(protocol))
            .Add(BuildEntityTypes(protocol))
            .Add(BuildMenuTypes(protocol))
            .Add(new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType).Build())
            .Add(new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome).Build())
            .Add(BuildEnchantments(protocol))
            .Add(BuildMobEffects(protocol))
            // minecraft:attribute joins registry identity with the DefaultValue/MinValue/MaxValue that AttributeInstance consumes. Empty on 47-578, which carry no attribute registry and do not need one (their update_attributes names attributes by string). See BuildAttributes.
            .Add(BuildAttributes(protocol));

        // Pre-1.14 versions resolve add_entity (SpawnObject) against a SECOND, disjoint id space. The registry is only added when the era actually has one, so its presence IS the split-id-space signal that RegistryAccess.HasSplitEntityIdSpaces reports and the entity applier routes on.
        Registry<EntityTypeDefinition> objectTypes = BuildLegacyObjectTypes(protocol);
        if (objectTypes.Count > 0)
            builder.Add(objectTypes);

        return RegistryAccess.FromSnapshot(builder.Build());
    }

    /// <summary>Builds the item registry for either era, keyed by the era's own wire id. Flattened protocols (1.13+) read the packed item-name table and key entries by flat index. Pre-flattening protocols read the packed legacy table (VarInt count; per item: string name, VarInt composite key) and key entries by the <c>(item_id &lt;&lt; 16) | damage</c> composite the 1.8 slot codec resolves, which is what makes inventory work on 1.8-1.12.2. Duplicate composites or identifiers keep the first occurrence: legacy datasets fold a few damage variants (1.8 potions) onto one name, and those variants still resolve through the codec's base-composite fallback with the damage carried as a component.</summary>
    private static Registry<ItemDefinition> BuildItems(int protocol)
    {
        var items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
        if (protocol >= FirstFlattenedProtocol)
        {
            ReadOnlySpan<byte> flat = ItemNames(protocol);
            if (flat.IsEmpty)
                return items.Build();

            var flatReader = new PacketReader(flat);
            int flatCount = flatReader.ReadVarInt();
            for (int id = 0; id < flatCount; id++)
            {
                string name = flatReader.ReadString();
                items.Add(id, Identifier.Parse(name), new ItemDefinition());
            }
            return items.Build();
        }

        ReadOnlySpan<byte> table = LegacyItemDefs(protocol);
        if (table.IsEmpty)
            return items.Build();

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        HashSet<int> seenIds = new(count);
        HashSet<Identifier> seenKeys = new(count);
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            int composite = reader.ReadVarInt();
            if (!Identifier.TryParse(name, out Identifier key) || !seenIds.Add(composite) || !seenKeys.Add(key))
                continue;

            items.Add(composite, key, new ItemDefinition());
        }
        return items.Build();
    }

    /// <summary>Builds the block registry from the packed block-definition table (VarInt count; per block: string name, VarInt blockId, VarInt minState, VarInt maxState, VarInt defaultState). The table covers both eras: flat (1.13+) blocks carry real state ranges, legacy (pre-1.13) blocks encode a single id:meta state as min=max=default. Duplicate block ids or identifiers (legacy datasets reuse a few material names across ids) keep the first occurrence.</summary>
    /// <remarks>
    /// Pre-flattening blocks own their whole metadata nibble, and the range is WIDENED here rather than taken from the dataset. Vanilla's pre-1.13 identity is <c>(block_id &lt;&lt; 4) | meta</c> (the legacy datasets declare exactly that as their <c>identity_formula</c>, and the DataGen validator enforces it), so block <c>id</c> owns states <c>id&lt;&lt;4</c> through <c>id&lt;&lt;4|15</c> by construction and no per-meta data is needed to know WHICH block a state belongs to.
    /// <para>The datasets carry only metadata-zero rows. Widening ensures every non-zero metadata state still resolves to its owning block instead of being mistaken for air by physics and pathfinding.</para>
    /// <para>What the widening deliberately does NOT claim: the metadata's per-block MEANING. Vanilla's meta is interpreted by the block class, so there is no version-independent name for state 35:14 other than the 1.13 identifier <c>red_wool</c>, which does not exist on these servers. Reporting the block the server itself names (<c>minecraft:wool</c>) with the nibble available through <c>BlockState.LegacyMeta</c> is exact; inventing a flattened per-meta identifier would not be. Nor does it claim that all sixteen metas are REACHABLE (wool uses all 16, stone only 7); an unreachable state resolves to the right block, which is strictly better than resolving to air.</para>
    /// </remarks>
    private static Registry<BlockDefinition> BuildBlocks(int protocol)
    {
        var blocks = new RegistryBuilder<BlockDefinition>(RegistryIds.Block);
        ReadOnlySpan<byte> table = BlockDefs(protocol);
        if (table.IsEmpty)
            return blocks.Build();

        bool legacy = protocol < FirstFlattenedProtocol;
        Dictionary<int, BlockAttributes> attributes = ReadBlockAttributes(BlockAttrs(protocol));

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        HashSet<int> seenIds = new(count);
        HashSet<Identifier> seenKeys = new(count);
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            int blockId = reader.ReadVarInt();
            int minState = reader.ReadVarInt();
            int maxState = reader.ReadVarInt();
            int defaultState = reader.ReadVarInt();
            if (!Identifier.TryParse(name, out Identifier key) || !seenIds.Add(blockId) || !seenKeys.Add(key))
                continue;

            if (legacy && minState == maxState && minState == (blockId << 4))
            {
                // The dataset's single meta-0 row stands in for the whole nibble.
                minState = blockId << 4;
                maxState = minState | 0xF;
            }

            var definition = new BlockDefinition(minState, maxState, defaultState);
            if (attributes.TryGetValue(blockId, out BlockAttributes attrs))
                definition = definition with
                {
                    Flags = attrs.Flags,
                    StateFlags = attrs.StateFlags,
                    Friction = attrs.Friction,
                    SpeedFactor = attrs.SpeedFactor,
                    JumpFactor = attrs.JumpFactor,
                    Properties = attrs.Properties,
                };

            blocks.Add(blockId, key, definition);
        }
        return blocks.Build();
    }

    /// <summary>The per-block attributes read off the generated table, keyed by block network id.</summary>
    private readonly record struct BlockAttributes(
        BlockFlags Flags,
        IReadOnlyList<BlockFlags> StateFlags,
        float Friction,
        float SpeedFactor,
        float JumpFactor,
        IReadOnlyList<BlockPropertyDefinition> Properties);

    /// <summary>Reads the packed block-attribute table (see <c>Emitter.InternBlockAttrs</c> for the layout): a per-band string pool, then per block its physics scalars, its flags (shared, plus a per-state list only when the states differ), and its state properties with their value domains.</summary>
    /// <remarks>A property whose emitted value count is zero keeps its NAME and gets an empty domain. That is the deliberate honest degradation: the generator only emits a domain when exactly one candidate assignment reproduces the block's own state count, so "no values" means "not proven for this version" and a value lookup fails instead of returning something plausible.</remarks>
    private static Dictionary<int, BlockAttributes> ReadBlockAttributes(ReadOnlySpan<byte> table)
    {
        Dictionary<int, BlockAttributes> map = [];
        if (table.IsEmpty)
            return map;

        var reader = new PacketReader(table);
        int poolCount = reader.ReadVarInt();
        var pool = new string[poolCount];
        for (int i = 0; i < poolCount; i++)
            pool[i] = reader.ReadString();

        int blockCount = reader.ReadVarInt();
        for (int i = 0; i < blockCount; i++)
        {
            int blockId = reader.ReadVarInt();
            float friction = reader.ReadVarInt() / 1000f;
            float speed = reader.ReadVarInt() / 1000f;
            float jump = reader.ReadVarInt() / 1000f;
            var flags = (BlockFlags)reader.ReadVarInt();

            int propertyCount = reader.ReadVarInt();
            IReadOnlyList<BlockPropertyDefinition> properties = [];
            if (propertyCount > 0)
            {
                var list = new BlockPropertyDefinition[propertyCount];
                for (int p = 0; p < propertyCount; p++)
                {
                    string name = pool[reader.ReadVarInt()];
                    int valueCount = reader.ReadVarInt();
                    if (valueCount == 0)
                    {
                        list[p] = new BlockPropertyDefinition(name, []);
                        continue;
                    }

                    var values = new string[valueCount];
                    for (int v = 0; v < valueCount; v++)
                        values[v] = pool[reader.ReadVarInt()];

                    list[p] = new BlockPropertyDefinition(name, values);
                }

                properties = list;
            }

            IReadOnlyList<BlockFlags> stateFlags = [];
            if (reader.ReadVarInt() == 1)
            {
                int stateCount = reader.ReadVarInt();
                var perState = new BlockFlags[stateCount];
                for (int s = 0; s < stateCount; s++)
                    perState[s] = (BlockFlags)reader.ReadVarInt();

                stateFlags = perState;
            }

            map[blockId] = new BlockAttributes(flags, stateFlags, friction, speed, jump, properties);
        }

        return map;
    }

    /// <summary>Builds the entity-type registry from the packed entity table (VarInt count; per entity: VarInt idNum, string name). Bounding-box dimensions are absent in the data, so entries default to the vanilla generic hitbox (0.6 x 1.8). Duplicate ids or identifiers keep the first occurrence.</summary>
    private static Registry<EntityTypeDefinition> BuildEntityTypes(int protocol)
    {
        var entityTypes = new RegistryBuilder<EntityTypeDefinition>(RegistryIds.EntityType);
        ReadOnlySpan<byte> table = EntityNames(protocol);
        if (table.IsEmpty)
            return entityTypes.Build();

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        HashSet<int> seenIds = new(count);
        HashSet<Identifier> seenKeys = new(count);
        for (int i = 0; i < count; i++)
        {
            int idNum = reader.ReadVarInt();
            string name = reader.ReadString();
            if (!Identifier.TryParse(name, out Identifier key) || !seenIds.Add(idNum) || !seenKeys.Add(key))
                continue;

            entityTypes.Add(idNum, key, new EntityTypeDefinition(0.6f, 1.8f));
        }
        return entityTypes.Build();
    }

    /// <summary>Builds the pre-1.14 SpawnObject entity-type registry from the packed object table (same packing as <see cref="BuildEntityTypes"/>). Empty on 1.14+, where the object and mob spaces merged into the single <c>minecraft:entity_type</c> registry. Keeping it a separate registry rather than folding it into the entity-type one is the point: the two spaces collide (object 1 is a boat, mob 1 is a dropped item), so a single table cannot hold both without silently mislabeling one.</summary>
    private static Registry<EntityTypeDefinition> BuildLegacyObjectTypes(int protocol)
    {
        var objectTypes = new RegistryBuilder<EntityTypeDefinition>(RegistryIds.LegacyObjectType);
        ReadOnlySpan<byte> table = LegacyObjectEntityNames(protocol);
        if (table.IsEmpty)
            return objectTypes.Build();

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        HashSet<int> seenIds = new(count);
        HashSet<Identifier> seenKeys = new(count);
        for (int i = 0; i < count; i++)
        {
            int idNum = reader.ReadVarInt();
            string name = reader.ReadString();
            if (!Identifier.TryParse(name, out Identifier key) || !seenIds.Add(idNum) || !seenKeys.Add(key))
                continue;

            objectTypes.Add(idNum, key, new EntityTypeDefinition(0.6f, 1.8f));
        }
        return objectTypes.Build();
    }

    /// <summary>Builds the menu-type registry from the packed menu table (VarInt count; per menu: VarInt idNum, string name, string legacy wire id, VarInt slots+1). Both eras land here. From 1.14 the idNum is the real <c>minecraft:menu</c> wire id and the wire-id string is empty. Before 1.14 there is no menu registry on the wire at all: open_screen names the window with a STRING, so the idNum is a synthetic dense index and the string is carried on the definition as the only key that resolves an open container. The packed slot count is read and currently always absent (encoded as 0); per-slot semantics belong to <c>ISlotLayoutSource</c>, not to the definition. Duplicate ids or identifiers keep the first occurrence, as in the other tables.</summary>
    private static Registry<Umpk.Game.Inventory.MenuTypeDefinition> BuildMenuTypes(int protocol)
    {
        var menus = new RegistryBuilder<Umpk.Game.Inventory.MenuTypeDefinition>(RegistryIds.Menu);
        ReadOnlySpan<byte> table = MenuDefs(protocol);
        if (table.IsEmpty)
            return menus.Build();

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        HashSet<int> seenIds = new(count);
        HashSet<Identifier> seenKeys = new(count);
        for (int i = 0; i < count; i++)
        {
            int idNum = reader.ReadVarInt();
            string name = reader.ReadString();
            string wireId = reader.ReadString();
            _ = reader.ReadVarInt();
            if (!Identifier.TryParse(name, out Identifier key) || !seenIds.Add(idNum) || !seenKeys.Add(key))
                continue;

            menus.Add(idNum, key, new Umpk.Game.Inventory.MenuTypeDefinition(wireId.Length == 0 ? null : wireId));
        }
        return menus.Build();
    }

    /// <summary>Builds the enchantment registry from the packed identity table (VarInt count; per entry: VarInt wire id, string name). This is what makes a component-era (1.20.5+/766) <c>minecraft:enchantments</c> component resolve to a NAMED enchantment instead of a default, unbound handle.</summary>
    /// <remarks>
    /// <para>Populated for protocols 477-766 (1.14 - 1.20.6). Earlier item codecs carry enchantments as NBT and <c>ItemStack.TryGetEnchantmentLevel</c> reads them without any registry lookup.</para>
    /// <para>EMPTY on 767+ ON PURPOSE, because vanilla itself has no built-in enchantment registry there. 1.20.6 declares <c>Registry&lt;Enchantment&gt; ENCHANTMENT</c>; 1.21.1 declares none and lists its enchantment registry through the synchronized-registry configuration flow. Resolving enchantments on 767+ therefore needs the config-phase registry sync to install <c>minecraft:enchantment</c>, not a generated table; today that sync installs dimension types and chat types only.</para>
    /// <para>The packed table carries identity only, so definitions use <c>EnchantmentDefinition</c>'s declared default rather than inventing a maximum level.</para>
    /// </remarks>
    private static Registry<EnchantmentDefinition> BuildEnchantments(int protocol)
    {
        var enchantments = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment);
        ReadIdentityTable(EnchantmentDefs(protocol), (id, key) => enchantments.Add(id, key, new EnchantmentDefinition()));
        return enchantments.Build();
    }

    /// <summary>Builds the status-effect registry from the packed identity table (same layout as <see cref="BuildEnchantments"/>). Without it, <c>EntityApplier</c>'s effect resolution misses for every entity and the decoded effect is dropped instead of stored.</summary>
    /// <remarks>Populated for protocol 47 with one-based numeric ids and for protocols 477-776. Empty on protocols 107-404. The packed table carries identity only, so category and color remain at <c>MobEffectDefinition</c>'s declared defaults.</remarks>
    private static Registry<MobEffectDefinition> BuildMobEffects(int protocol)
    {
        var effects = new RegistryBuilder<MobEffectDefinition>(RegistryIds.MobEffect);
        ReadIdentityTable(MobEffectDefs(protocol), (id, key) => effects.Add(id, key, new MobEffectDefinition()));
        return effects.Build();
    }

    /// <summary>Builds the attribute registry from the packed definition table. Empty on 47-578, whose <c>update_attributes</c> packet names attributes by string rather than by holder id, so no registry is needed there.</summary>
    /// <remarks>
    /// <para>This registry is what makes <c>update_attributes</c> readable at all from 1.20.5 (protocol 766) up: from that version the packet names its attribute ONLY by holder VarInt (<c>AttributeSnapshot.ModernId</c>), so an empty registry means every frame resolves to an unbound handle and is dropped. It also unblocks <c>ItemCodecPrimitives.ResolveAttribute</c> for item <c>attribute_modifiers</c> components.</para>
    /// <para>Unlike enchantments and mob effects, the definition payload is REAL rather than a declared default, because <c>AttributeInstance</c> consumes it: it seeds <c>BaseValue</c> from <c>DefaultValue</c> and clamps the resolved value into <c>[MinValue, MaxValue]</c>. The three numbers come from the attribute-default table, keyed by the raw registry name and joined at emit time. Validation ensures that a protocol adding an attribute fails the build instead of silently emitting a zero-filled definition.</para>
    /// <para>Keys are the raw registry names, so 766/767 carry <c>minecraft:generic.movement_speed</c> and 768+ carry <c>minecraft:movement_speed</c>. A consumer that wants one id across the rename canonicalises at lookup with <see cref="Umpk.Game.Entities.AttributeIds"/>; the registry itself never collapses the two, because <c>horse.jump_strength</c> and <c>generic.jump_strength</c> would collide with different ranges if it did.</para>
    /// </remarks>
    private static Registry<AttributeDefinition> BuildAttributes(int protocol)
    {
        var attributes = new RegistryBuilder<AttributeDefinition>(RegistryIds.Attribute);
        ReadOnlySpan<byte> table = AttributeDefs(protocol);
        if (table.IsEmpty)
            return attributes.Build();

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        HashSet<int> seenIds = new(count);
        HashSet<Identifier> seenKeys = new(count);
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadVarInt();
            string name = reader.ReadString();
            double defaultValue = reader.ReadDouble();
            double minValue = reader.ReadDouble();
            double maxValue = reader.ReadDouble();
            bool ranged = reader.ReadByte() != 0;
            if (!Identifier.TryParse(name, out Identifier key) || !seenIds.Add(id) || !seenKeys.Add(key))
                continue;

            attributes.Add(id, key, new AttributeDefinition(defaultValue, minValue, maxValue, ranged));
        }

        return attributes.Build();
    }

    /// <summary>Walks a packed identity table (VarInt count; per entry: VarInt wire id, string name) and hands each well-formed, not-yet-seen entry to <paramref name="add"/>. Duplicate ids or identifiers keep the first occurrence, as every other table here does; an empty span adds nothing.</summary>
    private static void ReadIdentityTable(ReadOnlySpan<byte> table, Action<int, Identifier> add)
    {
        if (table.IsEmpty)
            return;

        var reader = new PacketReader(table);
        int count = reader.ReadVarInt();
        HashSet<int> seenIds = new(count);
        HashSet<Identifier> seenKeys = new(count);
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadVarInt();
            string name = reader.ReadString();
            if (!Identifier.TryParse(name, out Identifier key) || !seenIds.Add(id) || !seenKeys.Add(key))
                continue;

            add(id, key);
        }
    }
}
