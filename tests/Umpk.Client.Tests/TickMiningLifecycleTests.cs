using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class TickMiningLifecycleTests
{
    public static IEnumerable<object[]> Protocols => JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Order().Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task CreativeUnbreakableAttacksStillSendStartAndOneSwing(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        fixture.State.Self.GameMode = GameMode.Creative;
        fixture.State.Self.InstantBuild = true;
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            await mining.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, fixture.Packets.Count);
            ServerboundPlayerActionPacket start = Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[protocol == 47 ? 1 : 0]);
            Assert.Equal(0, start.Action);
            Assert.Equal(Fixture.Target, start.Position);
            object animation = fixture.Packets[protocol == 47 ? 0 : 1];
            if (protocol == 777)
                Assert.IsType<ServerboundPunchPacket>(animation);
            else
                Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(animation).Hand);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task UnbreakableBlocksKeepSwingingUntilTheHeldAttackIsReleased(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.SetBlock("bedrock");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            for (int tick = 0; tick < 6; tick++)
                await fixture.TickAsync();

            Assert.False(mining.IsCompleted);
            ServerboundPlayerActionPacket start = Assert.Single(fixture.Packets.OfType<ServerboundPlayerActionPacket>());
            Assert.Equal(0, start.Action);
            Assert.Equal(Fixture.Target, start.Position);
            Assert.Equal(7, fixture.Packets.Count(packet => packet is ServerboundPunchPacket or ServerboundSwingPacket));
            foreach (object animation in fixture.Packets.Where(packet => packet is ServerboundPunchPacket or ServerboundSwingPacket))
            {
                if (protocol == 777)
                    Assert.IsType<ServerboundPunchPacket>(animation);
                else
                    Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(animation).Hand);
            }
            Assert.Same(start, fixture.Packets[protocol == 47 ? 1 : 0]);
            Assert.Equal(Identifier.Minecraft("bedrock"), fixture.State.World.GetBlock(Fixture.Target).Block.Id);

            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));

            ServerboundPlayerActionPacket abort = Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[^1]);
            Assert.Equal(protocol == 777 ? 2 : 1, abort.Action);
            Assert.Equal((byte)Direction.Down, abort.Direction);
            Assert.Equal(2, fixture.Packets.OfType<ServerboundPlayerActionPacket>().Count());
            int count = fixture.Packets.Count;
            await fixture.TickAsync();
            Assert.Equal(count, fixture.Packets.Count);
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task RemovingTheDefaultToolComponentUsesBareHandMining()
    {
        await using var fixture = new Fixture();
        fixture.Hold("diamond_pickaxe", removeTool: true);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            for (int tick = 0; tick < 150; tick++)
                await fixture.TickAsync();
            Assert.DoesNotContain(fixture.Packets, packet => packet is ServerboundPlayerActionPacket { Action: 3 });
            await fixture.TickAsync();
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(152, fixture.Packets.Count(packet => packet is ServerboundPunchPacket));
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task DefaultToolRulesUseTheLatestServerTagsWithoutRestartingTheAttack()
    {
        await using var fixture = new Fixture();
        fixture.State.RegistryTags.Install([]);
        fixture.Hold("diamond_pickaxe");
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            await fixture.TickAsync();
            Assert.False(mining.IsCompleted);
            fixture.State.RegistryTags.Install([new TagRegistry(RegistryIds.Block,
                [new TagEntry(Identifier.Minecraft("mineable/pickaxe"), [1])])]);
            for (int tick = 0; tick < 5; tick++)
                await fixture.TickAsync();
            Assert.DoesNotContain(fixture.Packets, packet => packet is ServerboundPlayerActionPacket { Action: 3 });
            await fixture.TickAsync();
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { 0, 3 }, fixture.Packets.OfType<ServerboundPlayerActionPacket>().Select(packet => packet.Action));
            Assert.Equal(8, fixture.Packets.Count(packet => packet is ServerboundPunchPacket));
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitToolRulesOverrideDefaultItemFactsOnTheActualTickPath(bool useTag)
    {
        await using var fixture = new Fixture();
        var blocks = new NbtList(useTag ? NbtTagType.String : NbtTagType.Int);
        var tag = Identifier.Parse("umpk_test:mineable");
        if (useTag)
        {
            blocks.Add(new NbtString(tag.ToString()));
            fixture.State.RegistryTags.Install([new TagRegistry(RegistryIds.Block, [new TagEntry(tag, [1])])]);
        }
        else
            blocks.Add(new NbtInt(1));
        var harvestRule = new NbtCompound();
        harvestRule.Put("blocks", blocks);
        harvestRule.PutBool("correct_for_drops", true);
        var speedRule = new NbtCompound();
        speedRule.Put("blocks", blocks);
        speedRule.PutFloat("speed", 16);
        speedRule.PutBool("correct_for_drops", false);
        fixture.Hold("diamond_pickaxe", new ToolComponent(1, 1, [harvestRule, speedRule]));
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            for (int tick = 0; tick < 3; tick++)
                await fixture.TickAsync();

            Assert.Single(fixture.Packets.OfType<ServerboundPlayerActionPacket>(), packet => packet.Action == 3);
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, fixture.Packets.Count(packet => packet is ServerboundPunchPacket));
        }
        finally
        {
            caller.Cancel();
            if (!mining.IsCompleted)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData("wooden_pickaxe", 23)]
    [InlineData("diamond_pickaxe", 6)]
    public async Task MeasuredToolsFinishOnTheirVanillaProgressTick(string item, int ticks)
    {
        await using var fixture = new Fixture();
        fixture.Hold(item);
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);

        for (int tick = 1; tick < ticks; tick++)
            await fixture.TickAsync();
        Assert.DoesNotContain(fixture.Packets, packet => packet is ServerboundPlayerActionPacket { Action: 3 });
        await fixture.TickAsync();
        await mining.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ticks + 1, fixture.Packets.Count(packet => packet is ServerboundPunchPacket));
        Assert.Equal(3, Assert.IsType<ServerboundPlayerActionPacket>(fixture.Packets[^2]).Action);
        Assert.IsType<ServerboundPunchPacket>(fixture.Packets[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationReleasesOnlyItsOwnLiveAttack(bool endSession)
    {
        await using var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        await fixture.TickAsync();

        if (endSession)
            fixture.Session.Cancel();
        else
            caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.DoesNotContain(fixture.Packets, packet => packet is ServerboundPlayerActionPacket { Action: 3 });
        ServerboundPlayerActionPacket[] aborts = fixture.Packets.OfType<ServerboundPlayerActionPacket>().Where(packet => packet.Action == 2).ToArray();
        if (endSession)
            Assert.Empty(aborts);
        else
        {
            ServerboundPlayerActionPacket abort = Assert.Single(aborts);
            Assert.Equal((byte)Direction.Down, abort.Direction);
            Assert.Equal(0, abort.Sequence);
        }
    }

    [Fact]
    public async Task ToolChangeAbortsAndRestartsWithoutRetainingThePreviousProgress()
    {
        await using var fixture = new Fixture();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        fixture.Hold("diamond_pickaxe");
        await fixture.TickAsync();

        Assert.Equal(new[] { 0, 2, 0 }, fixture.Packets.OfType<ServerboundPlayerActionPacket>().Select(packet => packet.Action));
        Assert.Equal(3, fixture.Packets.Count(packet => packet is ServerboundPunchPacket));
        for (int tick = 0; tick < 5; tick++)
            await fixture.TickAsync();
        Assert.False(mining.IsCompleted);
        await fixture.TickAsync();
        await mining.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 0, 2, 0, 3 }, fixture.Packets.OfType<ServerboundPlayerActionPacket>().Select(packet => packet.Action));
    }

    [Fact]
    public async Task ServerRemovalEndsTheAttackWithoutInventingStop()
    {
        await using var fixture = new Fixture();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target);
        await fixture.TickAsync();
        Assert.True(fixture.State.Registries!.Blocks.TryGetValue(Identifier.Minecraft("air"), out BlockDefinition? air));
        fixture.State.World.SetBlockStateId(Fixture.Target, air.DefaultStateId);
        await fixture.TickAsync();
        await mining.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, fixture.Packets.Count);
        Assert.DoesNotContain(fixture.Packets, packet => packet is ServerboundPlayerActionPacket { Action: 3 });
    }

    [Fact]
    public async Task WorldReplacementEndsTheOperationWithoutSendingIntoTheNewWorld()
    {
        await using var fixture = new Fixture();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target);
        await fixture.TickAsync();
        fixture.InstallWorld();
        await fixture.TickAsync();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, fixture.Packets.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayerReplacementEndsMiningWithoutSendingIntoTheRetainedWorld(bool started)
    {
        await using var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            if (started)
                await fixture.TickAsync();
            await fixture.EndPlayerLifetimeAsync();
            await fixture.TickAsync();

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(started ? 3 : 0, fixture.Packets.Count);
        }
        finally
        {
            caller.Cancel();
        }
    }

    [Fact]
    public async Task CancellationAfterPlayerReplacementRemainsCancellationWithoutSendingAStaleAbort()
    {
        await using var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        await fixture.TickAsync();
        await fixture.EndPlayerLifetimeAsync();
        caller.Cancel();

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(error.CancellationToken.IsCancellationRequested);
        Assert.Equal(3, fixture.Packets.Count);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public static readonly BlockPos Target = new(1, 64, 1);
        private readonly ChannelSessionScheduler _scheduler = new();
        public CancellationTokenSource Session { get; } = new();
        public ClientState State { get; } = new(new ClientFeatures().Normalized());
        public InteractionActions Actions { get; }
        public List<object> Packets { get; } = [];
        public Umpk.Game.World.BlockHitResult? ViewHit { get; set; } = Umpk.Game.World.BlockHitResult.FromHit(Target, Direction.North, Target.Center, 2);
        private readonly int _protocol;

        public Fixture(int protocol = 777)
        {
            _protocol = protocol;
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
            State.Registries = JavaGameData.Registries(protocol);
            State.RegistryTags.Install([new TagRegistry(RegistryIds.Block,
                [new TagEntry(Identifier.Minecraft("mineable/pickaxe"), [1])])]);
            InstallWorld();
            State.Self.Position = new Vec3d(1.5, 64, .5);
            var sequences = new SequenceTracker();
            var services = new ClientSessionServices
            {
                Version = version!,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = State,
                Wire = new WireIndex(version!),
                Logger = NullLogger.Instance,
                Scheduler = _scheduler,
                CurrentSessionCancellation = () => Session.Token,
                ReadBlockHit = _ => ViewHit,
            };
            Actions = new InteractionActions(new Sink(Packets, sequences), services, sequences);
        }

        public void InstallWorld()
        {
            var world = new Umpk.Game.World.World(
                WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, _protocol),
                new RegistryBlockDataSource(State.Registries!.Blocks, isLegacy: _protocol < 393), WorldFactory.EmptyBiomes());
            Assert.True(State.Registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
            world.SetBlockStateId(Target, stone.DefaultStateId);
            State.InstallWorld(world);
            State.EntityMembership.LoadColumn(ChunkPos.Containing(Target));
        }

        public void SetBlock(string name)
        {
            Assert.True(State.Registries!.Blocks.TryGetValue(Identifier.Minecraft(name), out BlockDefinition? block));
            State.World.SetBlockStateId(Target, block.DefaultStateId);
        }

        public void Hold(string name, ToolComponent? tool = null, bool removeTool = false)
        {
            Assert.True(State.Registries!.Items.TryGet(Identifier.Minecraft(name), out RegistryEntry<ItemDefinition> item));
            ItemStack stack = new(item, 1);
            if (tool is not null)
                stack = stack.With(DataComponents.Tool, tool);
            if (removeTool)
                stack = stack.WithComponents(stack.Components.Without(DataComponents.Tool));
            State.Inventory.SetSlot(0, 36, stack);
        }

        public Task EndPlayerLifetimeAsync() => _scheduler.InvokeAsync(() =>
        {
            State.AdvancePlayerLifetime();
            return ValueTask.CompletedTask;
        }, CancellationToken.None);

        public Task StopDestroyAsync() => _scheduler.InvokeAsync(() => Actions.StopDestroyBlockOnLoopAsync(Session.Token), CancellationToken.None);

        public Task TickAsync() => _scheduler.InvokeAsync(async () =>
        {
            State.AdvanceSessionTick();
            await Actions.TickMiningOnLoopAsync(Session.Token);
            Actions.TickAttackChargeOnLoop(playerTicking: true);
        }, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            Session.Cancel();
            await _scheduler.DisposeAsync();
            Session.Dispose();
        }
    }

    private sealed class Sink(List<object> packets, SequenceTracker sequences) : IPacketSink
    {
        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            packets.Add(packet);
            if (packet is ServerboundPlayerActionPacket { Sequence: int sequence })
                sequences.Acknowledge(sequence);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
            => throw new InvalidOperationException("Mining must use typed packets.");
    }
}
