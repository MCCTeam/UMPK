using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="SessionActions.RespawnAsync"/>: a respawn request answers what the server is known to have done, not only that the packet left. It captures health and subscribes before sending, then returns one of three outcomes.</summary>
public sealed class RespawnOutcomeTests
{
    [Fact]
    public void DefaultValueIsUnconfirmed()
        => Assert.Equal(RespawnOutcome.Unconfirmed, default);

    [Fact]
    public async Task Confirmed_WhenTheClientboundRespawnArrives()
    {
        Fixture fixture = Fixture.Build(healthAtRequest: 0f);

        // Published from a background continuation, not from inside the sink: this is the general "the server answered, eventually" path, distinct from the ordering-sensitive test below.
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            fixture.PublishRespawned();
        });

        RespawnOutcome outcome = await fixture.Actions.RespawnAsync();

        Assert.Equal(RespawnOutcome.Confirmed, outcome);
    }

    /// <summary>The fake sink publishes <see cref="Respawned"/> from INSIDE the single <c>client_command</c> packet's <c>SendAsync</c>. A subscription armed only after the send returns would miss an event that fires this early, and no health value can rescue a miss here: the outcome would fall through to the health-based classification instead of the observed Confirmed.</summary>
    [Fact]
    public async Task ArmsTheSubscriptionBeforeTheSend()
    {
        Fixture fixture = Fixture.Build(healthAtRequest: 20f);
        fixture.OnSend = () => fixture.PublishRespawned();

        RespawnOutcome outcome = await fixture.Actions.RespawnAsync();

        Assert.Equal(RespawnOutcome.Confirmed, outcome);
    }

    /// <summary>A server ignores an ordinary respawn request while the player is alive and sends no response. No event arrives, so this genuinely waits out <see cref="SessionActions.DefaultRespawnConfirmationWindow"/>.</summary>
    [Fact]
    public async Task NotDead_WhenHealthIsPositiveAtRequest()
    {
        Fixture fixture = Fixture.Build(healthAtRequest: 20f);

        RespawnOutcome outcome = await fixture.Actions.RespawnAsync();

        Assert.Equal(RespawnOutcome.NotDead, outcome);
    }

    /// <summary>A dead player with no answer at all is not told it worked.</summary>
    [Fact]
    public async Task Unconfirmed_ForADeadPlayerWithNoAnswer()
    {
        Fixture fixture = Fixture.Build(healthAtRequest: 0f);

        RespawnOutcome outcome = await fixture.Actions.RespawnAsync();

        Assert.Equal(RespawnOutcome.Unconfirmed, outcome);
    }

    /// <summary>The fake sink mutates <c>Self.Health</c> to 20 from INSIDE <c>SendAsync</c>, simulating the server having already healed the player by the time a (wrong) read-after would look. Health starts at 0 (dead) and no <see cref="Respawned"/> ever arrives, so a correct implementation that captured health BEFORE the send reports <see cref="RespawnOutcome.Unconfirmed"/>; an implementation that read health AFTER the send would see 20 and wrongly report <see cref="RespawnOutcome.NotDead"/>.</summary>
    [Fact]
    public async Task ReadsHealthBeforeTheSend_NotAfter()
    {
        Fixture fixture = Fixture.Build(healthAtRequest: 0f);
        fixture.OnSend = () => fixture.State.Self.Health = 20f;

        RespawnOutcome outcome = await fixture.Actions.RespawnAsync();

        Assert.Equal(RespawnOutcome.Unconfirmed, outcome);
    }

    /// <summary>The wire behaviour must not depend on the confirmation outcome: the request is always sent.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(776)]
    public async Task StillSendsPerformRespawn(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        Fixture fixture = Fixture.Build(healthAtRequest: 0f, version: version!);
        fixture.OnSend = () => fixture.PublishRespawned();

        await fixture.Actions.RespawnAsync();

        var sent = Assert.IsType<ServerboundClientCommandPacket>(Assert.Single(fixture.Sink.Packets));
        Assert.Equal(ClientCommandAction.PerformRespawn, sent.Action);
    }

    /// <summary>A hand-built harness (no live <c>UmpkClient</c>) plus a fake sink that can react to the single <c>client_command</c> packet as it is sent, so the arm-before-send ordering and the health-before-send capture are both observable without a live server.</summary>
    private sealed class Fixture
    {
        private readonly EventBus _events;

        private Fixture(ClientState state, EventBus events)
        {
            State = state;
            _events = events;
        }

        public SessionActions Actions { get; private set; } = null!;

        public ClientState State { get; }

        public FakeSink Sink { get; private set; } = null!;

        /// <summary>Invoked from inside the fake sink's SendAsync for the client_command packet.</summary>
        public Action? OnSend { get; set; }

        public void PublishRespawned()
        {
            // The only subscriber RespawnAsync installs is synchronous, so PublishAsync completes synchronously here; discarding (rather than awaiting) matches UmpkClient's own lag-publish seam for the same reason.
            _ = _events.PublishAsync(new Respawned());
        }

        public static Fixture Build(float healthAtRequest, JavaVersion? version = null)
        {
            JavaVersion effectiveVersion = version ?? JavaVersions.V1_8;
            var state = new ClientState(new ClientFeatures().Normalized());
            state.Self.Health = healthAtRequest;

            var events = new EventBus(NullLogger.Instance, TimeSpan.Zero, 256);
            var services = new ClientSessionServices
            {
                Version = effectiveVersion,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = state,
                Wire = new WireIndex(effectiveVersion),
                Logger = NullLogger.Instance,
                Scheduler = new ChannelSessionScheduler(),
                Events = events,
            };

            var fixture = new Fixture(state, events);
            var sink = new FakeSink(fixture);
            fixture.Sink = sink;
            fixture.Actions = new SessionActions(sink, services);
            return fixture;
        }

        /// <summary>Records every packet and fires the fixture's OnSend hook so a test can react to the send as it happens.</summary>
        public sealed class FakeSink : IPacketSink
        {
            private readonly Fixture _fixture;

            public FakeSink(Fixture fixture) => _fixture = fixture;

            public List<object> Packets { get; } = [];

            public ValueTask SendAsync(object packet, CancellationToken ct)
            {
                Packets.Add(packet);
                _fixture.OnSend?.Invoke();
                return ValueTask.CompletedTask;
            }

            public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
                => ValueTask.CompletedTask;
        }
    }
}
