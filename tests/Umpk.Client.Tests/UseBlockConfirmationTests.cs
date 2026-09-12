using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// <see cref="InteractionActions.UseBlockVerifiedAsync"/>: opening a door and then reading the door, rather than reporting the completed send as an opened door.
///
/// <para>This is <see cref="InteractionActions.DigBlockVerifiedAsync"/>'s reasoning on a different verb, and it applies for exactly the same reasons: a protected region, a spectator, adventure mode, a plugin that vetoes the interaction and a server that simply refuses all produce the same successful write, and each can return the block unchanged. The only honest report is the one taken from the world afterwards.</para>
///
/// <para>The witness is a SEPARATE position from the target on purpose. Pressing a button to open an iron door is one <c>use_item_on</c> against the button and one reading of the DOOR: The door sets <c>POWERED</c> and <c>OPEN</c> together from its neighbor signal, so the door tracks the signal exactly and the button's own state says nothing about whether the door moved.</para>
/// </summary>
public sealed class UseBlockConfirmationTests
{
    private static readonly BlockPos Door = new(10, 64, -3);

    /// <summary>Feet one block west of the door, so the eye is 1.62 up and about 1.5 away from it.</summary>
    private static readonly Vec3d StandingAt = new(9.5, 64.0, -2.5);

    /// <summary>Protocol 776, the current head: a flattened source, so <c>open</c> is readable.</summary>
    private static readonly JavaVersion Modern = Resolve(776);

    private static JavaVersion Resolve(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    [Fact]
    public void UseBlockOutcome_DefaultValueIsUnconfirmed()
        => Assert.Equal(UseBlockOutcome.Unconfirmed, default);

    [Fact]
    public async Task Use_ReportsUsed_WhenTheDoorReadsOpenAfterwards()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.OnUse = () => fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);

        UseBlockOutcome outcome = await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Equal(UseBlockOutcome.Used, outcome);
    }

    [Fact]
    public async Task Use_ReportsNotUsed_WhenTheServerResendsTheBlockStillClosed()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.OnUse = () => fixture.PublishBlockChanged(Door);

        UseBlockOutcome outcome = await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Equal(UseBlockOutcome.NotUsed, outcome);
    }

    [Fact]
    public async Task Use_ReportsUnconfirmed_WhenNothingComesBackInsideTheWindow()
    {
        Fixture fixture = Fixture.Build(Modern);

        UseBlockOutcome outcome = await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.Equal(UseBlockOutcome.Unconfirmed, outcome);
    }

    /// <summary>The button case: the press goes to one block and the answer is read off another. A witness that never moves is <see cref="UseBlockOutcome.Unconfirmed"/> however cheerfully the target itself changed.</summary>
    [Fact]
    public async Task Use_ReadsTheWitnessAndNotTheTarget()
    {
        Fixture fixture = Fixture.Build(Modern);
        var button = new BlockPos(Door.X - 1, Door.Y, Door.Z);
        fixture.OnUse = () => fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);

        UseBlockOutcome opened = await fixture.Actions.UseBlockVerifiedAsync(
            button, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(200));
        Assert.Equal(UseBlockOutcome.Used, opened);

        Fixture silent = Fixture.Build(Modern);
        UseBlockOutcome nothing = await silent.Actions.UseBlockVerifiedAsync(
            button, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150));
        Assert.Equal(UseBlockOutcome.Unconfirmed, nothing);
    }

    /// <summary>The subscription has to be armed before the send, for the reason <see cref="DigConfirmationTests"/> gives: on a fast local server the block update lands while the use is still awaiting its own acknowledgement.</summary>
    [Fact]
    public async Task Use_ArmsTheBlockChangedSubscriptionBeforeTheSend()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.OnBeforeSend = () => fixture.PublishBlockChanged(Door);

        UseBlockOutcome outcome = await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Equal(UseBlockOutcome.NotUsed, outcome);
    }

    /// <summary>Reach is <b>4.5 blocks, eye to the nearest point of the target's unit cube</b>, and it is checked BEFORE the send because a send from out of range is a packet the server discards and a wait this client then spends a whole window on.</summary>
    /// <remarks>One constant covers every era. From 1.20.5 onward block interaction range is 4.5, with the server checking eye-to-nearest-point squared against <c>(4.5 + 1.0)^2</c>; 1.20.4 and earlier check eye-to-centre under 36 and feet-to-centre under 64; 1.8 checks only feet-to-centre under 64. 4.5 to the nearest point implies at most about 5.4 to the centre, which clears all three.</remarks>
    [Fact]
    public async Task Use_RefusesOutOfReachBeforeItSends()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.SetPosition(new Vec3d(Door.X - 8.5, Door.Y, Door.Z + 0.5));

        UseBlockOutcome outcome = await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.Equal(UseBlockOutcome.OutOfReach, outcome);
        Assert.Empty(fixture.Sink.Packets);
    }

    /// <summary>A sneaking player with a held item uses the item on the block instead of activating the block. For a door, that means placing the held item. Sneaking with empty hands still opens the door, so the refusal is on the pair and not on the crouch.</summary>
    [Fact]
    public async Task Use_RefusesWhileSneakingWithAHeldItem()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.SetSneaking(true);
        fixture.GiveHeldItem();

        UseBlockOutcome outcome = await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.Equal(UseBlockOutcome.NotUsed, outcome);
        Assert.Empty(fixture.Sink.Packets);
    }

    [Fact]
    public async Task Use_WithoutAnEventBus_RefusesRatherThanGuessing()
    {
        Fixture fixture = Fixture.Build(Modern, withEvents: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Actions.UseBlockVerifiedAsync(
                Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>The wire shape, across the era split <c>InteractionActions</c> already routes on: 1.19+ carries <c>use_item_on</c> with an action sequence, and 1.8 has no <c>use_item_on</c> at all and spells the whole interaction as <c>block_place</c>.</summary>
    [Theory]
    [InlineData(763)]
    [InlineData(776)]
    public async Task Use_SendsOneUseItemOnWithTheBlockCentreCursor(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        Fixture fixture = Fixture.Build(version!);
        fixture.OnUse = () => fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);

        await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(200));

        object packet = Assert.Single(fixture.Sink.Packets);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packet);
        Assert.Equal(Door, use.Position);

        // abs(hit - centre) < 1.0000001 per axis is the ONLY geometric check the server makes on the hit vector, and the centre offset passes it on every face. Yaw and pitch are never consulted, which is why nothing here rotates the player.
        Assert.Equal(0.5f, use.CursorX);
        Assert.Equal(0.5f, use.CursorY);
        Assert.Equal(0.5f, use.CursorZ);
    }

    /// <summary>Protocol 47 has no <c>use_item_on</c> at all: the whole interaction travels on <c>block_place</c>, which <c>PlaceBlockAsync</c> already routes to. The outcome there is <see cref="UseBlockOutcome.Unconfirmed"/> and that is the honest answer rather than a gap: <c>BlockState.TryGetProperty</c> returns false for every name on a pre-flattening source, so no witness can be read and nothing can be confirmed. It is the same era boundary <c>MoveHelper.ClassifyBarrier</c> refuses to plan across, arrived at from the other side.</summary>
    [Fact]
    public async Task Use_OnProtocol47_SendsTheLegacyBlockPlaceAndCannotConfirm()
    {
        Fixture fixture = Fixture.Build(JavaVersions.V1_8);
        fixture.OnUse = () => fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);

        UseBlockOutcome outcome = await fixture.Actions.UseBlockVerifiedAsync(
            Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(fixture.Sink.Packets));
        Assert.Equal(UseBlockOutcome.Unconfirmed, outcome);
    }

    /// <summary><see cref="InteractionActions.ObserveBlockPropertyAsync"/> is the same wait with the send taken out, and the assertion that matters is <c>Sink.Packets</c> being empty. A pressure plate has no <c>use</c> handler at all - <c>BasePressurePlateBlock</c> fires from <c>entityInside</c> - so the body standing on it IS the interaction and there is nothing to send.</summary>
    /// <remarks>A test that asserted only the OUTCOME would stay green with the whole thing routed back through <see cref="InteractionActions.UseBlockVerifiedAsync"/>, because the witness is genuinely open by then and a press against a plate reports <see cref="UseBlockOutcome.Used"/> just as happily. The send is the only observable difference, so the send is what is asserted.</remarks>
    [Fact]
    public async Task Observe_ReportsUsedWithoutSendingAnything_WhenTheWitnessAlreadyReadsOpen()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);

        UseBlockOutcome outcome = await fixture.Actions.ObserveBlockPropertyAsync(
            Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(200));

        Assert.Equal(UseBlockOutcome.Used, outcome);
        Assert.Empty(fixture.Sink.Packets);
    }

    /// <summary>And it really waits, rather than answering off the first read: the door opens on another thread part-way through the window and the call still returns <see cref="UseBlockOutcome.Used"/>, with nothing sent.</summary>
    [Fact]
    public async Task Observe_WaitsForTheWitnessToMove_AndStillSendsNothing()
    {
        Fixture fixture = Fixture.Build(Modern);
        using var opened = new CancellationTokenSource();
        Task mover = Task.Run(
            async () =>
            {
                await Task.Delay(40, opened.Token);
                fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);
                fixture.PublishBlockChanged(Door);
            },
            opened.Token);

        UseBlockOutcome outcome = await fixture.Actions.ObserveBlockPropertyAsync(
            Door, "open", "true", confirmationWindow: TimeSpan.FromSeconds(2));
        await mover;

        Assert.Equal(UseBlockOutcome.Used, outcome);
        Assert.Empty(fixture.Sink.Packets);
    }

    /// <summary>Nothing happens inside the window: <see cref="UseBlockOutcome.Unconfirmed"/>, and still no send. Never <see cref="UseBlockOutcome.NotUsed"/>, because with no packet there is no refusal to observe - a server re-asserting the block says nothing about a signal that comes from a body's own position.</summary>
    [Fact]
    public async Task Observe_ReportsUnconfirmedRatherThanNotUsed_WhenTheWitnessNeverMoves()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.PublishBlockChanged(Door);

        UseBlockOutcome outcome = await fixture.Actions.ObserveBlockPropertyAsync(
            Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.Equal(UseBlockOutcome.Unconfirmed, outcome);
        Assert.Empty(fixture.Sink.Packets);
    }

    /// <summary><b>The sharp reason a plate must not go down the press path.</b> <see cref="Use_RefusesWhileSneakingWithAHeldItem"/> pins that a USE is refused outright when the body is sneaking with something in hand. Routed that way, a plate-driven door that was already open under the bot's feet would be scored a refusal and would spend an interaction budget. An observation has no packet to refuse, so it does not.</summary>
    [Fact]
    public async Task Observe_IsNotRefusedBySneakingWithAHeldItem_WhereAUseWouldBe()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.SetSneaking(true);
        fixture.GiveHeldItem();
        fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);

        Assert.Equal(
            UseBlockOutcome.NotUsed,
            await fixture.Actions.UseBlockVerifiedAsync(
                Door, Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150)));

        Assert.Equal(
            UseBlockOutcome.Used,
            await fixture.Actions.ObserveBlockPropertyAsync(
                Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150)));

        Assert.Empty(fixture.Sink.Packets);
    }

    /// <summary>Reach is a property of a packet, and there is no packet. A body that cannot reach the plate is a body that is not standing on it, which is a planning error rather than a send this can decline - so unlike <see cref="Use_RefusesOutOfReachBeforeItSends"/> there is nothing here to refuse.</summary>
    [Fact]
    public async Task Observe_HasNoReachCheck_BecauseItHasNoPacket()
    {
        Fixture fixture = Fixture.Build(Modern);
        fixture.SetPosition(new Vec3d(Door.X - 8.5, Door.Y, Door.Z + 0.5));
        fixture.World.SetBlockStateId(Door, fixture.OpenDoorStateId);

        UseBlockOutcome outcome = await fixture.Actions.ObserveBlockPropertyAsync(
            Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150));

        Assert.Equal(UseBlockOutcome.Used, outcome);
        Assert.Empty(fixture.Sink.Packets);
    }

    [Fact]
    public async Task Observe_WithoutAnEventBus_RefusesRatherThanGuessing()
    {
        Fixture fixture = Fixture.Build(Modern, withEvents: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Actions.ObserveBlockPropertyAsync(
                Door, "open", "true", confirmationWindow: TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>The same hand-built harness <see cref="DigConfirmationTests"/> uses, with a real oak door: the closed and open state ids come off the generated dataset by reading the <c>open</c> property of every state in the block's range, so the fixture cannot disagree with the table the client reads.</summary>
    private sealed class Fixture
    {
        private readonly EventBus? _events;
        private readonly ClientState _state;

        private readonly int _protocol;

        private Fixture(ClientState state, EventBus? events, int protocol, int closed, int open)
        {
            _state = state;
            _events = events;
            _protocol = protocol;
            ClosedDoorStateId = closed;
            OpenDoorStateId = open;
        }

        public InteractionActions Actions { get; private set; } = null!;

        public FakeSink Sink { get; private set; } = null!;

        public int ClosedDoorStateId { get; }

        public int OpenDoorStateId { get; }

        public Umpk.Game.World.World World => _state.World;

        /// <summary>Invoked from inside the fake sink's SendAsync, after the packet is recorded.</summary>
        public Action? OnUse { get; set; }

        /// <summary>Invoked from inside the fake sink's SendAsync, before anything else.</summary>
        public Action? OnBeforeSend { get; set; }

        public void SetPosition(Vec3d position) => _state.Self.Position = position;

        public void SetSneaking(bool sneaking) => _state.Self.Sneaking = sneaking;

        /// <summary>Puts a real stack in the selected hotbar slot. Slot 36 is hotbar index 0 in the player menu's own index space (<c>PlayerInventorySlotMap</c>), which is what <c>InteractionActions.HeldItem</c> resolves through.</summary>
        public void GiveHeldItem()
        {
            _state.Self.HeldSlot = 0;
            Registry<ItemDefinition> items = JavaGameData.Registries(_protocol).Items;
            Assert.True(items.TryGet(Identifier.Minecraft("stone"), out RegistryEntry<ItemDefinition> stone));
            _state.Inventory.SetSlot(0, 36, new ItemStack(stone, 1));
        }

        public void PublishBlockChanged(BlockPos position)
            => _ = (_events ?? throw new InvalidOperationException("No event bus wired.")).PublishAsync(
                new BlockChanged(position, 0));

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

            // 1.8's registry spells the block `wooden_door`; the flattening renamed it `oak_door`.
            if (!blocks.TryGetValue(Identifier.Minecraft("oak_door"), out BlockDefinition? door))
                Assert.True(blocks.TryGetValue(Identifier.Minecraft("wooden_door"), out door));

            (int closed, int open) = FindOpenAndClosed(data, door!);
            world.SetBlockStateId(Door, closed);
            state.InstallWorld(world);
            state.Self.Position = StandingAt;

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

            var fixture = new Fixture(state, events, protocol, closed, open);
            var sink = new FakeSink(sequences, fixture);
            fixture.Sink = sink;
            fixture.Actions = new InteractionActions(sink, services, sequences);
            return fixture;
        }

        /// <summary>The first state in the block's range whose <c>open</c> is false, and the first whose <c>open</c> is true, read through the same <see cref="IBlockDataSource"/> the client reads. On a pre-flattening source neither resolves and the door's default state stands in for both, which is the honest answer there: <c>open</c> is not readable at all on 47-340.</summary>
        private static (int Closed, int Open) FindOpenAndClosed(IBlockDataSource data, BlockDefinition door)
        {
            int closed = door.DefaultStateId;
            int open = door.DefaultStateId;
            for (int id = door.MinStateId; id <= door.MaxStateId; id++)
            {
                if (!data.TryGetPropertyValue(id, "open", out string value))
                    continue;

                if (value == "true")
                    open = id;

                else
                    closed = id;

            }

            return (closed, open);
        }

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
                _fixture.OnBeforeSend?.Invoke();
                Packets.Add(packet);
                if (packet is ServerboundUseItemOnPacket use)
                    _sequences.Acknowledge(use.Sequence);

                _fixture.OnUse?.Invoke();
                return ValueTask.CompletedTask;
            }

            public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
                => ValueTask.CompletedTask;
        }
    }
}
