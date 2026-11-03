using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EnchantmentClickSendTests
{
    [Theory]
    [InlineData(770, false, "one")]
    [InlineData(770, false, "duplicate")]
    [InlineData(770, false, "zero")]
    [InlineData(770, true, "one")]
    [InlineData(770, true, "duplicate")]
    [InlineData(770, true, "zero")]
    [InlineData(777, false, "one")]
    [InlineData(777, false, "duplicate")]
    [InlineData(777, false, "zero")]
    [InlineData(777, true, "one")]
    [InlineData(777, true, "duplicate")]
    [InlineData(777, true, "zero")]
    public async Task PacketPopulatedClickPredictsThenHashesBeforeEmitting(int protocol, bool stored, string scenario)
    {
        JavaVersion version = BoundPacketApplierHarness.Version(protocol);
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        Registry<EnchantmentDefinition> enchantments = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(12, Identifier.Minecraft("unbreaking"), new EnchantmentDefinition(3)).Build();
        var context = new PacketCodecContext(RegistryAccess.FromSnapshot(JavaGameData.Registries(protocol).Snapshot.With(enchantments)), IConnectionCodecState.Empty);
        ItemStack stack = new(context.Registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        EnchantmentInstance[] values = scenario switch
        {
            "one" => [new(enchantments[12], 1)],
            "duplicate" => [new(enchantments[12], 1), new(enchantments[12], 2)],
            "zero" => [new(enchantments[12], 0)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        stack = stored
            ? stack.With(DataComponents.StoredEnchantments, new StoredEnchantmentsComponent(values))
            : stack.With(DataComponents.Enchantments, new EnchantmentsComponent(values));
        BoundPacketCodec inbound = BoundDescriptorCodec.Clientbound(protocol, "container_set_slot");
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        inbound.Encode(ref writer, new ClientboundContainerSetSlotPacket(0, 1, 9, stack), context);
        var received = Assert.IsType<ClientboundContainerSetSlotPacket>(inbound.Decode(buffer.WrittenSpan, context));
        Assert.Equal(values.Select(value => value.Level), Levels(received.Item, stored).Select(value => value.Level));
        await harness.ApplyAsync(received);
        Assert.Equal(received.Item, harness.State.Inventory.GetSlot(9));

        var sink = new EncodingSink(version, context);
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        var actions = new InventoryActions(sink, services);
        if (scenario == "zero")
            await Assert.ThrowsAsync<ArgumentException>(() => actions.ClickAsync(new ClickAction.Pickup(9, MouseButton.Left)));
        else
            _ = await actions.ClickAsync(new ClickAction.Pickup(9, MouseButton.Left));

        // Native menu prediction occurs before hash generation; a DATA error does not roll it back.
        Assert.True(harness.State.Inventory.GetSlot(9).IsEmpty);
        Assert.Equal(received.Item, harness.State.Inventory.Cursor);
        Assert.True(harness.State.Inventory.HasPendingPrediction);
        Assert.Equal(1, sink.EncodeAttempts);
        if (scenario == "zero")
            Assert.Empty(sink.EmittedPayloads);
        else
            AssertPayload(Assert.Single(sink.EmittedPayloads), protocol, stored, received.Item.Item.NetworkId, scenario == "one" ? 323656802 : 182816843);
    }

    [Theory]
    [InlineData(770, false, false)]
    [InlineData(770, false, true)]
    [InlineData(770, true, false)]
    [InlineData(770, true, true)]
    [InlineData(777, false, false)]
    [InlineData(777, false, true)]
    [InlineData(777, true, false)]
    [InlineData(777, true, true)]
    public async Task ConnectedSessionUsesNativeHashAndKnownDataFailureBeforePeerReceivesClick(int protocol, bool stored, bool zero)
    {
        await using ComponentAdventureMiningSessionTests.Session session = await ComponentAdventureMiningSessionTests.Session.Create(protocol, configurationEnchantments: NativeMiningEnchantmentRegistryFixture.Create(protocol));
        var registries = session.Client.State.Registries!;
        RegistryEntry<EnchantmentDefinition> holder = registries.Enchantments[Identifier.Minecraft("unbreaking")];
        EnchantmentInstance[] values = zero ? [new(holder, 0)] : [new(holder, 1), new(holder, 2)];
        ItemStack stack = new(registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        stack = stored
            ? stack.With(DataComponents.StoredEnchantments, new StoredEnchantmentsComponent(values))
            : stack.With(DataComponents.Enchantments, new EnchantmentsComponent(values));
        await session.Send(new ClientboundContainerSetSlotPacket(0, 19, 9, stack));
        Assert.Empty(await session.Barrier());
        Assert.Equal(values.Select(value => value.Level), Levels(session.Client.State.Inventory.GetSlot(9), stored).Select(value => value.Level));
        JavaVersion version = BoundPacketApplierHarness.Version(protocol);
        PhaseRegistry outbound = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        int clickWireId = Assert.Single(outbound.Packets, pair => pair.Type.Id == Identifier.Minecraft("container_click")).WireId;
        List<byte[]> observed = [];
        void Observe(PacketObservation observation)
        {
            if (observation.Flow == PacketFlow.Serverbound && observation.Phase == ProtocolPhase.Play && observation.WireId == clickWireId)
                observed.Add(observation.RawPayload.ToArray());
        }
        session.Client.PacketFrameObserved += Observe;
        try
        {
            Task<ClickResult> action = await session.Client.InvokeAsync(client => client.Actions.Inventory.ClickAsync(new ClickAction.Pickup(9, MouseButton.Left), session.Token), session.Token);
            if (zero)
                await Assert.ThrowsAsync<ArgumentException>(() => action.WaitAsync(session.Token));
            else
            {
                _ = await action.WaitAsync(session.Token);
                var frame = await session.Server.NextFrameAsync(session.Token);
                Assert.Equal(clickWireId, frame.WireId);
                byte[] payload = frame.Payload.ToArray();
                Assert.Equal(payload, Assert.Single(observed));
                AssertPayload(payload, protocol, stored, stack.Item.NetworkId, 182816843, stateId: 19);
            }
            Assert.True(session.Client.State.Inventory.GetSlot(9).IsEmpty);
            Assert.Equal(values.Select(value => value.Level), Levels(session.Client.State.Inventory.Cursor, stored).Select(value => value.Level));
            Assert.True(session.Client.State.Inventory.HasPendingPrediction);
            if (zero)
                Assert.Empty(observed);

            // A subsequent peer-observed keepalive proves an encode error emitted no partial frame and left this connection usable.
            const long nonce = 704031;
            await session.Send(new ClientboundPlayKeepAlivePacket(nonce));
            var echo = await session.Server.NextFrameAsync(session.Token);
            Assert.True(outbound.TryGetInbound(echo.WireId, out BoundPacketCodec bound));
            var response = Assert.IsType<ServerboundPlayKeepAlivePacket>(bound.Decode(echo.Payload, new PacketCodecContext(registries, IConnectionCodecState.Empty)));
            Assert.Equal(nonce, response.Id);
            Assert.Empty(await session.Barrier());
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static IReadOnlyList<EnchantmentInstance> Levels(ItemStack stack, bool stored) => stored
        ? Assert.IsType<StoredEnchantmentsComponent>(stack.Components.Get(DataComponents.StoredEnchantments)).Enchantments
        : Assert.IsType<EnchantmentsComponent>(stack.Components.Get(DataComponents.Enchantments)).Enchantments;

    private static void AssertPayload(byte[] payload, int protocol, bool stored, int itemId, int nativeHash, int stateId = 1)
    {
        var reader = new PacketReader(payload);
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(stateId, reader.ReadVarInt());
        Assert.Equal(9, reader.ReadShort());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(1, reader.ReadVarInt());
        Assert.Equal(9, reader.ReadShort());
        Assert.False(reader.ReadBool());
        Assert.True(reader.ReadBool());
        Assert.Equal(itemId, reader.ReadVarInt());
        Assert.Equal(1, reader.ReadVarInt());
        Assert.Equal(1, reader.ReadVarInt());
        Assert.Equal((protocol, stored) switch { (770, false) => 10, (770, true) => 34, (777, false) => 13, (777, true) => 45, _ => throw new ArgumentOutOfRangeException(nameof(protocol)) }, reader.ReadVarInt());
        Assert.Equal(nativeHash, reader.ReadInt());
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(0, reader.Remaining);
    }

    // This substitutes only transport I/O: it uses the actual descriptor's outbound codec before recording an emitted payload.
    private sealed class EncodingSink(JavaVersion version, PacketCodecContext context) : IPacketSink
    {
        public int EncodeAttempts { get; private set; }
        public List<byte[]> EmittedPayloads { get; } = [];

        public ValueTask SendAsync(object packet, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            IPacket typed = Assert.IsAssignableFrom<IPacket>(packet);
            PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
            Assert.True(registry.TryGetOutbound(typed.Type, out _, out BoundPacketCodec bound));
            Assert.True(bound.IsImplemented);
            EncodeAttempts++;
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            bound.Encode(ref writer, packet, context);
            EmittedPayloads.Add(buffer.WrittenSpan.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected raw frame in container-click test.");
    }
}
