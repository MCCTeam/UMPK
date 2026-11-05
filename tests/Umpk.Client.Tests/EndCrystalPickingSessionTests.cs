using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EndCrystalPickingSessionTests
{
    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task DistantNativePickableCrystalLeavesPublicViewMiningActive(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", far: true);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Mine(caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick(), protocol);
            AssertContinued(await session.Tick(), protocol);
            Assert.False(mining.IsCompleted);
            Assert.Equal(Session.Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    public static IEnumerable<object[]> CrystalPositions()
    {
        foreach (int protocol in Enumerable.Range(769, 9))
            foreach (string scenario in new[] { "near", "behind-block", "radius-hit", "radius-miss", "inside" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(CrystalPositions))]
    public async Task NativeFixedBoundsAndZeroRadiusControlPublicMining(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        Vec3d position = scenario switch
        {
            "behind-block" => new(.5, 65, 6),
            "radius-hit" => new(1.4999999, 65, 2.5),
            "radius-miss" => new(1.5000001, 65, 2.5),
            "inside" => new(.5, 64, .5),
            _ => new(.5, 65, 2.5),
        };
        await session.SpawnAt("end_crystal", position);
        await session.AssertMining(scenario is "behind-block" or "radius-miss");
        Assert.Equal(Session.Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task RelativePositionImmediatelyIntervenesAndDoesNotCreateTravel(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.SpawnAt("end_crystal", new(.5, 65, 6));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Mine(caller.Token);
        await session.Queued();
        AssertStarted(await session.Tick(), protocol);
        var move = new ClientboundMoveEntityPosPacket(42, 0, 0, -14336, false)
        {
            Steps = protocol == 777 ? [new(3, 0, 0, -14336)] : [],
        };
        await session.Send(move);
        Assert.Empty(await session.Barrier());
        Assert.Equal(new Vec3d(.5, 65, 2.5), session.Client.State.Entities.Get(42)!.Position);
        AssertAbort(await session.Tick(), protocol);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        Assert.Empty(await session.Tick());
        Assert.Equal(new Vec3d(.5, 65, 2.5), session.Client.State.Entities.Get(42)!.Position);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task NativeMetadataAndReceivedVelocityKeepTheFixedBody(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.Send(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(0, MetadataValue.Byte(0x28)), new(6, MetadataValue.Pose(EntityPose.Swimming)), new(8, MetadataValue.OptionalPosition(new BlockPos(3, 90, 4))), new(9, MetadataValue.Boolean(false))], [])));
        byte[]? raw = protocol >= 773 ? Convert.FromHexString("31337fff5c59") : null;
        await session.Send(new ClientboundSetEntityMotionPacket(42, 1000, 2000, -3000, raw));
        Assert.Empty(await session.Barrier());
        Vec3d position = session.Client.State.Entities.Get(42)!.Position;
        Vec3d velocity = session.Client.State.Entities.Get(42)!.Velocity;
        Assert.NotEqual(Vec3d.Zero, velocity);
        Assert.Equal(protocol >= 773 ? -.39998779222364644 : .125, velocity.X, 12);
        Assert.Equal(protocol >= 773 ? .36080083012879194 : .25, velocity.Y, 12);
        Assert.Equal(protocol >= 773 ? 0 : -.375, velocity.Z, 12);
        for (int i = 0; i < 4; i++)
        {
            Assert.Empty(await session.Tick());
            Assert.Equal(position, session.Client.State.Entities.Get(42)!.Position);
            Assert.Equal(velocity, session.Client.State.Entities.Get(42)!.Velocity);
        }
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task ExternalShulkerHistoryNeedsIndependentPositionEvenAfterRemoval(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.AssertKnownBounds(true);
        await session.SpawnAt("shulker", new(8.5, 64, 8.5), 43);
        await session.AssertKnownBounds(false);
        await session.Send(new ClientboundRemoveEntitiesPacket([43]));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.Send(new ClientboundMoveEntityRotPacket(42, 90, 0, false));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.Teleport(new(0, 0, 0), 7);
        await session.AssertKnownBounds(false);
        await session.AssertMining(false);
        await session.Send(new ClientboundMoveEntityPosPacket(42, 0, 0, 0, false));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task PistonEventHistoryRemainsUnknownAfterNativeOwnerRetires(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.AssertKnownBounds(true);
        int state = session.Client.State.Registries!.Blocks[Identifier.Minecraft("piston")].Value.DefaultStateId;
        await session.Send(new ClientboundBlockUpdatePacket(new(5, 64, 5), state));
        await session.Send(new ClientboundBlockEventPacket(new(5, 64, 5), 0, 0, 0));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        for (int i = 0; i < 5; i++) Assert.Empty(await session.Tick());
        await session.AssertKnownBounds(false);
        await session.Send(new ClientboundMoveEntityRotPacket(42, 90, 0, false));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.AssertMining(false);
        await session.Sync(new(8.5, 64, 8.5));
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task MountHistoryAndReplacementUseTheExistingIdentityOwner(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        Entity original = session.Client.State.Entities.Get(42)!;
        await session.SpawnAt("cow", new(8.5, 64, 8.5), 43);
        await session.Send(new ClientboundSetPassengersPacket(43, [42]));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.Send(new ClientboundSetPassengersPacket(43, []));
        await session.Send(new ClientboundRemoveEntitiesPacket([43]));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.AssertMining(false);
        await session.Spawn("end_crystal", true);
        Assert.NotSame(original, session.Client.State.Entities.Get(42));
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
        await session.Send(new ClientboundRemoveEntitiesPacket([42]));
        Assert.Empty(await session.Barrier());
        Assert.Null(session.Client.State.Entities.Get(42));
        await session.AssertMining(true);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task DistantUnownedShulkerAndUntrackedMovingPistonStayUnknown(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        var cell = new BlockPos(8, 64, 8);
        await session.Send(new ClientboundBlockUpdatePacket(cell, session.Client.State.Registries!.Blocks[Identifier.Minecraft("moving_piston")].Value.DefaultStateId));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.Send(new ClientboundBlockUpdatePacket(cell, 0));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.AssertMining(false);
        await session.Teleport(new(8.5, 64, 8.5), 0);
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
    }

    [Theory]
    [InlineData(768)]
    public async Task AdjacentUnmeasuredEraKeepsCrystalPickingUnknown(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.AssertMining(false);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task AbsoluteSnapAndIndependentRelativeBaseAreSeparateFromTeleport(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", false);
        await session.AssertKnownBounds(true);
        await session.AssertMining(false);
        await session.Sync(new(8.5, 64, 8.5));
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
        await session.Teleport(new(.5, 65, 2.5), 0);
        Assert.Equal(new Vec3d(.5, 65, 2.5), session.Client.State.Entities.Get(42)!.Position);
        await session.AssertKnownBounds(true);
        await session.AssertMining(false);
        await session.Send(new ClientboundMoveEntityPosRotPacket(42, 0, 0, 0, 90, 0, false));
        Assert.Empty(await session.Barrier());
        Assert.Equal(new Vec3d(8.5, 64, 8.5), session.Client.State.Entities.Get(42)!.Position);
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
    }

    [Theory]
    [InlineData(769, false)]
    [InlineData(770, true)]
    [InlineData(777, true)]
    public async Task FarShulkerNeedsOwnedNativeBoundsBeforeExcludingItsPush(int protocol, bool known)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.SpawnAt("shulker", new(2.5, 64, 8.5), 43);
        await session.AssertKnownBounds(known);
        await session.Send(new ClientboundRemoveEntitiesPacket([43]));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(known);
        await session.AssertMining(known);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task MissingPhysicsOwnerCannotProveThePistonHistory(int protocol)
    {
        await using Session session = await Session.Create(protocol, physics: false);
        await session.Spawn("end_crystal", true);
        await session.AssertKnownBounds(false);
        await session.Sync(new(8.5, 64, 8.5));
        await session.AssertKnownBounds(false);
        await session.AssertMining(false);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task OverflowedPistonContextSurvivesRetirementAndAbsoluteSync(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.AssertKnownBounds(true);
        int state = session.Client.State.Registries!.Blocks[Identifier.Minecraft("piston")].Value.DefaultStateId;
        for (int i = 0; i < 65; i++)
        {
            var cell = new BlockPos(2 + i % 8, 56 + i / 8, 12);
            await session.Send(new ClientboundBlockUpdatePacket(cell, state));
            await session.Send(new ClientboundBlockEventPacket(cell, 0, 0, 0));
        }
        Assert.Empty(await session.Barrier());
        for (int i = 0; i < 5; i++) Assert.Empty(await session.Tick());
        await session.Sync(new(8.5, 64, 8.5));
        await session.AssertKnownBounds(false);
        await session.AssertMining(false);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task OriginalIdentityAndWorldReplacementClearTheValidityLifetime(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.SpawnAt("shulker", new(8.5, 64, 8.5), 43);
        await session.AssertKnownBounds(false);
        World originalWorld = session.Client.State.World;
        await session.Send(new ClientboundRespawnPacket(new CommonPlayerSpawnInfo(1, "minecraft:the_nether", 0, 0, -1, false, false, null, 0, 63), 0, null));
        Assert.Empty(await session.Barrier());
        Assert.NotSame(originalWorld, session.Client.State.World);
        Assert.Empty(session.Client.State.Entities.All);
        await session.ResetScene();
        await session.Spawn("end_crystal", true);
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
        Entity retained = session.Client.State.Entities.Get(42)!;
        Assert.True(session.Client.State.EntityGeometry.TryGetDimensions(retained, out _));
        await session.Close();
        Assert.Same(retained, session.Client.State.Entities.Get(42));
        Assert.False(session.Client.State.EntityGeometry.TryGetDimensions(retained, out _));
    }

    public static IEnumerable<object[]> PlacementCases()
    {
        foreach (int protocol in new[] { 768, 769, 770, 777 })
            foreach (bool far in new[] { false, true })
                yield return [protocol, far];
    }

    [Theory]
    [MemberData(nameof(PlacementCases))]
    public async Task PublicPlacementPreservesTheExistingTwoBlockCrystalObstruction(int protocol, bool far)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", far);
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 8);
        await session.Send(new ClientboundContainerSetSlotPacket(0, 0, 36, stack));
        Assert.Empty(await session.Barrier());
        List<IPacket> packets = await session.Use();
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal(Session.Target, use.Position);
        Assert.Equal((int)Direction.North, use.Face);
        Assert.Equal(0, use.Hand);
        Assert.Equal(.5F, use.CursorX);
        Assert.Equal(.5F, use.CursorY);
        Assert.Equal(0F, use.CursorZ);
        Assert.False(use.Inside);
        Assert.Equal(far && protocol != 777 ? 2 : 1, packets.Count);
        if (far && protocol != 777)
            AssertSwing(packets[1], protocol);
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Offset(Direction.North)).IsAir);
        Assert.Equal(8, session.Client.State.Inventory.GetSlot(36).Count);
        if (far && protocol is 769 or 770)
        {
            await session.AssertKnownBounds(true);
            await session.SpawnAt("shulker", new(8.5, 64, 8.5), 43);
            await session.AssertKnownBounds(false);
            await session.Send(new ClientboundRemoveEntitiesPacket([43]));
            Assert.Empty(await session.Barrier());
            await session.AssertKnownBounds(false);
            List<IPacket> uncertain = await session.Use();
            var uncertainUse = Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(uncertain));
            Assert.Equal(Session.Target, uncertainUse.Position);
            Assert.True(uncertainUse.Sequence > use.Sequence);
            Assert.True(session.Client.State.World.GetBlock(Session.Target.Offset(Direction.North)).IsAir);
            Assert.Equal(8, session.Client.State.Inventory.GetSlot(36).Count);
            await session.Sync(new(8.5, 64, 8.5));
            await session.AssertKnownBounds(true);
            List<IPacket> restored = await session.Use();
            Assert.Equal(2, restored.Count);
            Assert.IsType<ServerboundUseItemOnPacket>(restored[0]);
            AssertSwing(restored[1], protocol);
        }
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task AnUnrelatedSpectatorTabEntryDoesNotChangeNativeCrystalPickability(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Send(new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode,
            [new(Guid.Empty, "NotAPlayer", [], false, null, GameMode.Spectator, false, 0, null, 0, false)]));
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.TabList.TryGet(Guid.Empty, out var entry));
        Assert.Equal(GameMode.Spectator, entry.GameMode);
        await session.Spawn("end_crystal", false);
        await session.AssertKnownBounds(true);
        await session.AssertMining(false);
    }

    [Theory]
    [InlineData(769)]
    [InlineData(777)]
    public async Task ExplicitCoordinateMiningPreservesItsContractWithAnInterveningCrystal(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task mining = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, ct: caller.Token);
        try
        {
            await session.Queued();
            AssertStarted(await session.Tick(), protocol, Direction.Up);
            AssertContinued(await session.Tick(), protocol);
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
        }
    }

    [Theory]
    [InlineData(770)]
    [InlineData(777)]
    public async Task MountedShulkerCannotProveFarPushIrrelevanceOrEraseItsHistory(int protocol)
    {
        await using Session session = await Session.Create(protocol);
        await session.Spawn("end_crystal", true);
        await session.SpawnAt("shulker", new(2.5, 64, 8.5), 43);
        await session.AssertKnownBounds(true);
        await session.SpawnAt("cow", new(.5, 64, 8.5), 44);
        await session.Send(new ClientboundSetPassengersPacket(44, [43]));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.Send(new ClientboundRemoveEntitiesPacket([43, 44]));
        Assert.Empty(await session.Barrier());
        await session.AssertKnownBounds(false);
        await session.AssertMining(false);
        await session.Sync(new(8.5, 64, 8.5));
        await session.AssertKnownBounds(true);
        await session.AssertMining(true);
    }

    private static void AssertStarted(IReadOnlyList<IPacket> packets, int protocol, Direction direction = Direction.North)
    {
        Assert.Equal(3, packets.Count);
        var start = Assert.IsType<ServerboundPlayerActionPacket>(packets[0]);
        Assert.Equal(0, start.Action);
        Assert.Equal(Session.Target, start.Position);
        Assert.Equal((byte)direction, start.Direction);
        Assert.True(start.Sequence > 0);
        AssertSwing(packets[1], protocol);
        AssertSwing(packets[2], protocol);
    }

    private static void AssertContinued(IReadOnlyList<IPacket> packets, int protocol)
        => AssertSwing(Assert.Single(packets), protocol);

    private static void AssertAbort(IReadOnlyList<IPacket> packets, int protocol)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(packets));
        Assert.Equal(protocol == 777 ? 2 : 1, action.Action);
        Assert.Equal(Session.Target, action.Position);
        Assert.Equal((byte)Direction.Down, action.Direction);
    }

    private static void AssertSwing(IPacket packet, int protocol)
    {
        if (protocol == 777)
            Assert.IsType<ServerboundPunchPacket>(packet);
        else
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packet).Hand);
    }

    private sealed class Session : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(0, 65, 4);
        private readonly int _protocol;
        private readonly JavaVersion _version;
        private readonly FakeJavaServer _server = FakeJavaServer.Create();
        private readonly CancellationTokenSource _budget = new(TimeSpan.FromSeconds(30));
        private long _barrier;
        private bool _closed;
        internal ObservedTicks Ticks { get; } = new();
        internal UmpkClient Client { get; }
        internal CancellationToken Token => _budget.Token;
        private PacketCodecContext Context => new(Client.State.Registries!, IConnectionCodecState.Empty);

        private Session(int protocol, bool physics)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out _version!));
            Client = new UmpkClientBuilder().UseVersion(_version)
                .UseProfile(new GameProfile(Guid.NewGuid(), "CrystalPick"))
                .UseConnectionFactory(new PipeFactory(_server.ClientPipe))
                .UseStaticRegistries(JavaGameData.Registries(protocol)).UseTickSource(Ticks)
                .ConfigureFeatures(features => { features.Physics = physics; features.Pathfinding = false; features.Entities = true; features.Terrain = true; })
                .Build();
        }

        internal static async Task<Session> Create(int protocol, bool physics = true)
        {
            var session = new Session(protocol, physics);
            try
            {
                await Task.WhenAll(session.Client.ConnectAsync(new ServerEndpoint("test", 25565), session.Token), ScriptedServer.DriveToPlayAsync(session._server, session._version, session.Token));
                var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable join = session.Client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
                var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
                await session.Send(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, null));
                await joined.Task.WaitAsync(session.Token);
                var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using IDisposable correction = session.Client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
                var position = new Vec3d(.5, 64, .5);
                await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 1, new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
                await corrected.Task.WaitAsync(session.Token);
                await session.Terrain();
                int stone = session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
                await session.Send(new ClientboundBlockUpdatePacket(new BlockPos(0, 63, 0), stone));
                await session.Send(new ClientboundBlockUpdatePacket(Target, stone));
                Assert.Empty(await session.Barrier());
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
                Assert.True(session.Client.State.Self.OnGround);
                Assert.Equal(position, session.Client.State.Self.Position);
                Assert.Equal(Target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        internal Task Mine(CancellationToken token) => Client.Actions.Interaction.DigBlockInViewAsync(Target, ct: token);
        internal Task Queued() => Client.InvokeAsync(_ => true, Token);

        internal async Task<List<IPacket>> Tick()
        {
            long before = Client.State.SessionTick;
            await Ticks.Fire(Token);
            List<IPacket> packets = await Barrier();
            Assert.Equal(before + 1, Client.State.SessionTick);
            return packets;
        }

        internal Task Spawn(string kind, bool far) => SpawnAt(kind, far ? new(8.5, 64, 8.5) : new(.5, 65, 2.5));

        internal async Task SpawnAt(string kind, Vec3d position, int id = 42)
        {
            int type = kind == "unknown" ? 1000000 : Client.State.Registries!.EntityTypes[Identifier.Minecraft(kind)].NetworkId;
            await Send(new ClientboundAddEntityPacket(id, Guid.Empty, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
            Assert.Empty(await Barrier());
            await Client.InvokeAsync(client =>
            {
                var actor = Assert.IsType<Umpk.Game.Entities.Entity>(client.State.Entities.Get(id));
                Assert.Equal(Identifier.Minecraft(kind), actor.Type.Id);
                Assert.Equal(position, actor.Position);
                return true;
            }, Token);
        }

        internal async Task AssertMining(bool starts)
        {
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
            Task mining = Mine(caller.Token);
            try
            {
                await Queued();
                List<IPacket> packets = await Tick();
                if (starts)
                {
                    AssertStarted(packets, _protocol);
                    AssertContinued(await Tick(), _protocol);
                    Assert.False(mining.IsCompleted);
                }
                else
                {
                    Assert.Empty(packets);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(Token));
                    Assert.Empty(await Tick());
                }
            }
            finally
            {
                caller.Cancel();
                if (starts || !mining.IsCompleted)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(Token));
                await Barrier();
            }
        }

        internal Task AssertKnownBounds(bool known) => Client.InvokeAsync(client =>
        {
            Entity actor = client.State.Entities.Get(42)!;
            bool actual = client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(_protocol), out Aabb bounds, out bool building, _protocol);
            Assert.Equal(known, actual);
            if (known)
            {
                Assert.True(building);
                Assert.Equal(new Aabb(actor.Position.X - 1, actor.Position.Y, actor.Position.Z - 1, actor.Position.X + 1, actor.Position.Y + 2, actor.Position.Z + 1), bounds);
            }
            return true;
        }, Token);

        internal async Task Sync(Vec3d point)
        {
            await Send(new ClientboundEntityPositionSyncPacket(42, new(point, Vec3d.Zero, 0, 0), false)
            {
                Path = _protocol == 777 ? new EntityPositionPath.Stepped([new(new(1.5, 90, 1.5), 1), new(point, 5)]) : null,
            });
            Assert.Empty(await Barrier());
            Assert.Equal(point, Client.State.Entities.Get(42)!.Position);
        }

        internal async Task Teleport(Vec3d point, int relatives)
        {
            await Send(new ClientboundTeleportEntityPacket(42, point.X, point.Y, point.Z, 0, 0, false, new(point, Vec3d.Zero, 0, 0), relatives));
            Assert.Empty(await Barrier());
        }

        internal async Task ResetScene()
        {
            var position = new Vec3d(.5, 64, .5);
            await Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, 2, new(position, Vec3d.Zero, 0, 0)));
            await Terrain();
            int stone = Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].Value.DefaultStateId;
            await Send(new ClientboundBlockUpdatePacket(new(0, 63, 0), stone));
            await Send(new ClientboundBlockUpdatePacket(Target, stone));
            Assert.Empty(await Barrier());
            Assert.Empty(await Tick());
            Assert.Empty(await Tick());
            Assert.True(Client.State.Self.OnGround);
            Assert.Equal(position, Client.State.Self.Position);
        }

        internal async Task<List<IPacket>> Use()
        {
            Task action = Client.Actions.Interaction.PlaceBlockAsync(Target, Direction.North, new(.5, .5, 0), ct: Token);
            await Queued();
            List<IPacket> packets = await Barrier();
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.False(action.IsCompleted);
            await Send(new ClientboundBlockChangedAckPacket(use.Sequence));
            await action.WaitAsync(Token);
            return packets;
        }

        private async Task Terrain()
        {
            var empty = new ChunkPalettedContainerWire(0, 0, null, []);
            var sections = Enumerable.Range(0, Client.State.World.Dimension.SectionCount).Select(_ => new ChunkSectionWire(0, _protocol >= 775 ? (short)0 : null, empty, empty)).ToArray();
            ChunkWireBody body = _protocol >= 770
                ? new ModernChunkWireBody(0, 0, _protocol >= 775, [], sections, [], new byte[7])
                : new NbtHeightmapChunkWireBody(0, 0, new NbtCompound(), sections, [], new byte[7]);
            byte[] bytes = ChunkCodecs.EncodeStructural(body);
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(_protocol, "level_chunk_with_light");
            var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(bytes, Context));
            Assert.True(packet.FullChunk);
            Assert.Equal(0, packet.ChunkX);
            Assert.Equal(0, packet.ChunkZ);
            Assert.Equal(sections.Length, packet.Column.SectionCount);
            Assert.Equal(0, packet.Column.GetBlockStateId(Target.X, Target.Y, Target.Z));
            Assert.Equal(bytes, Encode(codec, packet));
            Assert.Equal(bytes, ChunkCodecs.EncodeStructural(ChunkCodecs.DecodeStructure(packet)));
            await Send(packet);
            Assert.Empty(await Barrier());
            Assert.True(Client.State.EntityMembership.HasColumn(Target));
        }

        internal async Task Send(IPacket packet)
        {
            PhaseRegistry registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            var pair = Assert.Single(registry.Packets, pair => pair.Type.Id == packet.Type.Id);
            Assert.True(registry.TryGetInbound(pair.WireId, out BoundPacketCodec codec));
            byte[] bytes = Encode(codec, packet);
            object decoded = codec.Decode(bytes, Context);
            switch (packet)
            {
                case ClientboundAddEntityPacket add:
                    var observed = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                    Assert.Equal(add.EntityId, observed.EntityId);
                    Assert.Equal(add.TypeId, observed.TypeId);
                    Assert.Equal(new Vec3d(add.X, add.Y, add.Z), new Vec3d(observed.X, observed.Y, observed.Z));
                    break;
                case ClientboundLoginPacket login:
                    Assert.Equal(login.PlayerId, Assert.IsType<ClientboundLoginPacket>(decoded).PlayerId);
                    break;
                case ClientboundPlayerPositionPacket correction:
                    Assert.Equal(correction.ModernValues, Assert.IsType<ClientboundPlayerPositionPacket>(decoded).ModernValues);
                    break;
                case ClientboundBlockUpdatePacket block:
                    Assert.Equal(block, Assert.IsType<ClientboundBlockUpdatePacket>(decoded));
                    break;
                case ClientboundForgetLevelChunkPacket forget:
                    Assert.Equal(forget, Assert.IsType<ClientboundForgetLevelChunkPacket>(decoded));
                    break;
                case ClientboundMoveEntityPosPacket move:
                    var moved = Assert.IsType<ClientboundMoveEntityPosPacket>(decoded);
                    Assert.Equal(move.EntityId, moved.EntityId);
                    Assert.Equal(move.Steps, moved.Steps);
                    Assert.Equal(move.DeltaZ, moved.DeltaZ);
                    Assert.Equal(move.OnGround, moved.OnGround);
                    break;
                case ClientboundMoveEntityPosRotPacket moveRotation:
                    var movedRotation = Assert.IsType<ClientboundMoveEntityPosRotPacket>(decoded);
                    Assert.Equal(moveRotation.EntityId, movedRotation.EntityId);
                    Assert.Equal(moveRotation.Steps, movedRotation.Steps);
                    Assert.Equal(moveRotation.DeltaX, movedRotation.DeltaX);
                    Assert.Equal(moveRotation.Yaw, movedRotation.Yaw);
                    break;
                case ClientboundRespawnPacket respawn:
                    Assert.Equal(respawn, Assert.IsType<ClientboundRespawnPacket>(decoded));
                    break;
                case ClientboundEntityPositionSyncPacket sync:
                    var synced = Assert.IsType<ClientboundEntityPositionSyncPacket>(decoded);
                    Assert.Equal(sync.EntityId, synced.EntityId);
                    Assert.Equal(sync.Values.Position, synced.Values.Position);
                    if (sync.Path is EntityPositionPath.Stepped path)
                        Assert.Equal(path.Steps, Assert.IsType<EntityPositionPath.Stepped>(synced.Path).Steps);
                    break;
                case ClientboundSetEntityDataPacket metadata:
                    var receivedMetadata = Assert.IsType<ClientboundSetEntityDataPacket>(decoded);
                    Assert.Equal(metadata.Metadata.Entries.Count, receivedMetadata.Metadata.Entries.Count);
                    for (int i = 0; i < metadata.Metadata.Entries.Count; i++)
                    {
                        Assert.Equal(metadata.Metadata.Entries[i].Index, receivedMetadata.Metadata.Entries[i].Index);
                        Assert.Equal(metadata.Metadata.Entries[i].Value.Kind, receivedMetadata.Metadata.Entries[i].Value.Kind);
                        Assert.Equal(metadata.Metadata.Entries[i].Value, receivedMetadata.Metadata.Entries[i].Value);
                        int serializer = metadata.Metadata.Entries[i].Value.Kind switch
                        {
                            MetadataValueKind.Byte => 0,
                            MetadataValueKind.Boolean => 8,
                            MetadataValueKind.OptionalPosition => 11,
                            MetadataValueKind.Pose => _protocol <= 772 ? 21 : 20,
                            _ => throw new InvalidOperationException("This fixture requires an original-source serializer binding."),
                        };
                        Assert.Equal(serializer, receivedMetadata.Metadata.Entries[i].WireSerializerId);
                    }
                    break;
                case ClientboundSetEntityMotionPacket motion:
                    var receivedMotion = Assert.IsType<ClientboundSetEntityMotionPacket>(decoded);
                    Assert.Equal(motion.EntityId, receivedMotion.EntityId);
                    Assert.Equal(_protocol >= 773 ? 0 : motion.VelocityX, receivedMotion.VelocityX);
                    Assert.Equal(_protocol >= 773 ? 0 : motion.VelocityY, receivedMotion.VelocityY);
                    Assert.Equal(_protocol >= 773 ? 0 : motion.VelocityZ, receivedMotion.VelocityZ);
                    Assert.Equal(motion.ModernVelocityRaw, receivedMotion.ModernVelocityRaw);
                    break;
                case ClientboundSetPassengersPacket passengers:
                    Assert.Equal(passengers.Passengers, Assert.IsType<ClientboundSetPassengersPacket>(decoded).Passengers);
                    break;
                case ClientboundRemoveEntitiesPacket removed:
                    Assert.Equal(removed.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
                    break;
                case ClientboundMoveEntityRotPacket rotation:
                    Assert.Equal(rotation, Assert.IsType<ClientboundMoveEntityRotPacket>(decoded));
                    break;
                case ClientboundTeleportEntityPacket teleport:
                    var teleported = Assert.IsType<ClientboundTeleportEntityPacket>(decoded);
                    Assert.Equal(teleport.ModernValues, teleported.ModernValues);
                    Assert.Equal(teleport.Relatives, teleported.Relatives);
                    break;
                case ClientboundBlockEventPacket blockEvent:
                    Assert.Equal(blockEvent, Assert.IsType<ClientboundBlockEventPacket>(decoded));
                    break;
                case ClientboundContainerSetSlotPacket slot:
                    var receivedSlot = Assert.IsType<ClientboundContainerSetSlotPacket>(decoded);
                    Assert.Equal(slot.Slot, receivedSlot.Slot);
                    Assert.Equal(slot.Item.Item.Id, receivedSlot.Item.Item.Id);
                    Assert.Equal(slot.Item.Count, receivedSlot.Item.Count);
                    Assert.Equal(slot.Item.Components, receivedSlot.Item.Components);
                    break;
                case ClientboundPlayerInfoUpdatePacket info:
                    var receivedInfo = Assert.IsType<ClientboundPlayerInfoUpdatePacket>(decoded);
                    Assert.Equal(info.Actions, receivedInfo.Actions);
                    PlayerInfoEntry receivedEntry = Assert.Single(receivedInfo.Entries);
                    Assert.Equal(Guid.Empty, receivedEntry.ProfileId);
                    Assert.Equal(GameMode.Spectator, receivedEntry.GameMode);
                    Assert.Equal("NotAPlayer", receivedEntry.Name);
                    break;
                case ClientboundBlockChangedAckPacket ack:
                    Assert.Equal(ack, Assert.IsType<ClientboundBlockChangedAckPacket>(decoded));
                    break;
                case ClientboundPlayKeepAlivePacket keepAlive:
                    Assert.Equal(keepAlive, Assert.IsType<ClientboundPlayKeepAlivePacket>(decoded));
                    break;
            }
            Assert.Equal(bytes, Encode(codec, decoded));
            await _server.SendFrameAsync(pair.WireId, bytes, Token);
        }

        private byte[] Encode(BoundPacketCodec codec, object packet)
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, packet, Context);
            return output.WrittenSpan.ToArray();
        }

        internal async Task<List<IPacket>> Barrier()
        {
            long nonce = ++_barrier;
            await Send(new ClientboundPlayKeepAlivePacket(nonce));
            List<IPacket> packets = [];
            while (true)
            {
                InboundFrame frame = await _server.NextFrameAsync(Token);
                var registry = _version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
                Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
                var packet = Assert.IsAssignableFrom<IPacket>(codec.Decode(frame.Payload, Context));
                Assert.Equal(frame.Payload.ToArray(), Encode(codec, packet));
                Assert.IsNotType<ServerboundAttackPacket>(packet);
                Assert.False(packet is ServerboundInteractPacket { Action: 1 }, "Block mining and placement must not initiate an entity attack.");
                if (packet is ServerboundPlayKeepAlivePacket { Id: var id } && id == nonce)
                    return packets;
                if (packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundUseItemOnPacket)
                    packets.Add(packet);
            }
        }

        internal async Task Close()
        {
            if (_closed)
                return;
            await Client.DisconnectAsync(Token);
            await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
            {
                while (true)
                    await _server.NextFrameAsync(Token);
            });
            Assert.True(Ticks.SessionCancelled);
            _closed = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Close(); }
            finally
            {
                try { await Client.DisposeAsync(); }
                finally { await _server.DisposeAsync(); _budget.Dispose(); }
            }
        }
    }

    private sealed class PipeFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct) => ValueTask.FromResult(pipe);
    }

    private sealed class ObservedTicks : ITickSource
    {
        private readonly Channel<TaskCompletionSource> _ticks = Channel.CreateUnbounded<TaskCompletionSource>();
        internal bool SessionCancelled { get; private set; }
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);
        internal async Task Fire(CancellationToken ct)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await _ticks.Writer.WriteAsync(completion, ct);
            await completion.Task.WaitAsync(ct);
        }
        public async IAsyncEnumerable<long> Ticks([EnumeratorCancellation] CancellationToken ct)
        {
            long sequence = 0;
            try
            {
                await foreach (TaskCompletionSource completion in _ticks.Reader.ReadAllAsync(ct))
                {
                    yield return ++sequence;
                    completion.TrySetResult();
                }
            }
            finally { SessionCancelled = ct.IsCancellationRequested; }
        }
    }
}
