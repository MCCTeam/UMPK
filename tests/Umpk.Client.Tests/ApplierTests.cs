using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ApplierTests
{
    private static JavaVersion Version => JavaVersions.V1_21_5;

    [Fact]
    public async Task Self_Health_Updates_And_Raises_Died()
    {
        var harness = new ApplierHarness(Version);
        bool died = false;
        harness.Events.Subscribe<Died>(_ => died = true);
        harness.State.Self.Health = 20f;

        await harness.ApplyAsync(new ClientboundSetHealthPacket(0f, 0, 0f));

        Assert.Equal(0f, harness.State.Self.Health);
        Assert.True(died);
    }

    [Fact]
    public async Task Self_Position_Applies_And_Confirms_Teleport()
    {
        var harness = new ApplierHarness(Version);
        var pos = new ClientboundPlayerPositionPacket(10, 65, -3, 90, 0, 0, TeleportId: 7, ModernValues: null);

        await harness.ApplyAsync(pos);

        Assert.Equal(new Vec3d(10, 65, -3), harness.State.Self.Position);
        Assert.Collection(
            harness.Recorder.Packets,
            packet => Assert.Equal(new ServerboundAcceptTeleportationPacket(7), packet),
            packet => Assert.Equal(
                new ServerboundMovePlayerPosRotPacket(10, 65, -3, 90, 0, OnGround: false, HorizontalCollision: false),
                packet));
    }

    [Fact]
    public async Task Self_Position_777_ConfirmsTeleportWithoutPositionEcho()
    {
        // 26.3 applies the teleport from the destination-carrying accept itself (vanilla client sends nothing else), so the follow-up position echo that older eras require would be a second position packet in the same tick.
        var harness = new ApplierHarness(JavaVersions.V26_3);
        var pos = new ClientboundPlayerPositionPacket(10, 65, -3, 90, 0, 0, TeleportId: 7, ModernValues: null);

        await harness.ApplyAsync(pos);

        Assert.Equal(new Vec3d(10, 65, -3), harness.State.Self.Position);
        var accept = Assert.IsType<ServerboundAcceptTeleportationPacket>(Assert.Single(harness.Recorder.Packets));
        Assert.Equal(new ServerboundAcceptTeleportationPacket(7, 10, 65, -3, 90, 0), accept);
    }

    [Fact]
    public async Task Self_Abilities_Pushes_PhysicsConditions()
    {
        var harness = new ApplierHarness(Version);
        int before = harness.ConditionsPushCount;

        await harness.ApplyAsync(new ClientboundPlayerAbilitiesPacket(0x06, 0.05f, 0.1f)); // flying + mayfly

        Assert.True(harness.State.Self.Flying);
        Assert.True(harness.State.Self.MayFly);
        Assert.True(harness.ConditionsPushCount > before);
    }

    [Fact]
    public async Task KeepAlive_IsAnswered()
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(new ClientboundPlayKeepAlivePacket(1234));
        Assert.Contains(harness.Recorder.Packets, p => p is ServerboundPlayKeepAlivePacket { Id: 1234 });
    }

    /// <summary>minecraft:ping (play phase, 1.17+) must be answered with minecraft:pong carrying the same id.</summary>
    /// <remarks>The client answers unconditionally and the server ignores the reply, so the only peers this packet is FOR are proxies and plugins - the peers that can actually be left waiting on a client that never replies. Both directions were decoded and bound; nothing joined them.</remarks>
    [Fact]
    public async Task PlayPing_IsAnsweredWithTheSameId()
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(new ClientboundPlayPingPacket(0x5150));
        Assert.Contains(harness.Recorder.Packets, p => p is ServerboundPlayPongPacket { Id: 0x5150 });
    }

    [Fact]
    public async Task World_Time_Updates()
    {
        var harness = new ApplierHarness(Version);
        // Build the world first via a join packet.
        await JoinAsync(harness);

        await harness.ApplyAsync(new ClientboundSetTimePacket(1000, 6000, false, []));

        Assert.Equal(6000, harness.State.World.TimeOfDay);
    }

    [Fact]
    public async Task World_BlockUpdate_Sets_State_And_Raises_Event()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        // Load a column so the block position is inside a loaded chunk.
        var column = new Game.World.ChunkColumn(new ChunkPos(0, 0), harness.State.World.Dimension);
        harness.State.World.LoadColumn(column);

        BlockChanged? seen = null;
        harness.Events.Subscribe<BlockChanged>(e => seen = e);

        var pos = new BlockPos(1, 64, 1);
        await harness.ApplyAsync(new ClientboundBlockUpdatePacket(pos, 5));

        Assert.Equal(5, harness.State.World.GetBlockStateId(pos));
        Assert.NotNull(seen);
        Assert.Equal(pos, seen!.Position);
    }

    /// <summary>Legacy (through 1.16.1) section_blocks_update records pack (x:4 hi, z:4, y:8 lo) with an ABSOLUTE Y and take the chunk origin from the two chunk ints. Decoding these with the modern 12-bit local layout silently writes incorrect world positions.</summary>
    [Fact]
    public async Task World_MultiBlock_Legacy_DecodesAbsoluteYAndChunkOrigin()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        harness.State.World.LoadColumn(new Game.World.ChunkColumn(new ChunkPos(2, -1), harness.State.World.Dimension));

        // Two records in chunk (2,-1): (x=3,z=7,y=64,state=9) and (x=15,z=0,y=70,state=12).
        var changes = new SectionBlockChange[]
        {
            new((short)((3 << 12) | (7 << 8) | 64), 9),
            new(unchecked((short)((15 << 12) | (0 << 8) | 70)), 12),
        };
        await harness.ApplyAsync(new ClientboundSectionBlocksUpdatePacket(
            SectionPos: 0, LegacyChunkX: 2, LegacyChunkZ: -1, changes, IsLegacy: true));

        Assert.Equal(9, harness.State.World.GetBlockStateId(new BlockPos(35, 64, -9)));
        Assert.Equal(12, harness.State.World.GetBlockStateId(new BlockPos(47, 70, -16)));
    }

    [Fact]
    public async Task World_MultiBlock_Modern_DecodesSectionPosAndLocalXzy()
    {
        var harness = new ApplierHarness(Version);
        await JoinAsync(harness);
        harness.State.World.LoadColumn(new Game.World.ChunkColumn(new ChunkPos(1, -2), harness.State.World.Dimension));

        // Section (x=1, y=4, z=-2); one record at local (x=5, z=6, y=7), state 11.
        long sectionPos = ((long)(1 & 0x3FFFFF) << 42) | ((long)(-2 & 0x3FFFFF) << 20) | (uint)(4 & 0xFFFFF);
        var changes = new SectionBlockChange[] { new((short)((5 << 8) | (6 << 4) | 7), 11) };
        await harness.ApplyAsync(new ClientboundSectionBlocksUpdatePacket(
            sectionPos, LegacyChunkX: 0, LegacyChunkZ: 0, changes));

        Assert.Equal(11, harness.State.World.GetBlockStateId(new BlockPos(21, 71, -26)));
    }

    [Fact]
    public async Task Entity_Spawn_And_Move_Tracks_Position()
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            EntityId: 42, Uuid: Guid.NewGuid(), TypeId: 0, X: 0, Y: 64, Z: 0,
            XRot: 0, YRot: 0, YHeadRot: 0, Data: 0, VelocityX: 0, VelocityY: 0, VelocityZ: 0, ModernVelocityRaw: null));

        Assert.True(harness.State.Entities.TryGet(42, out Game.Entities.Entity? entity));
        Assert.NotNull(entity);

        await harness.ApplyAsync(new ClientboundMoveEntityPosPacket(42, 4096, 0, 0, OnGround: true));

        Assert.Equal(1.0, harness.State.Entities.Get(42)!.Position.X, 3);
    }

    [Fact]
    public async Task Entity_MultiStepMove_AppliesCumulativeDeltas()
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            EntityId: 42, Uuid: Guid.NewGuid(), TypeId: 0, X: 0, Y: 64, Z: 0,
            XRot: 0, YRot: 0, YHeadRot: 0, Data: 0, VelocityX: 0, VelocityY: 0, VelocityZ: 0, ModernVelocityRaw: null));

        await harness.ApplyAsync(new ClientboundMoveEntityPosPacket(42, 0, 0, 0, OnGround: true)
        {
            Steps = [new EntityMoveStep(0, 4096, 0, 0), new EntityMoveStep(1, 0, 2048, 0)],
        });

        Assert.Equal(1.0, harness.State.Entities.Get(42)!.Position.X, 3);
        Assert.Equal(64.5, harness.State.Entities.Get(42)!.Position.Y, 3);
    }

    [Fact]
    public async Task Entity_Remove_Removes_From_Store()
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            7, Guid.NewGuid(), 0, 0, 64, 0, 0, 0, 0, 0, 0, 0, 0, null));
        await harness.ApplyAsync(new ClientboundRemoveEntitiesPacket([7]));
        Assert.False(harness.State.Entities.TryGet(7, out _));
    }

    [Fact]
    public async Task UpdateMobEffect_ForSelf_TracksInSelfState_AndRaisesEvent()
    {
        var harness = new ApplierHarness(Version);
        harness.State.Self.EntityId = 100;
        EntityEffectApplied? seen = null;
        harness.Events.Subscribe<EntityEffectApplied>(e => seen = e);

        // The server sends update_mob_effect for the local player itself; Self is not in the entity store, so it must be tracked in SelfState.ActiveEffects.
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(EntityId: 100, EffectId: 1, Amplifier: 0, Duration: 600, Flags: 0));

        Assert.True(harness.State.Self.ActiveEffects.ContainsKey(1));
        Assert.Equal(600, harness.State.Self.ActiveEffects[1].Duration);
        Assert.NotNull(seen);
        Assert.Equal(100, seen!.EntityId);
        Assert.Equal(1, seen.EffectId);
        Assert.True(harness.ConditionsPushCount > 0); // self effects mark physics conditions dirty
    }

    [Fact]
    public async Task RemoveMobEffect_ForSelf_ClearsSelfEffect_AndRaisesEvent()
    {
        var harness = new ApplierHarness(Version);
        harness.State.Self.EntityId = 100;
        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(100, 1, 0, 600, 0));
        Assert.True(harness.State.Self.ActiveEffects.ContainsKey(1));

        EntityEffectRemoved? removed = null;
        harness.Events.Subscribe<EntityEffectRemoved>(e => removed = e);
        await harness.ApplyAsync(new ClientboundRemoveMobEffectPacket(100, 1));

        Assert.False(harness.State.Self.ActiveEffects.ContainsKey(1));
        Assert.NotNull(removed);
        Assert.Equal(1, removed!.EffectId);
    }

    [Fact]
    public async Task UpdateMobEffect_ForTrackedEntity_RaisesEvent_NotSelfStore()
    {
        var harness = new ApplierHarness(Version);
        harness.State.Self.EntityId = 100;
        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            42, Guid.NewGuid(), 0, 0, 64, 0, 0, 0, 0, 0, 0, 0, 0, null));
        EntityEffectApplied? seen = null;
        harness.Events.Subscribe<EntityEffectApplied>(e => seen = e);

        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(42, 1, 0, 600, 0));

        Assert.NotNull(seen);
        Assert.Equal(42, seen!.EntityId);
        Assert.False(harness.State.Self.ActiveEffects.ContainsKey(1)); // a non-self effect stays off SelfState
    }

    [Fact]
    public async Task UpdateMobEffect_ForUntrackedEntity_RaisesNoEvent()
    {
        var harness = new ApplierHarness(Version);
        harness.State.Self.EntityId = 100;
        bool raised = false;
        harness.Events.Subscribe<EntityEffectApplied>(_ => raised = true);

        await harness.ApplyAsync(new ClientboundUpdateMobEffectPacket(999, 1, 0, 600, 0));

        Assert.False(raised);
    }

    [Fact]
    public async Task Scoreboard_Objective_And_Score_Tracked()
    {
        var harness = new ApplierHarness(Version);
        await harness.ApplyAsync(new ClientboundSetObjectivePacket(
            "obj", ScoreboardObjectiveMode.Add, Umpk.Text.Component.Text("Objective"),
            Game.Scoreboard.ObjectiveRenderType.Integer, NumberFormat: null));
        await harness.ApplyAsync(new ClientboundSetScorePacket("player", "obj", 5, DisplayName: null, NumberFormat: null));

        Assert.True(harness.State.Scoreboard.TryGetObjective("obj", out _));
        Assert.True(harness.State.Scoreboard.TryGetScore("obj", "player", out int value));
        Assert.Equal(5, value);
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
