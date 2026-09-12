using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Appliers for auxiliary packet rows must retain their payloads. Synthetic packets (no corpus fragment carries these); UpdateRecipes and DamageEvent additionally get a real-corpus replay assertion in <see cref="CorpusReplayTests"/>.</summary>
public sealed class AuxiliaryApplierTests
{
    private static JavaVersion Version => JavaVersions.V1_21_5;

    [Fact]
    public async Task BorderWarningDelay_And_Distance_Update_Border()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        await harness.ApplyAsync(new ClientboundInitializeBorderPacket(0, 0, 100, 100, 0, 29999984, 5, 15));

        int changes = 0;
        harness.Events.Subscribe<WorldBorderChanged>(_ => changes++);

        await harness.ApplyAsync(new ClientboundSetBorderWarningDelayPacket(WarningDelay: 21));
        await harness.ApplyAsync(new ClientboundSetBorderWarningDistancePacket(WarningBlocks: 7));

        Assert.Equal(21, harness.State.World.Border.WarningTimeSeconds);
        Assert.Equal(7, harness.State.World.Border.WarningBlocks);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task LightUpdate_Is_Owned_And_Surfaced()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        LightUpdated? seen = null;
        harness.Events.Subscribe<LightUpdated>(e => seen = e);

        var light = new LightUpdateData([], [], [], [], [], []);
        await harness.ApplyAsync(new ClientboundLightUpdatePacket(2, -3, light));
        Assert.Equal(new LightUpdated(2, -3), seen);
    }

    [Fact]
    public async Task ChunksBiomes_Is_Owned_And_Surfaced()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        ChunkBiomesUpdated? seen = null;
        harness.Events.Subscribe<ChunkBiomesUpdated>(e => seen = e);

        var packet = new ClientboundChunksBiomesPacket(
            [new ChunkBiomeEntry(new ChunkPos(0, 0), [1, 2, 3]), new ChunkBiomeEntry(new ChunkPos(1, 0), [4])]);
        await harness.ApplyAsync(packet);
        Assert.Equal(2, seen!.ChunkCount);
    }

    [Fact]
    public async Task LevelParticles_Publishes_ParticleSpawned()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        ParticleSpawned? seen = null;
        harness.Events.Subscribe<ParticleSpawned>(e => seen = e);

        var packet = new ClientboundLevelParticlesPacket(
            OverrideLimiter: false, AlwaysShow: false, X: 1.5, Y: 64, Z: -2.5,
            XDist: 0, YDist: 0, ZDist: 0, MaxSpeed: 0, Count: 4, Particle: new ParticleData(11, []));
        await harness.ApplyAsync(packet);

        Assert.Equal(11, seen!.ParticleTypeId);
        Assert.Equal(new Vec3d(1.5, 64, -2.5), seen.Position);
        Assert.Equal(4, seen.Count);
    }

    [Fact]
    public async Task StopSound_Publishes_SoundStopped()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        SoundStopped? seen = null;
        harness.Events.Subscribe<SoundStopped>(e => seen = e);

        await harness.ApplyAsync(new ClientboundStopSoundPacket(Source: 2, Name: "minecraft:block.anvil.land"));

        Assert.Equal(2, seen!.Source);
        Assert.Equal("minecraft:block.anvil.land", seen.SoundName);
    }

    [Fact]
    public async Task DamageEvent_Publishes_EntityDamaged()
    {
        var harness = new ApplierHarness(Version);
        EntityDamaged? seen = null;
        harness.Events.Subscribe<EntityDamaged>(e => seen = e);

        await harness.ApplyAsync(new ClientboundDamageEventPacket(
            EntityId: 42, SourceTypeId: 3, SourceCauseId: 7, SourceDirectId: null, SourcePosition: new Vec3d(1, 2, 3)));

        Assert.Equal(42, seen!.EntityId);
        Assert.Equal(3, seen.SourceTypeId);
        Assert.Equal(7, seen.SourceCauseId);
        Assert.Null(seen.SourceDirectId);
        Assert.Equal(new Vec3d(1, 2, 3), seen.SourcePosition);
    }

    [Fact]
    public async Task HurtAnimation_Publishes_EntityHurt()
    {
        var harness = new ApplierHarness(Version);
        EntityHurt? seen = null;
        harness.Events.Subscribe<EntityHurt>(e => seen = e);

        await harness.ApplyAsync(new ClientboundHurtAnimationPacket(EntityId: 9, Yaw: 137.5f));

        Assert.Equal(9, seen!.EntityId);
        Assert.Equal(137.5f, seen.Yaw);
    }

    [Fact]
    public async Task ContainerSetData_Stores_Property_And_Publishes()
    {
        var harness = new ApplierHarness(Version);
        ContainerPropertyChanged? seen = null;
        harness.Events.Subscribe<ContainerPropertyChanged>(e => seen = e);

        await harness.ApplyAsync(new ClientboundContainerSetDataPacket(ContainerId: 3, PropertyId: 2, Value: 187));

        Assert.Equal(187, harness.State.Inventory.Properties[2]);
        Assert.Equal(3, seen!.WindowId);
        Assert.Equal(2, seen.PropertyId);
        Assert.Equal(187, seen.Value);
    }

    [Fact]
    public async Task MerchantOffers_Stores_Trades_And_Publishes()
    {
        var harness = new ApplierHarness(Version);
        var seen = new List<TradeOffersReceived>();
        harness.Events.Subscribe<TradeOffersReceived>(seen.Add);

        var offer = new MerchantOffer(
            TestItems.Stone(2), TestItems.Stone(2), SecondCost: null, TestItems.DiamondSword(),
            Uses: 0, MaxUses: 12, Xp: 2, PriceMultiplier: 0.05f, SpecialPrice: 0, Demand: 0);
        var offers = new MerchantOffers([offer], VillagerLevel: 1, Experience: 0, IsRegularVillager: true, CanRestock: true);
        await harness.ApplyAsync(new ClientboundMerchantOffersPacket(ContainerId: 5, Offers: offers));

        Assert.NotNull(harness.State.Inventory.Trades);
        Assert.Single(harness.State.Inventory.Trades!.Offers);
        Assert.Equal(TestItems.DiamondSword(), harness.State.Inventory.Trades.Offers[0].Result);
        Assert.Equal(5, Assert.Single(seen).WindowId);

        // Uses changes only when vanilla actually sends another merchant-offers packet (normally open or restock, not each completed trade). When one does arrive, replacement and notification are direct.
        MerchantOffers refreshed = offers with { Offers = [offer with { Uses = 3 }] };
        await harness.ApplyAsync(new ClientboundMerchantOffersPacket(ContainerId: 5, Offers: refreshed));

        Assert.Equal(3, harness.State.Inventory.Trades.Offers[0].Uses);
        Assert.Equal(2, seen.Count);
        Assert.All(seen, e => Assert.Equal(5, e.WindowId));
    }

    [Fact]
    public async Task UpdateRecipes_Tracks_Payload_And_Revision()
    {
        var harness = new ApplierHarness(Version);
        RecipesUpdated? seen = null;
        harness.Events.Subscribe<RecipesUpdated>(e => seen = e);

        await harness.ApplyAsync(new ClientboundUpdateRecipesPacket([9, 8, 7]));

        Assert.Equal(1, harness.State.Recipes.Revision);
        Assert.Equal([9, 8, 7], harness.State.Recipes.LatestPayload);
        Assert.Equal(1, seen!.Revision);
    }

    private static async Task JoinAsync(ApplierHarness harness)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        var join = new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
        await harness.ApplyAsync(join);
    }
}
