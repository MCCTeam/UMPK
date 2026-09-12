using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="InteractionActions.DigBlockVerifiedAsync"/> reports what the server is known to have done, not only that the packets left. It subscribes before sending and returns one of three outcomes.</summary>
public sealed class DigConfirmationTests
{
    private static readonly BlockPos Target = new(10, 64, -3);

    [Fact]
    public void DigOutcome_DefaultValueIsUnconfirmed()
        => Assert.Equal(DigOutcome.Unconfirmed, default);

    [Fact]
    public async Task Dig_ReportsBroken_WhenThePositionReadsAirAfterwards()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8);
        fixture.OnFinish = () => fixture.World.SetBlockStateId(Target, fixture.AirStateId);

        DigOutcome outcome = await fixture.Actions.DigBlockVerifiedAsync(
            Target, confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Equal(DigOutcome.Broken, outcome);
    }

    [Fact]
    public async Task Dig_ReportsNotBroken_WhenTheServerResendsTheSamePositionAndTheBlockStands()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8);
        fixture.OnFinish = () => fixture.PublishBlockChanged(Target);

        DigOutcome outcome = await fixture.Actions.DigBlockVerifiedAsync(
            Target, confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Equal(DigOutcome.NotBroken, outcome);
    }

    [Fact]
    public async Task Dig_ReportsUnconfirmed_WhenNothingComesBackInsideTheWindow()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8);

        DigOutcome outcome = await fixture.Actions.DigBlockVerifiedAsync(
            Target, confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.Equal(DigOutcome.Unconfirmed, outcome);
    }

    [Fact]
    public async Task Dig_ReportsUnconfirmed_WhenTheColumnUnloadsUnderIt()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8);
        fixture.OnFinish = () => fixture.World.UnloadColumn(ChunkPos.Containing(Target));

        DigOutcome outcome = await fixture.Actions.DigBlockVerifiedAsync(
            Target, confirmationWindow: TimeSpan.FromMilliseconds(150));

        // Not loaded must NOT read as air: the column being gone is a "do not know", not a "it broke".
        Assert.Equal(DigOutcome.Unconfirmed, outcome);
    }

    /// <summary>The fake sink publishes <see cref="BlockChanged"/> from INSIDE the very first packet's <c>SendAsync</c> (the start-digging action, sent before the swing or the finish). A subscription armed only after <c>DigBlockAsync</c> returns would miss an event that fires this early, and the block never actually turns to air in this fixture, so a broken implementation reports <see cref="DigOutcome.Unconfirmed"/> here instead of <see cref="DigOutcome.NotBroken"/>.</summary>
    [Fact]
    public async Task Dig_ArmsTheBlockChangedSubscriptionBeforeTheSend()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8);
        fixture.OnStart = () => fixture.PublishBlockChanged(Target);

        DigOutcome outcome = await fixture.Actions.DigBlockVerifiedAsync(
            Target, confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Equal(DigOutcome.NotBroken, outcome);
    }

    [Fact]
    public async Task Dig_IgnoresABlockChangedAtADifferentPosition()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8);
        var elsewhere = new BlockPos(Target.X + 5, Target.Y, Target.Z);
        fixture.OnFinish = () => fixture.PublishBlockChanged(elsewhere);

        DigOutcome outcome = await fixture.Actions.DigBlockVerifiedAsync(
            Target, confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.Equal(DigOutcome.Unconfirmed, outcome);
    }

    [Fact]
    public async Task Dig_WithoutAnEventBus_RefusesRatherThanGuessing()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8, withEvents: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Actions.DigBlockVerifiedAsync(Target, confirmationWindow: TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>The wire behaviour of a verified dig must be identical to the unverified one: start, swing, finish. Protocol 47 (no action sequence), 763 (post-1.19 sequence, pre-configuration-reentry) and 776 (the current head) cover the era split in <c>InteractionActions.NextSequence</c>/<c>HasSequences</c>.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(763)]
    [InlineData(776)]
    public async Task Dig_StillSendsTheStartSwingFinishTriple(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        Fixture fixture = Fixture.Build(version!);
        fixture.OnFinish = () => fixture.World.SetBlockStateId(Target, fixture.AirStateId);

        await fixture.Actions.DigBlockVerifiedAsync(Target, confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Collection(
            fixture.Sink.Packets,
            p => Assert.Equal(0, Assert.IsType<ServerboundPlayerActionPacket>(p).Action),
            p => Assert.IsType<ServerboundSwingPacket>(p),
            p => Assert.Equal(2, Assert.IsType<ServerboundPlayerActionPacket>(p).Action));
    }

    /// <summary>A hand-built harness (no live <c>UmpkClient</c>, so <see cref="ClientSessionServices.Events"/> stays whatever the test wires) plus a fake sink that can react to a packet as it is sent, so the arm-before-send ordering and the "the server resent this exact position" path are both observable without a live server.</summary>
    private sealed class Fixture
    {
        private readonly EventBus? _events;

        private Fixture(ClientState state, EventBus? events, int airStateId)
        {
            State = state;
            _events = events;
            AirStateId = airStateId;
        }

        public InteractionActions Actions { get; private set; } = null!;

        public ClientState State { get; }

        public FakeSink Sink { get; private set; } = null!;

        public int AirStateId { get; }

        public Umpk.Game.World.World World => State.World;

        /// <summary>Invoked from inside the fake sink's SendAsync for the start-digging packet (action 0).</summary>
        public Action? OnStart { get; set; }

        /// <summary>Invoked from inside the fake sink's SendAsync for the finish-digging packet (action 2).</summary>
        public Action? OnFinish { get; set; }

        public void PublishBlockChanged(BlockPos position)
        {
            // The only subscriber DigBlockVerifiedAsync installs is synchronous, so PublishAsync completes synchronously here; discarding (rather than awaiting) matches UmpkClient's own lag-publish seam for the same reason.
            _ = (_events ?? throw new InvalidOperationException("No event bus wired.")).PublishAsync(
                new BlockChanged(position, 0));
        }

        public static Fixture Build(JavaVersion version, bool withEvents = true)
        {
            var state = new ClientState(new ClientFeatures().Normalized());
            int protocol = version.Version.Protocol;
            Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
            var data = new RegistryBlockDataSource(blocks, isLegacy: protocol < 393);
            var world = new Umpk.Game.World.World(
                WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
                data,
                WorldFactory.EmptyBiomes());

            Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
            Assert.True(blocks.TryGetValue(Identifier.Minecraft("air"), out BlockDefinition? air));
            world.SetBlockStateId(Target, stone.DefaultStateId);
            state.InstallWorld(world);

            EventBus? events = withEvents ? new EventBus(NullLogger.Instance, TimeSpan.Zero, 256) : null;
            var sequences = new SequenceTracker();
            var services = new ClientSessionServices
            {
                Version = version,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = state,
                Wire = new WireIndex(version),
                Logger = NullLogger.Instance,
                Scheduler = new ChannelSessionScheduler(),
                Events = events,
            };

            var fixture = new Fixture(state, events, air.DefaultStateId);
            var sink = new FakeSink(sequences, fixture);
            fixture.Sink = sink;
            fixture.Actions = new InteractionActions(sink, services, sequences);
            return fixture;
        }

        /// <summary>Records every packet, auto-acknowledges the block-action sequence, and fires the fixture's start/finish hooks so a test can react to a specific packet as it is sent.</summary>
        public sealed class FakeSink : IPacketSink
        {
            private readonly SequenceTracker _sequences;
            private readonly Fixture _fixture;

            public FakeSink(SequenceTracker sequences, Fixture fixture)
            {
                _sequences = sequences;
                _fixture = fixture;
            }

            public List<object> Packets { get; } = [];

            public ValueTask SendAsync(object packet, CancellationToken ct)
            {
                Packets.Add(packet);
                if (packet is ServerboundPlayerActionPacket action)
                {
                    if (action.Sequence is int seq)
                        _sequences.Acknowledge(seq);

                    if (action.Action == 0)
                        _fixture.OnStart?.Invoke();

                    else if (action.Action == 2)
                        _fixture.OnFinish?.Invoke();

                }

                return ValueTask.CompletedTask;
            }

            public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
                => ValueTask.CompletedTask;
        }
    }
}
