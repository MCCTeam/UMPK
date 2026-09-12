using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.Scoreboard;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.Text;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>The authored witness payload values, one per catalogued packet.</summary>
/// <remarks>
/// <para>Every value here is chosen for one property: the eras of its packet must disagree about it. That is the whole craft of a witness, and it is why an empty container, a bare text component, a metadata list with one byte in it and a zero-length icon are all useless. A container carries an item with a count and a component; a component carries a click and a hover event; a metadata list carries several serializer kinds at once; a spawn info carries a last-death position. What is written down is a value and the reason it separates the eras; the bytes are what the band's own encoder makes of it.</para>
/// <para>Values take the protocol they are minted at because a few of them have to: an item stack needs a handle from that protocol's own item registry, and the registries genuinely differ. Nothing here branches on a protocol number to pick a SHAPE; that decision belongs to the codec being witnessed.</para>
/// </remarks>
internal static partial class WitnessCatalog
{
    /// <summary>A component that carries a click event and a hover event, because a bare string encodes identically under the legacy and modern interaction eras when the transport is held constant. The interactions make the layout difference observable; <c>DisconnectFramePinTests</c> records the same requirement.</summary>
    private static Component RichText { get; } = new(
        new TextContent("witness"),
        new Style
        {
            ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://e.invalid/w"),
            HoverEvent = new HoverShowText(Component.Text("why")),
        });

    private static readonly Guid WitnessUuid = new("6f2f1a3c-4d5e-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly ConcurrentDictionary<int, IReadOnlyDictionary<string, int>> ParticleIdCache = [];

    private static RegistryAccess Registries(int protocol) => JavaGameData.Registries(protocol);

    /// <summary>A non-empty stack of a block that exists on every supported version.</summary>
    private static ItemStack Stone(int protocol, int count) => Stack(protocol, "stone", count);

    private static ItemStack Sword(int protocol, int count) => Stack(protocol, "diamond_sword", count);

    /// <summary>A stack carrying <c>minecraft:container_loot</c>, which is the one data component whose wire id moves across EVERY component-table boundary the tree has: 55, 56, 66, 70, 77, 79, 80 from 1.20.6 to 26.2. The id is the registration order Mojang assigns, so a stack decoded against a neighbouring era's ordering dispatches this component to the wrong codec, and a stack with no components at all says nothing about which ordering was used. The pre-1.20.5 stack writers ignore components entirely, so the same value serves every band.</summary>
    private static ItemStack Stack(int protocol, string id, int count)
    {
        var loot = new NbtCompound();
        loot.PutString("loot_table", "minecraft:chests/witness");
        return new ItemStack(Registries(protocol).Items[Identifier.Minecraft(id)], count)
            .With(DataComponents.ContainerLoot, new NbtPayloadComponent(loot));
    }

    /// <summary>Metadata lists in three widths, richest first. The 1.8 packed format writes a header byte per entry and terminates at 0x7F; every modern era writes an index, a VarInt serializer id and the value and terminates at 0xFF, and the serializer TABLE shifts whenever a serializer is inserted, so a list carrying several kinds moves whenever the table does. The three widths exist because the kinds themselves arrive over time: an optional component is a 1.13 serializer, a compound tag a 1.12 one, and the 1.8 writer knows seven kinds in total. A band takes the widest list its own table can spell, which is also the one that says the most about it.</summary>
    private static EntityMetadataList MetadataRich(int protocol) => new(
        [
            new EntityDataEntry(0, MetadataValue.Byte(0x21)),
            new EntityDataEntry(2, MetadataValue.OptionalComponent(RichText)),
            new EntityDataEntry(8, MetadataValue.Float(17.5f)),
            new EntityDataEntry(9, MetadataValue.Boolean(true)),
            new EntityDataEntry(10, MetadataValue.Slot(Sword(protocol, 1))),
            new EntityDataEntry(11, MetadataValue.Position(new BlockPos(17, 65, -33))),
        ],
        []);

    /// <summary>1.12 and up: the compound tag is what 1.12 added and 1.9 through 1.11.2 cannot spell.</summary>
    private static EntityMetadataList MetadataWithNbt()
    {
        var tag = new NbtCompound();
        tag.PutInt("w", 7);
        return new EntityMetadataList(
            [
                new EntityDataEntry(0, MetadataValue.Byte(0x21)),
                new EntityDataEntry(2, MetadataValue.VarInt(4660)),
                new EntityDataEntry(8, MetadataValue.Float(17.5f)),
                new EntityDataEntry(9, MetadataValue.Nbt(tag)),
                new EntityDataEntry(10, MetadataValue.Position(new BlockPos(17, 65, -33))),
                new EntityDataEntry(11, MetadataValue.Rotations(new Rotations(1.5f, -2.5f, 3.5f))),
            ],
            []);
    }

    /// <summary>The six kinds the 1.8 packed writer knows, so every band can carry at least this one.</summary>
    private static EntityMetadataList MetadataLegacy() => new(
        [
            new EntityDataEntry(0, MetadataValue.Byte(0x21)),
            new EntityDataEntry(2, MetadataValue.VarInt(4660)),
            new EntityDataEntry(8, MetadataValue.Float(17.5f)),
            new EntityDataEntry(9, MetadataValue.String("witness")),
            new EntityDataEntry(10, MetadataValue.Position(new BlockPos(17, 65, -33))),
            new EntityDataEntry(11, MetadataValue.Rotations(new Rotations(1.5f, -2.5f, 3.5f))),
        ],
        []);

    /// <summary>A spawn-info block with a last-death position set, which is the one optional field on it: an era that does not carry the field, or carries it in another place, cannot reproduce these bytes.</summary>
    private static CommonPlayerSpawnInfo SpawnInfo() => new(
        DimensionTypeId: 0,
        Dimension: "minecraft:overworld",
        Seed: 0x0102030405060708L,
        GameType: 1,
        PreviousGameType: 0,
        IsDebug: false,
        IsFlat: true,
        LastDeathDimensionAndPos: 0x0102030405060708L,
        PortalCooldown: 11,
        SeaLevel: 63);

    /// <summary>A hand-built 1.8 chunk frame: int x, int z, the ground-up flag, a ushort section mask and a VarInt-prefixed blob holding one section's little-endian block states, its two nibble light arrays and the 256-byte biome tail. Protocol 47 reads the mask as a short and the blob behind a VarInt byte length; 1.9 reads the mask as a VarInt, so the two bytes of a 0x0001 mask are one field to 1.8 and two to its successor and the rest of the frame lands somewhere else entirely. The capture for this band carries only bulk chunk frames, which is why the band is authored at all.</summary>
    private static object LegacyChunk(int protocol)
    {
        const int sectionStates = 16 * 16 * 16 * 2;
        const int nibbleArray = 16 * 16 * 16 / 2;
        byte[] blob = new byte[sectionStates + (nibbleArray * 2) + 256];
        for (int i = 0; i < sectionStates; i += 2)
        {
            // Little-endian (blockId << 4) | meta, alternating stone and dirt so the decoded grid is not one repeated value and a mis-read palette shows up in the digest.
            ushort state = (ushort)(((i % 8) == 0 ? 1 : 3) << 4);
            blob[i] = (byte)state;
            blob[i + 1] = (byte)(state >> 8);
        }

        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteInt(-3);
        w.WriteInt(8);
        w.WriteBool(true);
        w.WriteUShort(0x0001);
        w.WriteVarInt(blob.Length);
        w.WriteBytes(blob);
        byte[] body = buffer.WrittenSpan.ToArray();

        // The column is inert: the 1.8 encoder writes RawBody verbatim and the decoder builds its own column from the blob. It is here because the record requires one.
        var dimensionTypes = new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType)
            .Add(0, Identifier.Minecraft("overworld"), new DimensionTypeDefinition(0, 256, HasSkylight: true))
            .Build();
        var dimension = new DimensionState(dimensionTypes[0], Identifier.Minecraft("overworld"));
        return new ClientboundLevelChunkPacket(-3, 8, new ChunkColumn(new ChunkPos(-3, 8), dimension), body);
    }

    private static object SetEntityData(int protocol) =>
        new ClientboundSetEntityDataPacket(0x4321, MetadataRich(protocol));

    private static object SetEntityDataWithNbt(int protocol) =>
        new ClientboundSetEntityDataPacket(0x4321, MetadataWithNbt());

    private static object SetEntityDataLegacy(int protocol) =>
        new ClientboundSetEntityDataPacket(0x4321, MetadataLegacy());

    private static object ContainerClick(int protocol) =>
        new ServerboundContainerClickPacket(
            ContainerId: 7,
            StateId: 5,
            Slot: 9,
            Button: 1,
            Mode: 0x80,
            ActionNumber: 3,
            LegacyClickedItem: Stone(protocol, 2),
            ChangedSlots: [new PredictedSlot(4, Sword(protocol, 1))],
            CarriedItem: Stone(protocol, 3));

    private static object ContainerSetContent(int protocol) =>
        new ClientboundContainerSetContentPacket(
            ContainerId: 7,
            StateId: 5,
            Items: [Stone(protocol, 2), ItemStack.Empty, Sword(protocol, 1)],
            CarriedItem: Stone(protocol, 3));

    private static object ContainerSetSlot(int protocol) =>
        new ClientboundContainerSetSlotPacket(15, 1, 5, Sword(protocol, 1));

    private static object UpdateAdvancements(int protocol) =>
        new ClientboundUpdateAdvancementsPacket(
            Reset: true,
            Added:
            [
                new AdvancementEntry(
                    Identifier.Minecraft("story/witness"),
                    new AdvancementNode(
                        Parent: Identifier.Minecraft("story/root"),
                        Display: new AdvancementDisplayInfo(
                            RichText,
                            Component.Text("described"),
                            Sword(protocol, 1),
                            AdvancementFrameType.Goal,
                            Identifier.Minecraft("textures/gui/advancements/backgrounds/stone.png"),
                            ShowToast: true,
                            Hidden: false,
                            X: 1.5f,
                            Y: 2.25f),
                        Criteria: ["seen"],
                        Requirements: [["seen"]],
                        SendsTelemetryEvent: true)),
            ],
            Removed: [Identifier.Minecraft("story/gone")],
            Progress:
            [
                new AdvancementProgressEntry(
                    Identifier.Minecraft("story/witness"),
                    [new CriterionProgressEntry("seen", 1_700_000_000_000L)]),
            ],
            ShowAdvancements: true);

    /// <summary>A particle whose option payload is exactly as wide as the era says that particle's payload is. The option bytes ride verbatim through every modern particle codec, so the only thing an era can disagree about is HOW MANY of them belong to the particle: dust is a Vector3f colour plus a scale on 1.20.5 and 1.21 (sixteen bytes) and an int colour plus a scale from 1.21.2 (eight), and trail gains its duration VarInt at 1.21.4. A particle with no options says nothing about any of that. The id comes from the protocol's own particle_type registry, because the registry reorders too.</summary>
    private static ParticleData ParticleOf(int protocol, string name, int optionBytes)
    {
        int id = ParticleId(protocol, name);
        byte[] options = new byte[optionBytes];
        for (int i = 0; i < options.Length; i++)
            options[i] = (byte)(i + 1);

        return new ParticleData(id, options);
    }

    /// <summary>The particle's wire id at one protocol, read from that protocol's own registry report. The runtime <c>RegistryAccess</c> carries the registries the client needs and particle_type is not one of them, so the dataset is the source; it is the same file the codec tables are generated from, which is what makes the id a property of the protocol rather than of this test.</summary>
    private static int ParticleId(int protocol, string name)
    {
        IReadOnlyDictionary<string, int> ids = ParticleIdCache.GetOrAdd(protocol, static p =>
        {
            string path = Path.Combine(FixturePaths.RepoRoot(), "data", "java", p.ToString(CultureInfo.InvariantCulture), "registries.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            Dictionary<string, int> entries = [];
            if (document.RootElement.TryGetProperty("registries", out JsonElement registries) &&
                registries.TryGetProperty("minecraft:particle_type", out JsonElement particles))
                foreach (JsonElement entry in particles.EnumerateArray())
                    entries[entry.GetProperty("name").GetString()!] = entry.GetProperty("id").GetInt32();

            return entries;
        });

        return ids.TryGetValue($"minecraft:{name}", out int id)
            ? id
            : throw new InvalidOperationException($"Protocol {protocol} has no minecraft:{name} particle.");
    }

    private static object LevelParticles(int protocol) => Particles([]);

    private static object LevelParticlesDustWide(int protocol) => Particles(ParticleOf(protocol, "dust", 16));

    private static object LevelParticlesDustNarrow(int protocol) => Particles(ParticleOf(protocol, "dust", 8));

    private static object LevelParticlesTransitionWide(int protocol) =>
        Particles(ParticleOf(protocol, "dust_color_transition", 28));

    private static object LevelParticlesTransitionNarrow(int protocol) =>
        Particles(ParticleOf(protocol, "dust_color_transition", 12));

    private static object LevelParticlesTrail(int protocol) => Particles(ParticleOf(protocol, "trail", 28));

    private static object LevelParticlesTrailDuration(int protocol) => Particles(ParticleOf(protocol, "trail", 29));

    /// <summary>The same frame with a three-byte particle payload. 1.8 reads a FIXED run of VarInt arguments for the particle's type and 1.13 reads the frame remainder, so the only payload the two can disagree about is one the 1.8 reader stops short of; a payload it reads whole is by construction the whole remainder, and identical. This one is therefore not encodable on the 1.8 band at all, which is what makes it the witness for every band above it.</summary>
    private static object LevelParticlesWithOptions(int protocol) => Particles([0x2A, 0x2B, 0x2C]);

    private static object Particles(byte[] options) => Particles(new ParticleData(0, options));

    private static object Particles(ParticleData particle) =>
        new ClientboundLevelParticlesPacket(
            OverrideLimiter: true,
            AlwaysShow: true,
            X: 1.5,
            Y: 65.25,
            Z: -3.75,
            XDist: 0.5f,
            YDist: 0.25f,
            ZDist: 0.125f,
            MaxSpeed: 2.5f,
            Count: 7,
            Particle: particle);

    private static object MerchantOffers(int protocol) =>
        new ClientboundMerchantOffersPacket(
            3,
            new MerchantOffers(
                [
                    new MerchantOffer(
                        BaseFirstCost: Stone(protocol, 4),
                        AdjustedFirstCost: Stone(protocol, 5),
                        SecondCost: Sword(protocol, 1),
                        Result: Sword(protocol, 1),
                        Uses: 2,
                        MaxUses: 9,
                        Xp: 3,
                        PriceMultiplier: 0.25f,
                        SpecialPrice: 1,
                        Demand: 6),
                ],
                VillagerLevel: 2,
                Experience: 40,
                IsRegularVillager: true,
                CanRestock: true));

    private static object SetCreativeModeSlot(int protocol) =>
        new ServerboundSetCreativeModeSlotPacket(9, Sword(protocol, 2));

    private static object JoinGame(int protocol) =>
        new ClientboundLoginPacket(
            PlayerId: 0x11223344,
            Hardcore: true,
            Dimensions: ["minecraft:overworld", "minecraft:the_nether"],
            MaxPlayers: 20,
            ViewDistance: 10,
            SimulationDistance: 9,
            ReducedDebugInfo: true,
            ShowDeathScreen: false,
            DoLimitedCrafting: true,
            SpawnInfo: SpawnInfo(),
            OnlineMode: true,
            EnforcesSecureChat: true,
            Legacy: new LegacyLoginFields(1, 2, "flat"));

    private static object Explode(int protocol) => Blast(new ParticleData(0, []));

    private static object ExplodeDustWide(int protocol) => Blast(ParticleOf(protocol, "dust", 16));

    private static object ExplodeDustNarrow(int protocol) => Blast(ParticleOf(protocol, "dust", 8));

    private static object ExplodeTransitionWide(int protocol) =>
        Blast(ParticleOf(protocol, "dust_color_transition", 28));

    private static object ExplodeTransitionNarrow(int protocol) =>
        Blast(ParticleOf(protocol, "dust_color_transition", 12));

    private static object ExplodeTrail(int protocol) => Blast(ParticleOf(protocol, "trail", 28));

    private static object ExplodeTrailDuration(int protocol) => Blast(ParticleOf(protocol, "trail", 29));

    private static object Blast(ParticleData particle) =>
        new ClientboundExplodePacket(
            Center: new Vec3d(1.5, 65.25, -3.75),
            LegacyStrength: 4.5f,
            LegacyBlocks: [new ExplosionBlock(1, -2, 3), new ExplosionBlock(-4, 5, -6)],
            LegacyMotionX: 0.25f,
            LegacyMotionY: -0.5f,
            LegacyMotionZ: 0.75f,
            Knockback: new Vec3d(0.25, -0.5, 0.75),
            Particle: particle,
            Sound: new SoundEventHolder(0, "minecraft:entity.generic.explode", 12.5f),
            Radius: 4.5f,
            BlockCount: 2,
            BlockParticles: [],
            BlockInteraction: 1,
            ParticleSoundTail: null);

    private static object Respawn(int protocol) =>
        new ClientboundRespawnPacket(SpawnInfo(), 0x03, new LegacyRespawnFields(-1, 2, 1, "flat"));

    private static object Commands(int protocol) => CommandTree("minecraft:color", 16);

    /// <summary>The same tree with 26.2's colour parser. 26.2 replaced <c>minecraft:color</c> with <c>minecraft:team_color</c> at wire id 16 and moved nothing else, so the two trees are the only thing that separates the 1.21.6 table from the 26.2 one: each names a parser the other has no id for, and each decodes the other's id 16 to the wrong parser name.</summary>
    private static object CommandsTeamColor(int protocol) => CommandTree("minecraft:team_color", 16);

    private static object CommandTree(string colorParser, int colorId) =>
        new ClientboundCommandsPacket(
            new CommandTreeData(
                [
                    new CommandNodeData(CommandNodeKind.Root, 0x00, [1], 0, null, null),
                    new CommandNodeData(CommandNodeKind.Literal, 0x01, [2], 0, "witness", null),
                    new CommandNodeData(
                        CommandNodeKind.Argument,
                        0x02,
                        [3],
                        0,
                        "amount",
                        new CommandArgumentData(3, "brigadier:integer", new IntegerArgumentProperties(1, 64), null)),

                    // Three parsers whose wire ids move on different releases, so no two of the eight id tables spell this tree the same way. minecraft:function moves at 1.19.3 (which dropped mob_effect, item_enchantment and entity_summon for gamemode and the two resource_key parsers) and again at 1.20.5; minecraft:uuid moves at 1.20.2, 1.20.3, 1.20.5, 1.21.5 and 1.21.6; the colour parser is what 26.2 renamed. On 1.13 through 1.18.2 the node names its parser with a string instead and none of it applies.
                    new CommandNodeData(CommandNodeKind.Argument, 0x02, [4], 0, "fn", new CommandArgumentData(35, "minecraft:function", null, null)),
                    new CommandNodeData(CommandNodeKind.Argument, 0x02, [5], 0, "who", new CommandArgumentData(47, "minecraft:uuid", null, null)),
                    new CommandNodeData(CommandNodeKind.Argument, 0x06, [], 0, "tint", new CommandArgumentData(colorId, colorParser, null, null)),
                ],
                0));

    private static object RecipeBookAdd(int protocol) =>
        new ClientboundRecipeBookAddPacket(
            [
                new RecipeBookEntry(
                    DisplayId: 3,
                    Kind: RecipeDisplayKind.CraftingShapeless,
                    ResultItemId: 1,
                    ResultId: Identifier.Minecraft("stone"),
                    ResultCount: 1,
                    Group: 2,
                    Category: 1,
                    Notification: true,
                    Highlight: true,
                    Display: new RecipeDisplay.CraftingShapeless(
                        [new SlotDisplay.Item(1)],
                        new SlotDisplay.Stack(Stone(protocol, 1)),
                        new SlotDisplay.Item(1)),
                    CraftingRequirements: [new RecipeIngredient.Items([1, 2])]),
            ],
            Replace: true);

    private static object SetCursorItem(int protocol) => new ClientboundSetCursorItemPacket(Sword(protocol, 1));

    private static object SetPlayerInventory(int protocol) =>
        new ClientboundSetPlayerInventoryPacket(9, Sword(protocol, 1));

    private static object MapItemData(int protocol) =>
        new ClientboundMapItemDataPacket(
            MapId: 5,
            Scale: 2,
            Locked: true,
            Icons: [new MapIcon(3, -4, 5, 6, RichText)],
            Patch: new MapPatch(2, 2, 1, 1, [0x10, 0x20, 0x30, 0x40]),
            TrackingPosition: true);

    private static object ServerData(int protocol) =>
        new ClientboundServerDataPacket(RichText, [0x01, 0x02, 0x03, 0x04]);

    private static object SetPlayerTeam(int protocol) =>
        new ClientboundSetPlayerTeamPacket(
            "witnesses",
            TeamMethod.Add,
            new TeamParameters(
                RichText,
                Component.Text("["),
                Component.Text("]"),
                NameTagVisibility.HideForOtherTeams,
                CollisionRule.PushOwnTeam,
                Color: 6,
                Options: 0x03),
            ["alpha", "beta"]);

    private static object LoginFinished(int protocol) =>
        new ClientboundLoginFinishedPacket(
            WitnessUuid,
            "Witness",
            [new GameProfileProperty("textures", "dGV4dHVyZQ==", "c2ln")],
            null);

    private static object AddMob(int protocol) =>
        new ClientboundAddMobPacket(
            EntityId: 0x1234,
            TypeId: 1,
            X: 1.5,
            Y: 65.25,
            Z: -3.75,
            Yaw: 90f,
            Pitch: -45f,
            HeadPitch: 22.5f,
            VelocityX: 100,
            VelocityY: -200,
            VelocityZ: 300,
            Metadata: MetadataRich(protocol),
            Uuid: WitnessUuid);

    private static object AddMobWithNbt(int protocol) => Mob(MetadataWithNbt());

    private static object AddMobLegacy(int protocol) => Mob(MetadataLegacy());

    private static object Mob(EntityMetadataList metadata) =>
        new ClientboundAddMobPacket(
            EntityId: 0x1234,
            TypeId: 1,
            X: 1.5,
            Y: 65.25,
            Z: -3.75,
            Yaw: 90f,
            Pitch: -45f,
            HeadPitch: 22.5f,
            VelocityX: 100,
            VelocityY: -200,
            VelocityZ: 300,
            Metadata: metadata,
            Uuid: WitnessUuid);

    private static object AddPlayer(int protocol) =>
        new ClientboundAddPlayerPacket(
            EntityId: 0x1234,
            Uuid: WitnessUuid,
            X: 1.5,
            Y: 65.25,
            Z: -3.75,
            Yaw: 90f,
            Pitch: -45f,
            CurrentItem: 1,
            Metadata: MetadataRich(protocol));

    private static object AddPlayerWithNbt(int protocol) => Player(MetadataWithNbt());

    private static object AddPlayerLegacy(int protocol) => Player(MetadataLegacy());

    private static object Player(EntityMetadataList metadata) =>
        new ClientboundAddPlayerPacket(
            EntityId: 0x1234,
            Uuid: WitnessUuid,
            X: 1.5,
            Y: 65.25,
            Z: -3.75,
            Yaw: 90f,
            Pitch: -45f,
            CurrentItem: 1,
            Metadata: metadata);

    private static object SetDefaultSpawnPosition(int protocol) =>
        new ClientboundSetDefaultSpawnPositionPacket(new BlockPos(17, 65, -33), 90f, "minecraft:overworld", -12.5f);

    /// <summary>The serverbound chat identifier is bound to two different packet records across its timeline, so the value follows the binding rather than a protocol number: the descriptor says which record the band's codec speaks, and the message text is the same either way.</summary>
    private static object Chat(int protocol)
    {
        BoundPacketCodec bound = Witnesses.Bind(
            protocol, new ProtocolTimeline.PacketKey(ProtocolPhase.Play, PacketFlow.Serverbound, "minecraft:chat"))!;
        return bound.Type == PlayPackets.Serverbound.SignedChat
            ? new ServerboundSignedChatPacket(
                "witness message",
                1_700_000_000_000L,
                0x0102030405060708L,
                null,
                new LastSeenMessagesUpdate(3, [0x00, 0x00, 0x00], 0x07))
            : new ServerboundLegacyChatPacket("witness message");
    }

    private static object Hello(int protocol) => new ServerboundHelloPacket("Witness", WitnessUuid);

    private static object OpenScreen(int protocol) =>
        new ClientboundOpenScreenPacket(7, 2, RichText, "minecraft:chest", 27, 0x1234);

    private static object PlayerChat(int protocol) =>
        new ClientboundPlayerChatPacket(
            Sender: WitnessUuid,
            Index: 3,
            Signature: null,
            SignedContent: "witness message",
            TimestampMillis: 1_700_000_000_000L,
            Salt: 0x0102030405060708L,
            UnsignedContent: RichText,
            ChatTypeId: 1,
            SenderName: Component.Text("Witness"),
            TargetName: null);

    private static object PlayerInfoUpdate(int protocol) =>
        new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode | PlayerInfoActions.UpdateListed |
            PlayerInfoActions.UpdateLatency | PlayerInfoActions.UpdateDisplayName,
            [
                new PlayerInfoEntry(
                    ProfileId: WitnessUuid,
                    Name: "Witness",
                    Properties: [new GameProfileProperty("textures", "dGV4dHVyZQ==", "c2ln")],
                    HasChatSession: false,
                    ChatSession: null,
                    GameMode: GameMode.Adventure,
                    Listed: true,
                    Latency: 42,
                    DisplayName: RichText,
                    ListOrder: 3,
                    ShowHat: true),
            ]);

    private static object PlayerPosition(int protocol) =>
        new ClientboundPlayerPositionPacket(
            X: 1.5,
            Y: 65.25,
            Z: -3.75,
            Yaw: 90f,
            Pitch: -45f,
            RelativeFlags: 0x1F,
            TeleportId: 7,
            ModernValues: new PositionMoveRotation(
                new Vec3d(1.5, 65.25, -3.75), new Vec3d(0.25, -0.5, 0.75), 90f, -45f));

    private static object SetEquipment(int protocol) =>
        new ClientboundSetEquipmentPacket(
            0x1234,
            EquipmentSlot.MainHand,
            new EntityItemSlot(false, 1, 2, 3, null),
            null);
}
