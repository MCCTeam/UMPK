using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class CreativeMiningRestrictionsRuntimeTests
{
    private static readonly Guid SelfId = Guid.Parse("17f0126e-d266-4b18-9a25-719983c94aae");

    public static IEnumerable<object[]> RestrictedStacks()
    {
        foreach (int protocol in JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Order())
        {
            yield return [protocol, "diamond_sword"];
            if (protocol >= 393)
            {
                yield return [protocol, "trident"];
                yield return [protocol, "debug_stick"];
            }
            if (protocol >= 766)
                yield return [protocol, "mace"];
        }
    }

    [Theory]
    [MemberData(nameof(RestrictedStacks))]
    public async Task RestrictedCreativeItemStillSendsStartAndTheSamePhaseContinuation(int protocol, string name)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        if (protocol == 766 && name == "mace")
            await fixture.FeaturesAsync(["vanilla", "update_1_21"]);
        await fixture.HoldAsync(name, 0);
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);

        await fixture.TickAsync();
        await mining.WaitAsync(TimeSpan.FromSeconds(5));

        AssertGroup(fixture, protocol, 2);
        Assert.False(fixture.State.World.GetBlock(Fixture.Target).IsAir);
        int sent = fixture.Sent.Count;
        await fixture.TickAsync();
        Assert.Equal(sent, fixture.Sent.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledExperimentalItemKeepsTheNativeHeldOnlyPacketGroup(bool receiveFeaturePacket)
    {
        await using var fixture = new Fixture(766);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, 766, GameMode.Creative, true);
        await InfoAsync(fixture, 766, GameMode.Creative);
        if (receiveFeaturePacket)
            await fixture.FeaturesAsync(["vanilla"]);
        await fixture.HoldAsync("mace", 0);
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await mining.WaitAsync(TimeSpan.FromSeconds(5));
        AssertGroup(fixture, 766, 1);
        int count = fixture.Sent.Count;
        await fixture.TickAsync();
        Assert.Equal(count, fixture.Sent.Count);
    }

    public static IEnumerable<object[]> ToolPatches()
    {
        foreach (int protocol in new[] { 766, 769, 770, 777 })
            foreach (string name in new[] { "stone", "diamond_sword", "debug_stick" })
                foreach (int patch in new[] { -1, 0, 1 })
                    foreach (bool creative in new[] { false, true })
                        foreach (bool instantBuild in new[] { false, true })
                            yield return [protocol, name, patch, creative, instantBuild];
    }

    [Theory]
    [MemberData(nameof(ToolPatches))]
    public async Task EffectiveToolOnlyControlsCreativeRemovalFromItsNativeBoundary(int protocol, string name, int patch, bool creative, bool instantBuild)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, creative ? GameMode.Creative : GameMode.Survival, instantBuild);
        await InfoAsync(fixture, protocol, creative ? GameMode.Creative : GameMode.Survival);
        await fixture.HoldAsync(name, 0);
        ItemStack held = fixture.State.Inventory.PlayerSlots[36];
        held = patch == -1
            ? held.WithComponents(held.Components.Without(DataComponents.Tool))
            : held.With(DataComponents.Tool, new ToolComponent(1, 1, []) { CanDestroyBlocksInCreative = patch == 1 });
        await fixture.SetSlotAsync(36, held);
        ItemStack observed = fixture.State.Inventory.PlayerSlots[36];
        Assert.Equal(patch != -1, observed.Components.Has(DataComponents.Tool));
        if (patch != -1)
            Assert.Equal(protocol < 770 || patch == 1, observed.Components.Get(DataComponents.Tool)!.CanDestroyBlocksInCreative);
        using var caller = new CancellationTokenSource();
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        bool instant = protocol < 770 ? creative : instantBuild;
        bool restricted = name == "debug_stick" || (protocol < 770 ? name == "diamond_sword" && creative : patch == 0 && instantBuild);
        try
        {
            await fixture.TickAsync();
            AssertGroup(fixture, protocol, instant && !restricted ? 1 : 2);
            if (instant)
                await mining.WaitAsync(TimeSpan.FromSeconds(5));
            else
                Assert.False(mining.IsCompleted);
        }
        finally
        {
            await ReleaseAsync(caller, mining);
        }
    }

    [Theory]
    [InlineData(393, false, false)]
    [InlineData(393, true, false)]
    [InlineData(393, true, true)]
    [InlineData(760, false, false)]
    [InlineData(760, true, false)]
    [InlineData(760, true, true)]
    [InlineData(761, false, false)]
    [InlineData(761, true, false)]
    [InlineData(761, true, true)]
    [InlineData(766, false, false)]
    [InlineData(766, true, false)]
    [InlineData(766, true, true)]
    [InlineData(769, false, false)]
    [InlineData(769, true, false)]
    [InlineData(769, true, true)]
    public async Task ItemCreativeCheckUsesOwnPlayerInfoRatherThanTheControllerMode(int protocol, bool present, bool infoCreative)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        if (present)
            await InfoAsync(fixture, protocol, infoCreative ? GameMode.Creative : GameMode.Survival);
        await fixture.HoldAsync("diamond_sword", 0);
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await mining.WaitAsync(TimeSpan.FromSeconds(5));
        AssertGroup(fixture, protocol, present && infoCreative ? 2 : 1);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(393)]
    [InlineData(758)]
    [InlineData(766)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task FailedLocalRemovalRetainsFourDelayTicksForTheNextExplicitRequest(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        await fixture.HoldAsync("diamond_sword", 0);
        Task first = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await ModeAsync(fixture, protocol, GameMode.Survival, false);
        await InfoAsync(fixture, protocol, GameMode.Survival);
        await fixture.HoldAsync("diamond_pickaxe", 0);
        using var caller = new CancellationTokenSource();
        Task next = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            for (int tick = 1; tick < 10; tick++)
                await fixture.TickAsync();
            Assert.DoesNotContain(fixture.Sent.OfType<ServerboundPlayerActionPacket>(), action => action.Action == (protocol == 777 ? 3 : 2));
            await fixture.TickAsync();
            Assert.Equal(new[] { 0, 0, protocol == 777 ? 3 : 2 }, fixture.Sent.OfType<ServerboundPlayerActionPacket>().Select(action => action.Action));
            await next.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await ReleaseAsync(caller, next);
        }
    }

    [Theory]
    [InlineData(760, false)]
    [InlineData(761, true)]
    [InlineData(766, true)]
    [InlineData(769, true)]
    public async Task OwnPlayerInfoUsesTheNativeLookupVersusCachedObjectBoundary(int protocol, bool cached)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        await fixture.HoldAsync("diamond_sword", 0);
        Task first = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Sent.Clear();
        if (protocol < 761)
            await ApplyAsync(fixture, protocol, "player_info", new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.RemovePlayer, [new LegacyPlayerListEntry(SelfId, null, null, 0, 0, null)]));
        else
            await ApplyAsync(fixture, protocol, "player_info_remove", new ClientboundPlayerInfoRemovePacket([SelfId]));
        await InfoAsync(fixture, protocol, GameMode.Survival);
        Task second = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        AssertGroup(fixture, protocol, cached ? 2 : 1);
        fixture.Sent.Clear();
        fixture.State.AdvancePlayerLifetime();
        Task replacement = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await replacement.WaitAsync(TimeSpan.FromSeconds(5));
        AssertGroup(fixture, protocol, 1);
    }

    [Theory]
    [InlineData(760, false)]
    [InlineData(761, true)]
    [InlineData(766, true)]
    [InlineData(769, true)]
    public async Task CompletedSurvivalAttemptPrimesTheSameNativePlayerInfoCache(int protocol, bool cached)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Survival, false);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        await fixture.HoldAsync("diamond_sword", 0);
        using var caller = new CancellationTokenSource();
        Task first = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        try
        {
            for (int tick = 0; tick < 600 && !fixture.Sent.OfType<ServerboundPlayerActionPacket>().Any(action => action.Action == 2); tick++)
                await fixture.TickAsync();
            Assert.Single(fixture.Sent.OfType<ServerboundPlayerActionPacket>(), action => action.Action == 2);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await ReleaseAsync(caller, first);
        }
        fixture.Sent.Clear();
        if (protocol < 761)
            await ApplyAsync(fixture, protocol, "player_info", new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.RemovePlayer, [new LegacyPlayerListEntry(SelfId, null, null, 0, 0, null)]));
        else
            await ApplyAsync(fixture, protocol, "player_info_remove", new ClientboundPlayerInfoRemovePacket([SelfId]));
        await InfoAsync(fixture, protocol, GameMode.Survival);
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        Task second = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        AssertGroup(fixture, protocol, cached ? 2 : 1);
    }

    [Theory]
    [InlineData(393)]
    [InlineData(761)]
    [InlineData(766)]
    [InlineData(769)]
    public async Task UnknownLocalIdentityDoesNotInventACreativePlayerInfoResult(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        await fixture.HoldAsync("diamond_sword", 0);
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        await fixture.TickAsync();
        await mining.WaitAsync(TimeSpan.FromSeconds(5));
        AssertGroup(fixture, protocol, 1);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(393)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task CallerCancellationAfterStartFinishesTheCapturedNativePacketGroup(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        await fixture.HoldAsync("diamond_sword", 0);
        using var caller = new CancellationTokenSource();
        fixture.CommittedStartBarrier = fixture.CancellationCleanupQueued.Task;
        fixture.PacketCommitted = packet =>
        {
            if (packet is ServerboundPlayerActionPacket { Action: 0 })
                caller.Cancel();
        };
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North, caller.Token);
        await fixture.TickAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        AssertGroup(fixture, protocol, 2);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(393)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task SessionReplacementAfterStartDoesNotWriteOldAnimationsToTheNewSession(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        await fixture.HoldAsync("diamond_sword", 0);
        fixture.PacketCommitted = packet =>
        {
            if (packet is ServerboundPlayerActionPacket { Action: 0 })
                fixture.ReplaceSession();
        };
        Task mining = fixture.Actions.DigBlockAsync(Fixture.Target, Direction.North);
        Exception? tickError = await Record.ExceptionAsync(() => fixture.TickAsync());
        Assert.True(tickError is null or OperationCanceledException);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(protocol == 47 ? 2 : 1, fixture.Sent.Count);
        await fixture.TickAsync();
        Assert.Equal(protocol == 47 ? 2 : 1, fixture.Sent.Count);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(393)]
    [InlineData(766)]
    [InlineData(770)]
    [InlineData(777)]
    public async Task CompletedRestrictedPacketGroupDoesNotClaimTheServerBrokeTheBlock(int protocol)
    {
        await using var fixture = new Fixture(protocol);
        fixture.State.Self.Uuid = SelfId;
        await ModeAsync(fixture, protocol, GameMode.Creative, true);
        await InfoAsync(fixture, protocol, GameMode.Creative);
        await fixture.HoldAsync("diamond_sword", 0);
        Task<DigOutcome> mining = fixture.Actions.DigBlockVerifiedAsync(Fixture.Target, Direction.North, confirmationWindow: TimeSpan.FromSeconds(2));
        await fixture.TickAsync();
        await ApplyAsync(fixture, protocol, "block_update", new ClientboundBlockUpdatePacket(Fixture.Target, fixture.State.World.GetBlock(Fixture.Target).StateId));
        Assert.Equal(DigOutcome.NotBroken, await mining.WaitAsync(TimeSpan.FromSeconds(5)));
        AssertGroup(fixture, protocol, 2);
    }

    private static async Task ModeAsync(Fixture fixture, int protocol, GameMode mode, bool instantBuild)
    {
        await ApplyAsync(fixture, protocol, "game_event", new ClientboundGameEventPacket(3, (float)mode));
        await ApplyAsync(fixture, protocol, protocol == 47 ? "abilities" : "player_abilities", new ClientboundPlayerAbilitiesPacket((byte)(instantBuild ? 8 : 0), .05F, .1F));
        Assert.Equal(mode, fixture.State.Self.GameMode);
        Assert.Equal(instantBuild, fixture.State.Self.InstantBuild);
    }

    private static Task InfoAsync(Fixture fixture, int protocol, GameMode mode)
        => protocol < 761
            ? ApplyAsync(fixture, protocol, "player_info", new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.AddPlayer, [new LegacyPlayerListEntry(SelfId, "self", [], (int)mode, 0, null)]))
            : ApplyAsync(fixture, protocol, "player_info_update", new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode, [new PlayerInfoEntry(SelfId, "self", [], false, null, mode, true, 0, null, 0, true)]));

    private static Task ApplyAsync(Fixture fixture, int protocol, string identifier, object packet)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, identifier);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        object decoded = codec.Decode(buffer.WrittenSpan, context);
        var again = new ArrayBufferWriter<byte>();
        var secondWriter = new PacketWriter(again);
        codec.Encode(ref secondWriter, decoded, context);
        Assert.Equal(buffer.WrittenSpan.ToArray(), again.WrittenSpan.ToArray());
        return fixture.ApplyAsync(identifier, decoded);
    }

    private static void AssertGroup(Fixture fixture, int protocol, int animations)
    {
        Assert.Equal(animations + 1, fixture.Sent.Count);
        int startIndex = protocol == 47 ? 1 : 0;
        ServerboundPlayerActionPacket start = Assert.IsType<ServerboundPlayerActionPacket>(fixture.Sent[startIndex]);
        Assert.Equal(0, start.Action);
        Assert.Equal(Fixture.Target, start.Position);
        Assert.Equal((byte)Direction.North, start.Direction);
        Assert.Equal(animations, fixture.Sent.Count(packet => packet is ServerboundSwingPacket or ServerboundPunchPacket));
        foreach (IPacket packet in fixture.Sent.Cast<IPacket>())
        {
            byte[] bytes = BoundDescriptorCodec.EncodeServerbound(protocol, packet);
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
            PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            Assert.True(registry.TryGetOutbound(packet.Type, out _, out BoundPacketCodec codec));
            var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
            IPacket decoded = Assert.IsAssignableFrom<IPacket>(codec.Decode(bytes, context));
            Assert.Equal(bytes, BoundDescriptorCodec.EncodeServerbound(protocol, decoded));
            if (decoded is ServerboundPlayerActionPacket action)
            {
                Assert.Equal(start.Action, action.Action);
                Assert.Equal(start.Position, action.Position);
                Assert.Equal(start.Direction, action.Direction);
                Assert.Equal(start.Sequence, action.Sequence);
            }
            else if (protocol == 777)
                Assert.IsType<ServerboundPunchPacket>(decoded);
            else
                Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(decoded).Hand);
        }
    }

    private static async Task ReleaseAsync(CancellationTokenSource caller, Task mining)
    {
        caller.Cancel();
        try
        {
            await mining.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal static readonly BlockPos Target = new(1, 64, 1);
        private readonly ChannelSessionScheduler _scheduler = new();
        internal TaskCompletionSource CancellationCleanupQueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? CommittedStartBarrier { get; set; }
        private readonly List<CancellationTokenSource> _lifetimes = [];
        private CancellationTokenSource _session = new();
        private readonly ApplierHarness _harness;
        private readonly int _protocol;
        internal InteractionActions Actions { get; }
        internal List<object> Sent { get; } = [];
        internal ClientState State => _harness.State;
        internal Action<IPacket>? PacketCommitted { get; set; }

        internal Fixture(int protocol)
        {
            _protocol = protocol;
            _lifetimes.Add(_session);
            Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
            _harness = new ApplierHarness(version);
            State.Registries = JavaGameData.Registries(protocol);
            var world = new Umpk.Game.World.World(WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), null, protocol), new RegistryBlockDataSource(State.Registries.Blocks, isLegacy: protocol < 393), WorldFactory.EmptyBiomes());
            Assert.True(State.Registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
            Assert.True(State.Registries.Blocks.TryGetNetworkId(Identifier.Minecraft("stone"), out int stoneId));
            State.RegistryTags.Install([new TagRegistry(RegistryIds.Block, [new TagEntry(Identifier.Minecraft("mineable/pickaxe"), [stoneId])])]);
            world.SetBlockStateId(Target, stone.DefaultStateId);
            State.InstallWorld(world);
            var services = new ClientSessionServices
            {
                Version = version,
                Options = new ClientOptions(),
                Policies = new ClientPolicies(),
                State = State,
                Wire = new WireIndex(version),
                Logger = NullLogger.Instance,
                Scheduler = new ObservedScheduler(_scheduler, () => CancellationCleanupQueued.TrySetResult()),
                CurrentSessionCancellation = () => _session.Token,
                Events = _harness.Events,
            };
            Actions = new InteractionActions(new Sink(this), services, _harness.Sequences);
        }

        internal async Task HoldAsync(string name, int damage)
        {
            Assert.True(State.Registries!.Items.TryGet(Identifier.Minecraft(name), out RegistryEntry<ItemDefinition> item));
            ItemStack stack = new(item, 1);
            if (_protocol < 393 || _protocol >= 766)
                stack = stack.With(DataComponents.Damage, new DamageComponent(damage));
            await SetSlotAsync(36, stack);
        }

        internal Task FeaturesAsync(string[] names)
        {
            Assert.True(JavaVersions.TryGetByProtocol(_protocol, out var version));
            ProtocolPhase phase = _protocol < 764 ? ProtocolPhase.Play : ProtocolPhase.Configuration;
            Assert.True(version.Protocol.TryGetRegistry(phase, PacketFlow.Clientbound, out var registry));
            var entry = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("update_enabled_features"));
            Assert.True(registry.TryGetInbound(entry.WireId, out var codec));
            Assert.True(codec.IsImplemented);
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            writer.WriteVarInt(names.Length);
            foreach (string name in names)
                writer.WriteString("minecraft:" + name);
            var context = new PacketCodecContext(State.Registries!, IConnectionCodecState.Empty);
            var packet = Assert.IsType<ClientboundConfigUpdateEnabledFeaturesPacket>(codec.Decode(output.WrittenSpan, context));
            Assert.Equal(names.Select(Identifier.Minecraft), packet.Features);
            Assert.Equal(phase, packet.Type.Phase);
            var again = new ArrayBufferWriter<byte>();
            var secondWriter = new PacketWriter(again);
            codec.Encode(ref secondWriter, packet, context);
            Assert.Equal(output.WrittenSpan.ToArray(), again.WrittenSpan.ToArray());
            return _scheduler.InvokeAsync(() => _harness.ApplyAsync(packet), _session.Token);
        }

        internal Task SetSlotAsync(short slot, ItemStack stack)
            => CreativeMiningRestrictionsRuntimeTests.ApplyAsync(this, _protocol, _protocol == 47 ? "set_slot" : "container_set_slot", new ClientboundContainerSetSlotPacket(0, 0, slot, stack));

        internal Task ApplyAsync(string identifier, object decoded)
            => _scheduler.InvokeAsync(() => _harness.ApplyAsync(decoded), CancellationToken.None);

        internal Task TickAsync()
        {
            CancellationToken token = _session.Token;
            return _scheduler.InvokeAsync(async () =>
            {
                State.AdvanceSessionTick();
                await Actions.TickMiningOnLoopAsync(token);
                Actions.TickAttackChargeOnLoop(playerTicking: true);
            }, CancellationToken.None);
        }

        internal void ReplaceSession()
        {
            _session.Cancel();
            _session = new CancellationTokenSource();
            _lifetimes.Add(_session);
            State.ResetForConfigurationReentry();
            State.ResetForSessionEnd();
        }

        public async ValueTask DisposeAsync()
        {
            _session.Cancel();
            await _scheduler.DisposeAsync();
            foreach (CancellationTokenSource lifetime in _lifetimes)
                lifetime.Dispose();
        }

        private sealed class ObservedScheduler(ChannelSessionScheduler scheduler, Action asynchronousWorkQueued) : ISessionScheduler
        {
            public bool IsCurrent => scheduler.IsCurrent;
            public void Post(Action work) => scheduler.Post(work);
            public Task InvokeAsync(Action work, CancellationToken cancellationToken) => scheduler.InvokeAsync(work, cancellationToken);
            public Task<TResult> InvokeAsync<TResult>(Func<TResult> work, CancellationToken cancellationToken) => scheduler.InvokeAsync(work, cancellationToken);
            public Task<TResult> InvokeAsync<TResult>(Func<ValueTask<TResult>> work, CancellationToken cancellationToken) => scheduler.InvokeAsync(work, cancellationToken);
            public Task InvokeAsync(Func<ValueTask> work, CancellationToken cancellationToken)
            {
                Task completion = scheduler.InvokeAsync(work, cancellationToken);
                asynchronousWorkQueued();
                return completion;
            }
            public ValueTask DisposeAsync() => scheduler.DisposeAsync();
        }

        private sealed class Sink(Fixture fixture) : IPacketSink
        {
            public async ValueTask SendAsync(object packet, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                IPacket typed = Assert.IsAssignableFrom<IPacket>(packet);
                fixture.Sent.Add(typed);
                fixture.PacketCommitted?.Invoke(typed);
                if (typed is ServerboundPlayerActionPacket { Action: 0 } && fixture.CommittedStartBarrier is { } barrier)
                    await barrier.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (typed is ServerboundPlayerActionPacket { Sequence: int sequence })
                    fixture._harness.Sequences.Acknowledge(sequence);
            }

            public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct)
                => throw new InvalidOperationException("Mining must use bound typed packets.");
        }
    }
}
