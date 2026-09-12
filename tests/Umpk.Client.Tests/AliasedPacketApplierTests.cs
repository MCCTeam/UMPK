using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Every packet goes through the codec the version catalog binds at that protocol and then through the real applier chain, so a marker or a mis-bound era fails exactly the way a live session would.</summary>
/// <remarks>The tests verify that decoded packets reach their consumers. Entity equipment must update the entity on protocol 47 and on 1.16 onward, and name-addressed sound packets must reach their applier.</remarks>
public sealed class AliasedPacketApplierTests
{
    private const int MobId = 42;

    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    private static ApplierHarness Harness(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        harness.State.Registries = JavaGameData.Registries(protocol);
        return harness;
    }

    private static async Task<object> WireAsync(ApplierHarness harness, int protocol, string identifier, object packet)
    {
        object decoded = BoundDescriptorCodec.RoundTrip(protocol, identifier, packet);
        await harness.ApplyAsync(decoded);
        return decoded;
    }

    // set_carried_item -> the hotbar slot a consumer reads

    /// <summary>The server-forced hotbar slot has to land on <c>Self.HeldSlot</c>, which is the value every use-item and dig path reads. On protocols 477-767 the frame must reach state so the client keeps acting on a slot the server had already changed.</summary>
    [Theory]
    [InlineData(477)]
    [InlineData(578)]
    [InlineData(754)]
    [InlineData(760)]
    [InlineData(767)]
    [InlineData(768)]
    public async Task SetCarriedItem_UpdatesTheHeldSlot(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        Assert.NotEqual(7, harness.State.Self.HeldSlot);

        HeldSlotChanged? seen = null;
        harness.Events.Subscribe<HeldSlotChanged>(e => seen = e);

        await WireAsync(harness, protocol, "set_held_slot", new ClientboundSetHeldSlotPacket(7));

        Assert.Equal(7, harness.State.Self.HeldSlot);
        Assert.Equal(7, seen!.Slot);
    }

    // set_equipped_item -> the tracked entity's equipment a consumer reads

    /// <summary>Another entity's equipment has to land in <c>Entity.Equipment</c>. The packet was a marker across 107-578, and even where it decoded no applier ever wrote the result anywhere, so the dictionary was permanently empty on every protocol.</summary>
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(404)]
    [InlineData(578)]
    public async Task SetEquippedItem_UpdatesTheTrackedEntityEquipment(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        await harness.ApplyAsync(new ClientboundAddMobPacket(
            MobId, 0, 8.5, 64.0, -12.25, 0f, 0f, 0f, 0, 0, 0, new EntityMetadataList([], [])));

        Entity entity = harness.State.Entities.Get(MobId)!;
        Assert.Empty(entity.Equipment);

        EntityEquipmentChanged? seen = null;
        harness.Events.Subscribe<EntityEquipmentChanged>(e => seen = e);

        // Item id 5 with a count of 1: the id and the count survive all three item forms in the band, which is what makes one assertion valid across it.
        await WireAsync(
            harness,
            protocol,
            "set_equipment",
            new ClientboundSetEquipmentPacket(MobId, EquipmentSlot.Head, new EntityItemSlot(false, 5, 1, 0, null), null));

        Assert.True(entity.TryGetEquipment(EquipmentSlot.Head, out IMetadataSlot? item));
        var slot = Assert.IsType<EntityItemSlot>(item);
        Assert.Equal(5, slot.ItemId);
        Assert.Equal(1, slot.Count);
        Assert.Equal(MobId, seen!.EntityId);
        Assert.Equal(EquipmentSlot.Head, seen.Slot);
    }

    /// <summary>An empty stack clears the slot rather than recording an empty one, so a consumer that asks what an entity is holding gets null instead of a zero-count ghost.</summary>
    [Fact]
    public async Task SetEquippedItem_EmptyStack_ClearsTheSlot()
    {
        ApplierHarness harness = Harness(340);
        await harness.ApplyAsync(new ClientboundAddMobPacket(
            MobId, 0, 0, 0, 0, 0f, 0f, 0f, 0, 0, 0, new EntityMetadataList([], [])));
        Entity entity = harness.State.Entities.Get(MobId)!;

        await WireAsync(
            harness, 340, "set_equipment",
            new ClientboundSetEquipmentPacket(MobId, EquipmentSlot.MainHand, new EntityItemSlot(false, 5, 1, 0, null), null));
        await WireAsync(
            harness, 340, "set_equipment",
            new ClientboundSetEquipmentPacket(MobId, EquipmentSlot.MainHand, EntityItemSlot.Empty, null));

        Assert.True(entity.TryGetEquipment(EquipmentSlot.MainHand, out IMetadataSlot? item));
        Assert.Null(item);
    }

    // set_spawn_position -> the world spawn point a consumer reads

    /// <summary>The world spawn point, at coordinates chosen so the 1.14 packing flip is observable: a negative z and a y that is neither zero nor equal to either horizontal axis. Under the pre-1.14 packing the same eight bytes decode to a different, entirely plausible position, so this is the assertion that the frame-length checks elsewhere in this sweep cannot make.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(754)]
    [InlineData(755)]
    public async Task SetSpawnPosition_ReachesTheConsumerWithTheRightCoordinates(int protocol)
    {
        ApplierHarness harness = Harness(protocol);

        // WorldApplier drops every terrain packet until a world exists, so the join has to happen first or the assertion below would pass or fail for a reason that has nothing to do with the binding.
        await JoinAsync(harness);
        SpawnPositionChanged? seen = null;
        harness.Events.Subscribe<SpawnPositionChanged>(e => seen = e);

        var pos = new BlockPos(100, 64, -200);
        await WireAsync(
            harness, protocol, "set_default_spawn_position",
            new ClientboundSetDefaultSpawnPositionPacket(pos, 0f, null, 0f));

        Assert.NotNull(seen);
        Assert.Equal(pos, seen.Position);
    }

    // custom_sound / sound_effect -> the sound events a consumer reads

    /// <summary>Server-defined (resource-pack) sounds reach the same <see cref="SoundPlayed"/> a registry-id sound does, with the fixed-point position decoded back to block coordinates.</summary>
    [Theory]
    [InlineData(107)]
    [InlineData(210)]
    [InlineData(404)]
    [InlineData(578)]
    [InlineData(754)]
    [InlineData(758)]
    [InlineData(759)]
    [InlineData(760)]
    public async Task CustomSound_ReachesTheSoundConsumer(int protocol)
    {
        ApplierHarness harness = Harness(protocol);
        await JoinAsync(harness);
        SoundPlayed? seen = null;
        harness.Events.Subscribe<SoundPlayed>(e => seen = e);

        await WireAsync(
            harness, protocol, "custom_sound",
            new ClientboundCustomSoundPacket("app:ping", Source: 3, X: 800, Y: 512, Z: -1600, Volume: 1f, Pitch: 63f, Seed: 7L));

        Assert.NotNull(seen);
        Assert.Equal(3, seen.Source);
        Assert.Equal(new Vec3d(100, 64, -200), seen.Position);

        // The identity the wire carried. A name-addressed packet has no registry id, so the id reports -1 rather than a plausible-looking 0 that a consumer could mistake for a real sound.
        Assert.Equal("app:ping", seen.SoundName);
        Assert.Equal(-1, seen.SoundId);
    }

    /// <summary>The 1.8 named-sound form reaches the same consumer.</summary>
    [Fact]
    public async Task NamedSound_ReachesTheSoundConsumer()
    {
        ApplierHarness harness = Harness(47);
        await JoinAsync(harness);
        SoundPlayed? seen = null;
        harness.Events.Subscribe<SoundPlayed>(e => seen = e);

        await WireAsync(
            harness, 47, "sound_effect",
            new ClientboundNamedSoundPacket("random.pop", X: 800, Y: 512, Z: -1600, Volume: 1f, Pitch: 63));

        Assert.NotNull(seen);
        Assert.Equal(new Vec3d(100, 64, -200), seen.Position);
        Assert.Equal("random.pop", seen.SoundName);
        Assert.Equal(-1, seen.SoundId);
    }

    /// <summary>A registry-addressed sound forwards its registry id, and an inline (resource-pack) sound event forwards the name the server sent instead. Without either, a consumer wanting one specific sound (AutoFishing matching the bobber splash, for one) has nothing at all to match on.</summary>
    [Fact]
    public async Task RegistrySound_ForwardsItsId_AndInlineSound_ForwardsItsName()
    {
        ApplierHarness harness = Harness(774);
        await JoinAsync(harness);
        SoundPlayed? seen = null;
        harness.Events.Subscribe<SoundPlayed>(e => seen = e);

        // Registry id 609 is entity.fishing_bobber.splash on protocol 774.
        await WireAsync(
            harness, 774, "sound",
            new ClientboundSoundPacket(
                new SoundEventHolder(609, null, null), Source: 3, X: 800, Y: 512, Z: -1600,
                Volume: 1f, Pitch: 63f, Seed: 7L));

        Assert.NotNull(seen);
        Assert.Equal(609, seen.SoundId);

        // The registry id is now resolved to its name from the per-protocol sound table, which is what lets a consumer match ONE sound (AutoFishing's bobber splash) instead of an opaque number that means something different on every protocol.
        Assert.Equal("minecraft:entity.fishing_bobber.splash", seen.SoundName);

        seen = null;
        await WireAsync(
            harness, 774, "sound",
            new ClientboundSoundPacket(
                new SoundEventHolder(SoundId: -1, "app:custom", null), Source: 3, X: 800, Y: 512, Z: -1600,
                Volume: 1f, Pitch: 63f, Seed: 7L));

        Assert.NotNull(seen);
        Assert.Equal("app:custom", seen.SoundName);
        Assert.Equal(-1, seen.SoundId);
    }

    // move_player / spectate -> the two serverbound halves

    /// <summary>The heartbeat has to be SENDABLE, which is a different question from decodable: an unimplemented marker throws the moment a live session tries to serialise it, and the outbound table is the only place that shows it. A bare <c>Wire.Serverbound*(id) &gt;= 0</c> check would return true for a marker, so this test encodes through the bound descriptor.</summary>
    [Theory]
    [InlineData(477)]
    [InlineData(498)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(754)]
    public void MovePlayerStatus_IsSendable_AcrossTheBand(int protocol)
    {
        byte[] frame = BoundDescriptorCodec.EncodeServerbound(protocol, new ServerboundMovePlayerStatusPacket(true));
        Assert.Equal([1], frame);
    }

    /// <summary>The 1.8 spectator teleport, sendable under the modern packet identity.</summary>
    [Fact]
    public void Spectate_IsSendableOn18()
    {
        var target = Guid.NewGuid();
        byte[] frame = BoundDescriptorCodec.EncodeServerbound(47, new ServerboundTeleportToEntityPacket(target));
        Assert.Equal(16, frame.Length);
    }

    // close_screen / craft_progress_bar -> the container consumers on 1.8

    /// <summary>A server-forced container close and a container property update, both 1.8-only spellings, decode on 47 and reach the same appliers the later protocols already used.</summary>
    [Fact]
    public async Task LegacyContainerPackets_DecodeOn18()
    {
        ApplierHarness harness = Harness(47);
        var close = (ClientboundContainerClosePacket)BoundDescriptorCodec.RoundTrip(
            47, "container_close", new ClientboundContainerClosePacket(3));
        Assert.Equal(3, close.ContainerId);

        var data = (ClientboundContainerSetDataPacket)BoundDescriptorCodec.RoundTrip(
            47, "container_set_data", new ClientboundContainerSetDataPacket(3, 1, 200));
        Assert.Equal(3, data.ContainerId);
        Assert.Equal(1, data.PropertyId);
        Assert.Equal(200, data.Value);

        await harness.ApplyAsync(close);
        await harness.ApplyAsync(data);
    }

    // bundle_delimiter -> the accumulator that could not run

    /// <summary>The accumulator is driven by DECODED packets, so while the delimiter was a marker it could never open a bundle on any protocol. This feeds it the packet the bound codec now produces and asserts the full open/buffer/close cycle, which is the behaviour that was unreachable.</summary>
    [Fact]
    public void BundleDelimiter_DecodesAndDrivesTheAccumulator()
    {
        var delimiter = BoundDescriptorCodec.RoundTrip(
            770, "bundle_delimiter", new ClientboundBundleDelimiterPacket());
        Assert.IsType<ClientboundBundleDelimiterPacket>(delimiter);

        var accumulator = new BundleAccumulator(Identifier.Minecraft("bundle_delimiter"));
        Assert.False(accumulator.IsAccumulating);

        Assert.Equal(BundleFeed.Opened, accumulator.Offer(delimiter, out _));
        Assert.True(accumulator.IsAccumulating);
        Assert.Equal(BundleFeed.Buffered, accumulator.Offer(new ClientboundSetHeldSlotPacket(1), out _));
        Assert.Equal(BundleFeed.Closed, accumulator.Offer(delimiter, out PacketBundle? bundle));

        Assert.NotNull(bundle);
        Assert.Single(bundle.Packets);
        Assert.False(accumulator.IsAccumulating);
    }

    /// <summary>Joins the harness so the terrain-gated world appliers accept the sound packets.</summary>
    private static async Task JoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: -1, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 0);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: [], MaxPlayers: 20,
            ViewDistance: 0, SimulationDistance: 0, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: true, EnforcesSecureChat: false,
            Legacy: new LegacyLoginFields(Dimension: 0, Difficulty: 2, LevelType: "default")));
    }
}
