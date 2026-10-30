using System.Buffers;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class ComponentPredicateIdentity770SessionTests
{
    [Theory]
    [InlineData(770, "duplicate-exact", false, true)]
    [InlineData(770, "duplicate-recursive", false, true)]
    [InlineData(770, "duplicate-partial", false, true)]
    [InlineData(770, "changed-exact-damage", true, false)]
    [InlineData(770, "changed-recursive-can-break", true, false)]
    [InlineData(770, "changed-partial-damage", true, false)]
    [InlineData(770, "reordered-exact", true, false)]
    [InlineData(770, "canonical-any", false, false)]
    [InlineData(770, "canonical-point", false, false)]
    [InlineData(770, "ignored-damage-keys", false, false)]
    [InlineData(769, "legacy-duplicate", false, true)]
    [InlineData(769, "legacy-tooltip-change", true, false)]
    public async Task FramedSurvivalReplacementUsesNativePredicateIdentity(int protocol, string scenario, bool reset, bool identicalWire)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        (AdventureModePredicateComponent before, AdventureModePredicateComponent after) = Predicates(session, scenario);
        (byte[] originalBytes, AdventureModePredicateComponent original) = await Deliver(session, protocol, before);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> start = await session.Tick();
            Assert.Collection(start,
                packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), protocol, 0),
                AssertSwing,
                AssertSwing);
            AssertSwing(Assert.Single(await session.Tick()));
            Assert.False(action.IsCompleted);

            (byte[] replacementBytes, AdventureModePredicateComponent replacement) = await Deliver(session, protocol, after);
            Assert.NotSame(original, replacement);
            Assert.NotSame(original.Predicates, replacement.Predicates);
            Assert.Equal(identicalWire, originalBytes.AsSpan().SequenceEqual(replacementBytes));
            if (scenario == "duplicate-recursive")
            {
                var originalNested = Assert.IsType<AdventureModePredicateComponent>(Assert.Single(Assert.Single(original.Predicates).ComponentMatchers!.Exact).Value);
                var replacementNested = Assert.IsType<AdventureModePredicateComponent>(Assert.Single(Assert.Single(replacement.Predicates).ComponentMatchers!.Exact).Value);
                Assert.NotSame(originalNested, replacementNested);
                Assert.NotSame(originalNested.Predicates, replacementNested.Predicates);
            }

            List<IPacket> continued = await session.Tick();
            ServerboundPlayerActionPacket[] changes = continued.OfType<ServerboundPlayerActionPacket>().ToArray();
            if (reset)
            {
                Assert.Collection(continued,
                    packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), protocol, 1),
                    packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), protocol, 0),
                    AssertSwing);
            }
            else
            {
                // The current 770 Client fails here after a fresh equivalent framed slot update.
                Assert.Empty(changes);
                Assert.Collection(continued, AssertSwing);
            }
            Assert.False(action.IsCompleted);

            // One continuation preceded replacement: retaining progress needs three more updates, restarting needs six.
            int stopAfter = reset ? 6 : 3;
            for (int tick = 1; tick <= stopAfter; tick++)
            {
                List<IPacket> sent = await session.Tick();
                if (tick < stopAfter)
                {
                    Assert.Collection(sent, AssertSwing);
                    Assert.False(action.IsCompleted);
                }
                else
                {
                    Assert.Collection(sent,
                        packet => ComponentAdventureMiningSessionTests.AssertAction(Assert.IsType<ServerboundPlayerActionPacket>(packet), protocol, 2),
                        AssertSwing);
                    var stop = Assert.IsType<ServerboundPlayerActionPacket>(sent[0]);
                    Assert.False(action.IsCompleted);
                    await session.Complete(action, stop);
                    Assert.True(action.IsCompletedSuccessfully);
                }
            }
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await session.Tick();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    private static (AdventureModePredicateComponent Before, AdventureModePredicateComponent After) Predicates(Session session, string scenario)
    {
        AdventureModePredicateComponent Plain(string block = "stone", bool tooltip = true)
            => new([Block(block, [], [])], tooltip);

        BlockPredicateEntry Block(string block, IReadOnlyList<DataComponentEntry> exact, IReadOnlyList<BlockPredicatePartialMatcher> partial)
            => new(new HolderSetRef(null, [session.Client.State.Registries!.Blocks[Identifier.Minecraft(block)].NetworkId]), null, null)
            {
                ComponentMatchers = session.Protocol == 770 ? new(exact, partial) : null,
            };

        DataComponentEntry Damage(int value) => DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(value));
        DataComponentEntry Nested(string block = "stone") => DataComponentEntry.Set(DataComponents.CanBreak, Plain(block));
        AdventureModePredicateComponent Exact(params DataComponentEntry[] values) => new([Block("stone", values, [])]);
        AdventureModePredicateComponent Partial(NbtCompound value) => new([Block("stone", [], [new(Identifier.Minecraft("damage"), false, value)])]);
        static NbtCompound Range(int min, int max) => new() { ["min"] = new NbtInt(min), ["max"] = new NbtInt(max) };
        static NbtCompound DamageRange(int max) => new() { ["damage"] = Range(1, max), ["durability"] = new NbtInt(3) };

        return scenario switch
        {
            // The 770 payload has no tooltip boolean: these distinct helper-model flags must produce identical frames.
            "duplicate-exact" => (Exact(Damage(1)), Exact(Damage(1)) with { ShowInTooltip = false }),
            "duplicate-recursive" => (Exact(Nested()), Exact(Nested())),
            "duplicate-partial" => (Partial(DamageRange(2)), Partial(DamageRange(2))),
            "changed-exact-damage" => (Exact(Damage(1)), Exact(Damage(2))),
            "changed-recursive-can-break" => (Exact(Nested()), Exact(Nested("dirt"))),
            "changed-partial-damage" => (Partial(DamageRange(2)), Partial(DamageRange(3))),
            "reordered-exact" => (Exact(Damage(1), Nested()), Exact(Nested(), Damage(1))),
            "canonical-any" => (Partial(new()), Partial(new() { ["damage"] = new NbtCompound(), ["durability"] = new NbtCompound() })),
            "canonical-point" => (Partial(new() { ["damage"] = new NbtInt(5) }), Partial(new() { ["damage"] = Range(5, 5) })),
            "ignored-damage-keys" => (
                Partial(new() { ["durability"] = Range(1, 2), ["damage"] = new NbtInt(5), ["ignored"] = new NbtString("before") }),
                Partial(new()
                {
                    ["ignored"] = new NbtInt(42),
                    ["damage"] = new NbtCompound() { ["max"] = new NbtInt(5), ["min"] = new NbtInt(5), ["ignored"] = new NbtString("after") },
                    ["durability"] = new NbtCompound() { ["max"] = new NbtInt(2), ["min"] = new NbtInt(1) },
                })),
            "legacy-duplicate" => (Plain(), Plain()),
            "legacy-tooltip-change" => (Plain(), Plain(tooltip: false)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static async Task<(byte[] Payload, AdventureModePredicateComponent Component)> Deliver(Session session, int protocol, AdventureModePredicateComponent expected)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        var entry = Assert.Single(registry.Packets, pair => pair.Type.Id == Identifier.Minecraft("container_set_slot"));
        Assert.True(registry.TryGetInbound(entry.WireId, out BoundPacketCodec codec));
        Assert.True(codec.IsImplemented);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        var packet = new ClientboundContainerSetSlotPacket(0, 7, 36, stack.With(DataComponents.CanBreak, expected));
        byte[] bytes = Encode(codec, packet, context);
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
        Assert.Equal(bytes, Encode(codec, decoded, context));
        AssertPredicate(expected, decoded.Item.Components.Get(DataComponents.CanBreak)!, protocol);
        var received = new TaskCompletionSource<ClientboundContainerSetSlotPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        int observedRawFrames = 0;
        void Observe(PacketObservation observation)
        {
            if (observation.Flow == PacketFlow.Clientbound && observation.Phase == ProtocolPhase.Play && observation.WireId == entry.WireId && observation.RawPayload.SequenceEqual(bytes))
                Interlocked.Increment(ref observedRawFrames);
        }
        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 7, Slot: 36 } slot)
                received.TrySetResult(slot);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(entry.WireId, bytes, session.Token);
            ClientboundContainerSetSlotPacket arrived = await received.Task.WaitAsync(session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Equal(1, Volatile.Read(ref observedRawFrames));
            Assert.Equal(bytes, Encode(codec, arrived, context));
            ItemStack applied = session.Client.State.Inventory.GetSlot(36);
            Assert.Equal("minecraft:diamond_pickaxe", applied.Item.Id.ToString());
            Assert.Equal(1, applied.Count);
            AdventureModePredicateComponent actual = applied.Components.Get(DataComponents.CanBreak)!;
            Assert.NotSame(expected, actual);
            Assert.NotSame(decoded.Item.Components.Get(DataComponents.CanBreak), actual);
            AssertPredicate(expected, actual, protocol);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
            return (bytes, actual);
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static void AssertPredicate(AdventureModePredicateComponent expected, AdventureModePredicateComponent actual, int protocol)
    {
        Assert.NotNull(actual);
        Assert.Equal(protocol == 769 ? expected.ShowInTooltip : true, actual.ShowInTooltip);
        Assert.Equal(expected.Predicates.Count, actual.Predicates.Count);
        for (int index = 0; index < expected.Predicates.Count; index++)
        {
            BlockPredicateEntry first = expected.Predicates[index];
            BlockPredicateEntry second = actual.Predicates[index];
            Assert.Equal(first.Blocks!.Tag, second.Blocks!.Tag);
            Assert.Equal(first.Blocks.Ids, second.Blocks.Ids);
            Assert.Equal(first.Properties, second.Properties);
            Assert.Equal(first.Nbt, second.Nbt);
            if (protocol == 769)
            {
                Assert.Null(second.ComponentMatchers);
                continue;
            }
            BlockPredicateComponentMatchers wanted = Assert.IsType<BlockPredicateComponentMatchers>(first.ComponentMatchers);
            BlockPredicateComponentMatchers observed = Assert.IsType<BlockPredicateComponentMatchers>(second.ComponentMatchers);
            Assert.Equal(wanted.Exact.Count, observed.Exact.Count);
            for (int exact = 0; exact < wanted.Exact.Count; exact++)
            {
                DataComponentEntry left = wanted.Exact[exact];
                DataComponentEntry right = observed.Exact[exact];
                Assert.Equal(left.Type.Id, right.Type.Id);
                Assert.False(right.IsRemoval);
                if (left.Value is DamageComponent damage)
                    Assert.Equal(damage.Value, Assert.IsType<DamageComponent>(right.Value).Value);
                else
                    AssertPredicate(Assert.IsType<AdventureModePredicateComponent>(left.Value), Assert.IsType<AdventureModePredicateComponent>(right.Value), protocol);
            }
            Assert.Equal(wanted.Partial.Count, observed.Partial.Count);
            for (int partial = 0; partial < wanted.Partial.Count; partial++)
            {
                Assert.Equal(wanted.Partial[partial].Type, observed.Partial[partial].Type);
                Assert.False(observed.Partial[partial].IsAnyValue);
                Assert.NotSame(wanted.Partial[partial].Value, observed.Partial[partial].Value);
                Assert.Equal(wanted.Partial[partial].Value, observed.Partial[partial].Value);
            }
        }
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static void AssertSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }
}
