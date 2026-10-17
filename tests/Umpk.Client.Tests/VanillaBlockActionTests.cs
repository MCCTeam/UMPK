using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Data.Java;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaBlockActionTests
{
    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(777)]
    public async Task SurvivalStone_DoesNotFinishBeforeTheSessionHasMinedIt(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        var state = new ClientState(new ClientFeatures().Normalized());
        var target = new BlockPos(1, 64, 1);
        InstallStoneWorld(state, protocol, target);
        var sequences = new SequenceTracker();
        var sink = new AcknowledgingSink(sequences);
        await using var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };
        var actions = new InteractionActions(sink, services, sequences);
        using var cancellation = new CancellationTokenSource();

        Task mining = actions.DigBlockAsync(target, Direction.South, cancellation.Token);

        Assert.DoesNotContain(sink.Packets, packet => packet is ServerboundPlayerActionPacket action && action.Action == (protocol == 777 ? 3 : 2));
        Assert.False(mining.IsCompleted);
        await TickAsync(scheduler, actions, state);
        Assert.Equal(3, sink.Packets.Count);
        Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(sink.Packets[protocol == 47 ? 1 : 0]).Action);
        for (int tick = 2; tick <= 150; tick++)
            await TickAsync(scheduler, actions, state);
        Assert.DoesNotContain(sink.Packets, packet => packet is ServerboundPlayerActionPacket action && action.Action == (protocol == 777 ? 3 : 2));
        Assert.False(mining.IsCompleted);

        await TickAsync(scheduler, actions, state);
        await mining.WaitAsync(TimeSpan.FromSeconds(5));

        var stop = Assert.IsType<ServerboundPlayerActionPacket>(sink.Packets[^2]);
        Assert.Equal(protocol == 777 ? 3 : 2, stop.Action);
        Assert.Equal((byte)Direction.South, stop.Direction);
        Assert.Equal(152, sink.Packets.Count(packet => packet is ServerboundSwingPacket or ServerboundPunchPacket));
        Assert.False(state.World.GetBlock(target).IsAir);
    }

    [Fact]
    public async Task CreativeDig_On26_3_SendsStartAndPunchWithoutStop()
    {
        Assert.True(JavaVersions.TryGetByProtocol(777, out var version));
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.GameMode = GameMode.Creative;
        state.Self.InstantBuild = true;
        InstallStoneWorld(state, 777, new BlockPos(1, 64, 1));
        var sequences = new SequenceTracker();
        var sink = new AcknowledgingSink(sequences);
        await using var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };
        var actions = new InteractionActions(sink, services, sequences);

        Task mining = actions.DigBlockAsync(new BlockPos(1, 64, 1));
        await TickAsync(scheduler, actions, state);
        await mining.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Collection(sink.Packets,
            packet => Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(packet).Action),
            packet => Assert.IsType<ServerboundPunchPacket>(packet));
    }

    [Theory]
    [InlineData(47, GameMode.Creative, false, true)]
    [InlineData(107, GameMode.Creative, false, false)]
    [InlineData(769, GameMode.Creative, false, false)]
    [InlineData(770, GameMode.Survival, true, false)]
    public async Task InstantDig_UsesVanillasEraSpecificCriterionAndInitialSwingOrder(
        int protocol, GameMode mode, bool instantBuild, bool swingBeforeStart)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.GameMode = mode;
        state.Self.InstantBuild = instantBuild;
        InstallStoneWorld(state, protocol, new BlockPos(1, 64, 1));
        var sequences = new SequenceTracker();
        var sink = new AcknowledgingSink(sequences);
        await using var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };
        var actions = new InteractionActions(sink, services, sequences);

        Task mining = actions.DigBlockAsync(new BlockPos(1, 64, 1));
        await TickAsync(scheduler, actions, state);
        await mining.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, sink.Packets.Count);
        var start = Assert.IsType<ServerboundPlayerActionPacket>(sink.Packets[swingBeforeStart ? 1 : 0]);
        Assert.Equal(0, start.Action);
        Assert.IsType<ServerboundSwingPacket>(sink.Packets[swingBeforeStart ? 0 : 1]);
    }

    private static void InstallStoneWorld(ClientState state, int protocol, BlockPos target)
    {
        RegistryAccess registries = JavaGameData.Registries(protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
            new RegistryBlockDataSource(registries.Blocks, isLegacy: protocol < 393), WorldFactory.EmptyBiomes());
        Assert.True(registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        world.SetBlockStateId(target, stone.DefaultStateId);
        state.Registries = registries;
        state.InstallWorld(world);
    }

    private static Task TickAsync(ChannelSessionScheduler scheduler, InteractionActions actions, ClientState state)
        => scheduler.InvokeAsync(async () =>
        {
            state.AdvanceSessionTick();
            await actions.TickMiningOnLoopAsync(CancellationToken.None);
        }, CancellationToken.None);

    private sealed class AcknowledgingSink(SequenceTracker sequences) : IPacketSink
    {
        public List<object> Packets { get; } = [];

        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Packets.Add(packet);
            if (packet is ServerboundPlayerActionPacket { Sequence: int sequence })
                sequences.Acknowledge(sequence);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
            => throw new InvalidOperationException("This action must send typed packets.");
    }
}
