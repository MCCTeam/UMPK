using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class Protocol393FallingBlockRuntimeTests
{
    private const int Protocol = 393;

    [Fact]
    public void MeasuredStoneStateHasTheNativeUnitCollisionBox()
    {
        var source = new RegistryBlockDataSource(JavaGameData.Registries(Protocol).Blocks, false);
        var stone = new BlockState(source, 1);
        Assert.True(stone.IsValid);
        Assert.Equal(Identifier.Minecraft("stone"), stone.Block.Id);
        Assert.Equal(1, stone.Block.NetworkId);
        Assert.Equal(Aabb.BlockAt(0, 0, 0), Assert.Single(JavaGameData.BlockShapes(Protocol).GetCollisionShapes(stone).ToArray()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public async Task OriginalFramesDriveNativePhasesAndExplicitUnknownContexts(int scene)
    {
        using JsonDocument native = ReadNative();
        JsonElement row = native.RootElement.GetProperty("rows")[scene];
        JsonElement[] packets = row.GetProperty("packets").EnumerateArray().ToArray();
        await using var harness = new Harness();
        foreach (JsonElement packet in packets.Where(value => value.GetProperty("action").GetString() == "initial_full"))
            await harness.Native(packet, native);
        if (scene is 3 or 13)
            await harness.Block(new BlockPos(1, 99, 1), 1);
        if (scene == 13)
            for (int y = 99; y <= 103; y++)
                await harness.Block(new BlockPos(2, y, 2), 1);
        if (scene is 10 or 15)
            await harness.Block(new BlockPos(1, 101, 1), scene == 15 ? 8393 : 66);
        await Apply("add_entity");
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.Equal(Vec3d.Zero, actor.Velocity);
        AssertSnapshot(row.GetProperty("observations")[0], false);
        for (int tick = 0; tick < 12; tick++)
        {
            if (scene == 5 && tick == 2) await Apply("unload");
            if (scene == 5 && tick == 5) await Apply("reload");
            if (scene == 6 && tick == 2) await Apply("late_full");
            if (scene == 7 && tick == 2)
            {
                Entity old = actor;
                await Apply("replace_id");
                actor = harness.State.Entities.Get(42)!;
                Assert.NotSame(old, actor);
                Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(old, out _));
                Assert.False(harness.State.EntityMembership.TryGetLegacyMembership(old, out _));
            }
            if (scene == 11 && tick == 2) await Apply("teleport_east");
            if (scene == 11 && tick == 4) await Apply("east_full");
            if (scene == 12 && tick == 2) await Apply("vertical_velocity");
            if (scene == 13 && tick == 2) await Apply("horizontal_velocity");
            if (scene == 14 && tick == 2) await Apply("no_gravity_true");
            if (scene == 14 && tick == 5) await Apply("no_gravity_false");
            bool unsupported = scene is 11 or 13 && tick >= 2;
            await harness.Tick(false);
            AssertSnapshot(row.GetProperty("observations")[tick * 2 + 1], unsupported);
            await harness.WorldPhase();
            AssertSnapshot(row.GetProperty("observations")[tick * 2 + 2], unsupported);
        }
        return;

        Task Apply(string action) => harness.Native(packets.Single(value => value.GetProperty("action").GetString() == action), native);

        void AssertSnapshot(JsonElement expected, bool unsupported)
        {
            Assert.True(harness.State.EntityMembership.TryGetLegacyMembership(actor, out EntityMembershipState.LegacyMembershipSnapshot membership));
            Vec3d packetBase = harness.State.PacketBases.Get(actor);
            JsonElement encodedBase = expected.GetProperty("packet_base");
            Assert.Equal(encodedBase[0].GetInt64(), (long)Math.Floor(packetBase.X * 4096));
            Assert.Equal(encodedBase[1].GetInt64(), (long)Math.Floor(packetBase.Y * 4096));
            Assert.Equal(encodedBase[2].GetInt64(), (long)Math.Floor(packetBase.Z * 4096));
            if (unsupported)
            {
                Assert.False(membership.RegistrationKnown);
                Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
                Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
                return;
            }
            Assert.True(membership.RegistrationKnown);
            Assert.Equal(expected.GetProperty("in_entity_list").GetBoolean(), membership.InEntityList);
            Assert.Equal(expected.GetProperty("added").GetBoolean(), membership.Added);
            JsonElement chunk = expected.GetProperty("chunk");
            Assert.Equal(chunk[0].GetInt32(), membership.X);
            Assert.Equal(chunk[1].GetInt32(), membership.Y);
            Assert.Equal(chunk[2].GetInt32(), membership.Z);
            Assert.Equal(Point(expected.GetProperty("position")), actor.Position);
            Assert.Equal(Point(expected.GetProperty("velocity")), actor.Velocity);
            Assert.Equal(expected.GetProperty("on_ground").GetBoolean(), actor.OnGround);
            Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(Protocol), out Aabb box, out bool blocksBuilding, Protocol));
            Assert.True(blocksBuilding);
            Assert.Equal(Box(expected.GetProperty("box")), box);
            bool? member = harness.State.EntityMembership.IsWorldQueryMember(actor);
            Assert.NotNull(member);
            bool clear = member == false || !VanillaPlacementCollision.Intersects(new BlockPos(1, 101, 1), box, BlockInteractionProfile.ForProtocol(Protocol));
            Assert.Equal(expected.GetProperty("query_clear").GetBoolean(), clear);
            var sourceCell = new BlockPos(1, 101, 1);
            int nativeSourceState = expected.GetProperty("source_state").GetInt32();
            if (!expected.GetProperty("center_loaded").GetBoolean())
            {
                Assert.Equal(8574, nativeSourceState);
                var source = new RegistryBlockDataSource(JavaGameData.Registries(Protocol).Blocks, false);
                Assert.Equal(Identifier.Minecraft("void_air"), new BlockState(source, nativeSourceState).Block.Id);
                Assert.False(harness.State.EntityMembership.HasColumn(sourceCell));
                Assert.Null(harness.State.World.GetColumn(sourceCell));
                Assert.Equal(0, harness.State.World.GetBlockStateId(sourceCell));
            }
            else
                Assert.Equal(nativeSourceState, harness.State.World.GetBlockStateId(sourceCell));
        }
    }

    [Fact]
    public void All88OriginalPacketBodiesRetainDecodedValuesAndExactBytes()
    {
        using JsonDocument native = ReadNative();
        int count = 0;
        foreach (JsonElement scene in native.RootElement.GetProperty("rows").EnumerateArray())
            foreach (JsonElement packet in scene.GetProperty("packets").EnumerateArray())
            {
                _ = DecodeNative(packet, native);
                count++;
            }
        Assert.Equal(88, count);
        Assert.Equal(400, native.RootElement.GetProperty("rows").EnumerateArray().Sum(row => row.GetProperty("observations").GetArrayLength()));
    }

    [Fact]
    public void All95OriginalHardTargetOwnersBoundTheFinitePeerProfile()
    {
        using JsonDocument native = ReadNative();
        int soft = 0, hard = 0;
        foreach (JsonElement peer in native.RootElement.GetProperty("hardTargets").EnumerateArray())
        {
            bool expected = peer.GetProperty("targetBoxOwner").GetString() == "aeo";
            Assert.Equal(expected, LegacyFallingBlockProfile.HasNoHardPeerBox(Identifier.Parse(peer.GetProperty("id").GetString()!), Protocol));
            if (expected) soft++; else hard++;
        }
        Assert.Equal(93, soft);
        Assert.Equal(2, hard);
        Assert.False(LegacyFallingBlockProfile.HasNoHardPeerBox(Identifier.Parse("example:cow"), Protocol));
        Assert.False(LegacyFallingBlockProfile.HasNoHardPeerBox(Identifier.Minecraft("unmeasured_entity"), Protocol));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public async Task UnsupportedContextStaysUnknownAfterLaterPackets(int scene)
    {
        await using var harness = new Harness();
        await harness.Column();
        int carried = scene == 15 ? harness.State.Registries!.Blocks[Identifier.Minecraft("chest")].Value.DefaultStateId : 66;
        await harness.Spawn(42, 101, scene == 13 ? 15.5 : 1.5, carried);
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
        if (scene < 6)
        {
            string[] names = ["water", "lava", "cobweb", "slime_block", "oak_slab", "moving_piston"];
            await harness.Block(new BlockPos(1, 100, 1), names[scene]);
        }
        else if (scene == 6)
            await harness.Apply("add_entity", new ClientboundAddEntityPacket(43, Guid.Empty, 1, 1000, 101, 1000, 0, 0, 0, 0, 0, 0, 0, null));
        else if (scene == 7)
            await harness.Mob(43, "shulker", 1000);
        else if (scene == 8)
            await harness.Owner(() => harness.State.World.SetBorder(harness.State.World.Border.WithSize(4).WithCenter(1.5, 1.5)));
        else if (scene is 9 or 10)
        {
            await harness.Spawn(43, 30);
            await harness.Apply("set_passengers", scene == 9 ? new ClientboundSetPassengersPacket(43, [42]) : new ClientboundSetPassengersPacket(42, [43]));
        }
        else if (scene == 11)
            await harness.Apply("set_entity_data", new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(5, MetadataValue.Byte(1))], [])));
        else if (scene == 12)
            await harness.Apply("set_entity_motion", new ClientboundSetEntityMotionPacket(42, 1, 0, 0));
        else if (scene == 14)
            await harness.Owner(() => harness.State.World.UnloadColumn(new ChunkPos(0, 0)));
        else if (scene == 15)
            await harness.Block(new BlockPos(1, 101, 1), carried);
        await harness.Tick();
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
        Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
        if (scene == 15)
            Assert.Equal(carried, harness.State.World.GetBlockStateId(new BlockPos(1, 101, 1)));
        await harness.Apply("forget_level_chunk", new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        await harness.Tick();
        Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
        await harness.Block(new BlockPos(1, 100, 1), 0);
        await harness.Apply("set_passengers", new ClientboundSetPassengersPacket(43, []));
        await harness.Apply("set_passengers", new ClientboundSetPassengersPacket(42, []));
        await harness.Apply("remove_entities", new ClientboundRemoveEntitiesPacket([43]));
        await harness.Owner(() => harness.State.World.SetBorder(WorldBorderState.Default));
        await harness.Column();
        await harness.Apply("teleport_entity", new ClientboundTeleportEntityPacket(42, 1.5, 30, 1.5, 0, 0, false, null, 0));
        await harness.Apply("set_entity_motion", new ClientboundSetEntityMotionPacket(42, 0, 0, 0));
        await harness.Apply("set_entity_data", new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(5, MetadataValue.Boolean(false))], [])));
        await harness.Tick();
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
        Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
        await harness.Spawn(42, 101);
        Entity replacement = harness.State.Entities.Get(42)!;
        Assert.NotSame(actor, replacement);
        await harness.Tick();
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(replacement, out _));
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(replacement));
    }

    [Theory]
    [InlineData("item")]
    [InlineData("cow")]
    public async Task ProvenSoftPeersDoNotInvalidateFallingMotion(string peer)
    {
        await using var harness = new Harness();
        await harness.Column();
        await harness.Spawn(42, 101);
        if (peer == "item")
            await harness.Apply("add_entity", new ClientboundAddEntityPacket(43, Guid.Empty, 2, 1.5, 101, 1.5, 0, 0, 0, 0, 0, 0, 0, null));
        else
            await harness.Mob(43, peer, 1.5);
        await harness.Tick();
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(harness.State.Entities.Get(42)!, out Aabb box));
        Assert.Equal(100.96999999135733, box.MinY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockUpdateStorageDoesNotEstablishPacketColumnMembership(bool sourceBlock)
    {
        await using var harness = new Harness();
        await harness.Block(new BlockPos(1, 101, 1), sourceBlock ? 66 : 0);
        await harness.Spawn(42, 101);
        Entity actor = harness.State.Entities.Get(42)!;
        await harness.Tick();
        Assert.NotNull(harness.State.World.GetColumn(new BlockPos(1, 64, 1)));
        Assert.False(harness.State.EntityMembership.HasColumn(new BlockPos(1, 64, 1)));
        Assert.False(harness.State.EntityMembership.CanTick(actor));
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(actor, out Aabb box));
        Assert.Equal(101.00999999046326, box.MinY);
        Assert.Equal(Vec3d.Zero, actor.Velocity);
        Assert.Equal(sourceBlock ? 66 : 0, harness.State.World.GetBlockStateId(new BlockPos(1, 101, 1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task AirAndUnavailableCarriedStatesStayUnknownAfterTheFirstTick(int scene)
    {
        await using var harness = new Harness();
        await harness.Column();
        int state = scene switch
        {
            1 => harness.State.Registries!.Blocks[Identifier.Minecraft("cave_air")].Value.DefaultStateId,
            2 => harness.State.Registries!.Blocks[Identifier.Minecraft("void_air")].Value.DefaultStateId,
            3 => -1,
            4 => int.MaxValue,
            _ => 0,
        };
        await harness.Spawn(42, 101, data: state);
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
        await harness.Tick();
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
        await harness.Apply("teleport_entity", new ClientboundTeleportEntityPacket(42, 1.5, 30, 1.5, 0, 0, false, null, 0));
        await harness.Apply("set_entity_motion", new ClientboundSetEntityMotionPacket(42, 0, 0, 0));
        await harness.Tick();
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task TeleportThresholdPreservesPacketBaseAndReappliesNativeBox(int scene)
    {
        await using var harness = new Harness();
        await harness.Column();
        await harness.Spawn(42, 101);
        await harness.Tick();
        await harness.Tick();
        await harness.Tick();
        Entity actor = harness.State.Entities.Get(42)!;
        Vec3d before = actor.Position;
        Vec3d target = scene switch
        {
            0 => new(Math.BitDecrement(before.X + .03125), before.Y, before.Z),
            1 => before.Add(.03125, 0, 0),
            2 => before.Add(-.03125, 0, 0),
            3 => new(before.X, Math.BitDecrement(before.Y + .015625), before.Z),
            4 => before.Add(0, .015625, 0),
            5 => before.Add(0, -.015625, 0),
            6 => before.Add(0, 0, .03125),
            _ => before,
        };
        await harness.Apply("teleport_entity", new ClientboundTeleportEntityPacket(42, target.X, target.Y, target.Z, 0, 0, false, null, 0));
        Vec3d expected = scene is 0 or 3 or 7 ? before : target;
        Assert.Equal(expected, actor.Position);
        Assert.Equal(target, harness.State.PacketBases.Get(actor));
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(actor, out Aabb box));
        Assert.Equal(new Aabb(expected.X - .98F / 2F, expected.Y, expected.Z - .98F / 2F, expected.X + .98F / 2F, expected.Y + .98F, expected.Z + .98F / 2F), box);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task RemovalReplacementAndWorldLifetimesDiscardOwnedState(int scene)
    {
        await using var harness = new Harness();
        await harness.Column();
        await harness.Spawn(42, 101);
        Entity old = harness.State.Entities.Get(42)!;
        await harness.Tick();
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(old, out _));
        if (scene == 0)
            await harness.Apply("remove_entities", new ClientboundRemoveEntitiesPacket([42]));
        else if (scene == 1)
            await harness.Spawn(42, 30);
        else
            await harness.Owner(() =>
            {
                if (scene == 2) harness.State.ResetForRespawn(harness.State.World);
                else if (scene == 3) harness.State.ResetForConfigurationReentry();
                else if (scene == 4) harness.State.ResetForSessionEnd();
                else WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), Protocol);
            });
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(old, out _));
        Assert.False(harness.State.EntityMembership.TryGetLegacyMembership(old, out _));
        if (scene is 2 or 3 or 4)
            await harness.Owner(() => WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), Protocol));
        await harness.Column();
        await harness.Spawn(42, 101);
        Entity current = harness.State.Entities.Get(42)!;
        Assert.NotSame(old, current);
        await harness.Tick();
        Assert.True(harness.State.EntityGeometry.TryGetFallingBlock(current, out Aabb box));
        Assert.Equal(100.96999999135733, box.MinY);
        Assert.Equal(-0.03919999988675116, current.Velocity.Y);
    }

    [Theory]
    [InlineData(340, 12)]
    [InlineData(401, 66)]
    [InlineData(404, 66)]
    public async Task UnmeasuredAdjacentProfilesKeepTheirGeometryUnknown(int protocol, int data)
    {
        await using var harness = new Harness(protocol);
        await harness.Spawn(42, 101, data: data);
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.False(LegacyFallingBlockProfile.Supports(actor, protocol));
        Assert.False(harness.State.EntityGeometry.TryGetFallingBlock(actor, out _));
        Assert.False(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    public async Task ConnectedPlacementUsesCurrentFallingBoundsAndNativeSendPolicy(int scene, int expectedPackets)
    {
        await using var session = await BlockUseSession.Create(Protocol, "stone", physics: true);
        await session.Mode(GameMode.Creative, true);
        await session.Hold("stone");
        using JsonDocument native = ReadNative();
        if (scene != 0)
        {
            JsonElement spawn = native.RootElement.GetProperty("rows")[scene == 1 ? 4 : 1].GetProperty("packets").EnumerateArray().Single(value => value.GetProperty("action").GetString() == "add_entity");
            await session.Send(DecodeNative(spawn, native));
            Assert.Empty(await session.Barrier());
            Entity actor = session.Client.State.Entities.Get(42)!;
            Assert.True(await session.Client.InvokeAsync(client => client.State.EntityGeometry.TryGetFallingBlock(actor, out _), session.Token));
            if (scene == 3)
                await session.Send(new ClientboundRemoveEntitiesPacket([42]));
            if (scene == 4)
                for (int tick = 0; tick < 12; tick++)
                    await session.Tick();
        }
        var support = new BlockPos(1, 100, 1);
        await session.Block(support, "stone");
        await session.Block(support.Above(), "air");
        await session.Send(new ClientboundPlayerPositionPacket(.5, 101, -1.5, 0, 0, 0, 22, null));
        _ = await session.Barrier();
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(support, Direction.Up, new Vec3d(.5, 1, .5), ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        await action.WaitAsync(session.Token);
        Assert.Equal(expectedPackets, packets.Count);
        ServerboundUseItemOnPacket use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal(support, use.Position);
        Assert.Equal((int)Direction.Up, use.Face);
        Assert.Equal(0, use.Hand);
        Assert.Equal(.5F, use.CursorX);
        Assert.Equal(1F, use.CursorY);
        Assert.Equal(.5F, use.CursorZ);
        if (expectedPackets == 2)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        // Public received state is retained; this does not claim native local BlockItem world/stack prediction.
        Assert.True(session.Client.State.World.GetBlock(support.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Fact]
    public async Task ConnectedSessionClockUsesOriginalFlattenedSpawnAndGravityPackets()
    {
        await using var session = await BlockUseSession.Create(Protocol, "stone", physics: true);
        using JsonDocument native = ReadNative();
        JsonElement row = native.RootElement.GetProperty("rows")[14];
        JsonElement[] packets = row.GetProperty("packets").EnumerateArray().ToArray();
        var publication = new TaskCompletionSource<(Vec3d Position, Vec3d Velocity, bool Known, Aabb Box)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable spawned = session.Client.Events.Subscribe<EntitySpawned>(value =>
        {
            if (value.EntityId == 42)
            {
                bool known = session.Client.State.EntityGeometry.TryGetFallingBlock(value.Entity, out Aabb box);
                publication.TrySetResult((value.Entity.Position, value.Entity.Velocity, known, box));
            }
        });
        await session.Send(DecodeNative(packets.Single(value => value.GetProperty("action").GetString() == "add_entity"), native));
        Assert.Empty(await session.Barrier());
        var published = await publication.Task.WaitAsync(session.Token);
        JsonElement initial = row.GetProperty("observations")[0];
        Assert.True(published.Known);
        Assert.Equal(Point(initial.GetProperty("position")), published.Position);
        Assert.Equal(Vec3d.Zero, published.Velocity);
        Assert.Equal(Box(initial.GetProperty("box")), published.Box);
        Entity actor = session.Client.State.Entities.Get(42)!;
        for (int tick = 0; tick < 12; tick++)
        {
            if (tick is 2 or 5)
            {
                string action = tick == 2 ? "no_gravity_true" : "no_gravity_false";
                await session.Send(DecodeNative(packets.Single(value => value.GetProperty("action").GetString() == action), native));
                Assert.Empty(await session.Barrier());
            }
            await session.Tick();
            JsonElement expected = row.GetProperty("observations")[tick * 2 + 2];
            Assert.Equal(Point(expected.GetProperty("position")), actor.Position);
            Assert.Equal(Point(expected.GetProperty("velocity")), actor.Velocity);
            Assert.True(await session.Client.InvokeAsync(client => client.State.EntityGeometry.TryGetFallingBlock(actor, out _), session.Token));
            _ = await session.Barrier();
        }
    }

    [Fact]
    public async Task ConnectedDisconnectAndRestartDiscardOldFallingInstance()
    {
        await using var session = await BlockUseSession.Create(Protocol, "stone", physics: true);
        using JsonDocument native = ReadNative();
        JsonElement spawn = native.RootElement.GetProperty("rows")[15].GetProperty("packets").EnumerateArray().Single(value => value.GetProperty("action").GetString() == "add_entity");
        await session.Send(DecodeNative(spawn, native));
        Assert.Empty(await session.Barrier());
        Entity old = session.Client.State.Entities.Get(42)!;
        Assert.True(await session.Client.InvokeAsync(client => client.State.EntityGeometry.TryGetFallingBlock(old, out _), session.Token));
        await session.Client.DisconnectAsync(session.Token);
        Assert.False(session.Client.State.EntityGeometry.TryGetFallingBlock(old, out _));
        await session.Restart("stone");
        await session.Send(DecodeNative(spawn, native));
        Assert.Empty(await session.Barrier());
        Entity current = session.Client.State.Entities.Get(42)!;
        Assert.NotSame(old, current);
        var sourceCell = new BlockPos(1, 101, 1);
        await session.Block(sourceCell, "white_concrete_powder");
        Assert.Empty(await session.Barrier());
        Assert.Equal(8393, session.Client.State.World.GetBlockStateId(sourceCell));
        await session.Tick();
        Assert.Equal(100.96999999135733, current.Position.Y);
        Assert.True(session.Client.State.World.GetBlock(sourceCell).IsAir);
    }

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(Protocol393FallingBlockRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.Protocol393FallingBlock.native.json.gz")!;
        using var compressed = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(compressed);
    }

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static Aabb Box(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble());

    private static string PacketName(JsonElement packet) => packet.GetProperty("type").GetString() switch
    {
        "kg" => "level_chunk_with_light",
        "iz" => "add_entity",
        "kd" => "forget_level_chunk",
        "ly" => "teleport_entity",
        "li" => "set_entity_motion",
        "lg" => "set_entity_data",
        _ => throw new InvalidOperationException("Unrecognized native393 packet type"),
    };

    private static IPacket DecodeNative(JsonElement record, JsonDocument native)
    {
        byte[] body = Convert.FromHexString(native.RootElement.GetProperty("packetBodies").GetProperty(record.GetProperty("sha256").GetString()!).GetString()!);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(Protocol, PacketName(record));
        var context = new PacketCodecContext(JavaGameData.Registries(Protocol), IConnectionCodecState.Empty);
        IPacket packet = Assert.IsAssignableFrom<IPacket>(codec.Decode(body, context));
        Assert.Equal(body, Encode(codec, packet, context));
        JsonElement fields = record.GetProperty("decoded");
        switch (packet)
        {
            case ClientboundAddEntityPacket spawn:
                Assert.Equal(fields.GetProperty("a").GetInt32(), spawn.EntityId);
                Assert.Equal(Guid.Parse(fields.GetProperty("b").GetString()!), spawn.Uuid);
                Assert.Equal(fields.GetProperty("k").GetInt32(), spawn.TypeId);
                Assert.Equal(new Vec3d(fields.GetProperty("c").GetDouble(), fields.GetProperty("d").GetDouble(), fields.GetProperty("e").GetDouble()), new Vec3d(spawn.X, spawn.Y, spawn.Z));
                Assert.Equal(fields.GetProperty("l").GetInt32(), spawn.Data);
                Assert.Equal(fields.GetProperty("f").GetInt16(), spawn.VelocityX);
                Assert.Equal(fields.GetProperty("g").GetInt16(), spawn.VelocityY);
                Assert.Equal(fields.GetProperty("h").GetInt16(), spawn.VelocityZ);
                break;
            case ClientboundLevelChunkPacket chunk:
                Assert.Equal(fields.GetProperty("a").GetInt32(), chunk.ChunkX);
                Assert.Equal(fields.GetProperty("b").GetInt32(), chunk.ChunkZ);
                Assert.Equal(fields.GetProperty("f").GetBoolean(), chunk.FullChunk);
                Assert.Equal(1, chunk.Column.GetBlockStateId(chunk.ChunkX * 16, 0, chunk.ChunkZ * 16));
                break;
            case ClientboundSetEntityDataPacket metadata:
                Assert.Equal(fields.GetProperty("a").GetInt32(), metadata.EntityId);
                var entry = Assert.Single(metadata.Metadata.Entries);
                Assert.Equal(5, entry.Index);
                Assert.Equal(MetadataValueKind.Boolean, entry.Value.Kind);
                Assert.Equal(fields.GetProperty("metadata")[0].GetProperty("value").GetBoolean(), entry.Value.AsBoolean());
                break;
            case ClientboundSetEntityMotionPacket motion:
                Assert.Equal(fields.GetProperty("a").GetInt32(), motion.EntityId);
                Assert.Equal(fields.GetProperty("b").GetInt16(), motion.VelocityX);
                Assert.Equal(fields.GetProperty("c").GetInt16(), motion.VelocityY);
                Assert.Equal(fields.GetProperty("d").GetInt16(), motion.VelocityZ);
                break;
            case ClientboundTeleportEntityPacket teleport:
                Assert.Equal(fields.GetProperty("a").GetInt32(), teleport.EntityId);
                Assert.Equal(new Vec3d(fields.GetProperty("b").GetDouble(), fields.GetProperty("c").GetDouble(), fields.GetProperty("d").GetDouble()), new Vec3d(teleport.X, teleport.Y, teleport.Z));
                Assert.Equal(fields.GetProperty("g").GetBoolean(), teleport.OnGround);
                break;
            case ClientboundForgetLevelChunkPacket unload:
                Assert.Equal(new ChunkPos(fields.GetProperty("a").GetInt32(), fields.GetProperty("b").GetInt32()), unload.Chunk);
                break;
            default:
                Assert.Fail("Native packet did not resolve its expected implemented bound type");
                break;
        }
        return packet;
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ApplierHarness _applier;
        private readonly ChannelSessionScheduler _scheduler = new();
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(30));
        private readonly int _protocol;
        public ClientState State => _applier.State;

        public Harness(int protocol = Protocol)
        {
            _protocol = protocol;
            _applier = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = true, Entities = true, Inventory = true, Physics = false, Pathfinding = false });
            State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        }

        public Task Owner(Action action) => _scheduler.InvokeAsync(action, _lifetime.Token);
        public Task Tick(bool worldPhase = true) => Owner(() =>
        {
            State.TickEntities(_protocol, JavaGameData.BlockShapes(_protocol), []);
            if (worldPhase) State.EntityMembership.FinishWorldPhase();
        });
        public Task WorldPhase() => Owner(State.EntityMembership.FinishWorldPhase);
        public Task Spawn(int id, double y, double x = 1.5, int data = 66) => Apply("add_entity", new ClientboundAddEntityPacket(id, Guid.Empty, 70, x, y, 1.5, 0, 0, 0, data, 8000, 16000, -8000, null));
        public Task Block(BlockPos position, int state) => Apply("block_update", new ClientboundBlockUpdatePacket(position, state));
        public Task Block(BlockPos position, string name) => Block(position, State.Registries!.Blocks[Identifier.Minecraft(name)].Value.DefaultStateId);
        public Task Mob(int id, string name, double x) => Apply("add_mob", new ClientboundAddMobPacket(id, State.Registries!.EntityTypes[Identifier.Minecraft(name)].NetworkId, x, 101, x, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], []), Guid.Empty));

        public Task Native(JsonElement record, JsonDocument native)
        {
            IPacket packet = DecodeNative(record, native);
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(packet), _lifetime.Token);
        }

        public Task Column()
        {
            using JsonDocument native = ReadNative();
            JsonElement packet = native.RootElement.GetProperty("rows")[1].GetProperty("packets")[0];
            return Native(packet, native);
        }

        public Task Apply(string name, IPacket packet)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            byte[] body = Encode(codec, packet, context);
            IPacket decoded = Assert.IsAssignableFrom<IPacket>(codec.Decode(body, context));
            Assert.Equal(body, Encode(codec, decoded, context));
            Assert.Equal(packet.GetType(), decoded.GetType());
            return _scheduler.InvokeAsync(() => _applier.ApplyAsync(decoded), _lifetime.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            await _scheduler.DisposeAsync();
            _lifetime.Dispose();
        }
    }
}
