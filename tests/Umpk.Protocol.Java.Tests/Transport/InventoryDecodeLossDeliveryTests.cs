using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Ordered managed delivery of a declared compact-component loss. Header variants are constructed codec inputs; these tests do not claim native execution.</summary>
public sealed class InventoryDecodeLossDeliveryTests
{
    private const int Delimiter = 0;
    private const int Slot = 1;
    private const int Contents = 2;
    private const int Cursor = 3;
    private const int PlayerSlot = 4;
    private const int Unscoped = 5;
    private const int WeaponComponent = 26;

    [Theory]
    [InlineData(Slot, InventoryUpdateLossKind.ContainerSlot, 0, 7, 36, null, false)]
    [InlineData(Slot, InventoryUpdateLossKind.ContainerSlot, -1, 7, -1, null, false)]
    [InlineData(Slot, InventoryUpdateLossKind.ContainerSlot, -2, 7, 0, null, false)]
    [InlineData(Contents, InventoryUpdateLossKind.ContainerContents, 3, 7, null, 46, false)]
    [InlineData(Contents, InventoryUpdateLossKind.ContainerContents, 3, 7, null, 2, true)]
    [InlineData(Cursor, InventoryUpdateLossKind.Cursor, null, null, null, null, false)]
    [InlineData(PlayerSlot, InventoryUpdateLossKind.PlayerInventorySlot, null, null, 0, null, false)]
    public async Task DecodedHeaderScopeSurvivesBindingAndPrecedesFollowingPacket(
        int wireId, InventoryUpdateLossKind kind, int? containerId, int? stateId,
        int? slot, int? itemCount, bool unmodeledCarried)
    {
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: true);
        byte[] body = LossBody(wireId, containerId, stateId, slot, itemCount, unmodeledCarried);
        await Write(pair.Right.Output, wireId, body);
        await Write(pair.Right.Output, Slot, EmptySlotBody(9));

        InboundItem item = await connection.ReceiveAsync(timeout.Token);
        InboundDecodeLoss loss = Assert.IsType<InboundDecodeLoss>(item.DecodeLoss);
        Assert.Null(item.Packet);
        Assert.Null(item.Bundle);
        Assert.Null(loss.BeforePacketIndex);
        Assert.Equal(ProtocolPhase.Play, loss.Phase);
        Assert.Equal(PacketFlow.Clientbound, loss.Flow);
        Assert.Equal(WeaponComponent, loss.ComponentWireId);
        Assert.Equal(Identifier.Minecraft("weapon"), loss.ComponentId);
        Assert.Equal(wireId, item.Frame.WireId);
        Assert.Equal(body, item.Frame.Payload.ToArray());
        Assert.Equal(body, loss.Frame.Payload.ToArray());
        Assert.Equal(wireId, loss.Frame.WireId);
        Assert.Equal(new InventoryUpdateLossScope(kind, containerId, stateId, slot, itemCount), loss.InventoryScope);

        InboundItem next = await connection.ReceiveAsync(timeout.Token);
        Assert.Null(next.DecodeLoss);
        Assert.Equal(9, Assert.IsType<ClientboundContainerSetSlotPacket>(next.Packet).Slot);
    }

    [Fact]
    public async Task DefaultConnectionKeepsExistingSkipContract()
    {
        Assert.False(new JavaConnectionOptions().DeliverUnmodeledPacketLosses);
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: false);
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 36, null, false));
        await Write(pair.Right.Output, Slot, EmptySlotBody(9));

        InboundItem item = await connection.ReceiveAsync(timeout.Token);
        Assert.Null(item.DecodeLoss);
        Assert.Equal(9, Assert.IsType<ClientboundContainerSetSlotPacket>(item.Packet).Slot);
    }

    [Fact]
    public async Task BundleLossPositionsKeepRepeatedAndTrailingOrder()
    {
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: true);
        await Write(pair.Right.Output, Delimiter, []);
        await Write(pair.Right.Output, Slot, EmptySlotBody(9));
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 36, null, false));
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 37, null, false));
        await Write(pair.Right.Output, Slot, EmptySlotBody(10));
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 38, null, false));
        await Write(pair.Right.Output, Delimiter, []);
        await Write(pair.Right.Output, Slot, EmptySlotBody(11));

        InboundItem item = await connection.ReceiveAsync(timeout.Token);
        PacketBundle bundle = Assert.IsType<PacketBundle>(item.Bundle);
        Assert.Null(item.Packet);
        Assert.Null(item.DecodeLoss);
        Assert.Equal(2, bundle.Packets.Count);
        Assert.Equal(bundle.Packets.Count, bundle.Frames.Count);
        Assert.Equal(new[] { 9, 10 }, bundle.Packets.Select(packet => Assert.IsType<ClientboundContainerSetSlotPacket>(packet).Slot));
        Assert.Equal(new int?[] { 1, 1, 2 }, bundle.DecodeLosses.Select(loss => loss.BeforePacketIndex));
        Assert.Equal(new int?[] { 36, 37, 38 }, bundle.DecodeLosses.Select(loss => loss.InventoryScope!.Slot));
        foreach (InboundDecodeLoss loss in bundle.DecodeLosses)
        {
            Assert.Equal(Slot, loss.Frame.WireId);
            Assert.Equal(LossBody(Slot, 0, 7, loss.InventoryScope!.Slot, null, false), loss.Frame.Payload.ToArray());
        }

        InboundItem after = await connection.ReceiveAsync(timeout.Token);
        Assert.Null(after.Bundle);
        Assert.Equal(11, Assert.IsType<ClientboundContainerSetSlotPacket>(after.Packet).Slot);
    }

    [Fact]
    public async Task LossOnlyBundleIsDeliveredBeforeTheFollowingPacket()
    {
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: true);
        await Write(pair.Right.Output, Delimiter, []);
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 36, null, false));
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 37, null, false));
        await Write(pair.Right.Output, Delimiter, []);
        await Write(pair.Right.Output, Slot, EmptySlotBody(9));

        PacketBundle bundle = Assert.IsType<PacketBundle>((await connection.ReceiveAsync(timeout.Token)).Bundle);
        Assert.Empty(bundle.Packets);
        Assert.Empty(bundle.Frames);
        Assert.Equal(new int?[] { 0, 0 }, bundle.DecodeLosses.Select(loss => loss.BeforePacketIndex));
        Assert.Equal(new int?[] { 36, 37 }, bundle.DecodeLosses.Select(loss => loss.InventoryScope!.Slot));
        Assert.Equal(9, Assert.IsType<ClientboundContainerSetSlotPacket>((await connection.ReceiveAsync(timeout.Token)).Packet).Slot);
    }

    [Fact]
    public async Task IncompleteBundleDoesNotLeakLosses()
    {
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: true);
        await Write(pair.Right.Output, Delimiter, []);
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 36, null, false));
        await pair.Right.Output.CompleteAsync();

        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await connection.ReceiveAsync(timeout.Token));
    }

    [Fact]
    public async Task LossFramesCountTowardTheBundleLimit()
    {
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: true, useUnscopedBinding: true);
        await Write(pair.Right.Output, Delimiter, []);
        for (int i = 0; i <= BundleAccumulator.BundleSizeLimit; i++)
            await Write(pair.Right.Output, Unscoped, []);

        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(async () => await connection.ReceiveAsync(timeout.Token));
        Assert.Equal(CloseReason.ProtocolViolation, closed.Reason);
    }

    [Fact]
    public async Task UnscopedLossDoesNotInventAnInventoryHeader()
    {
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: true, useUnscopedBinding: true);
        await Write(pair.Right.Output, Unscoped, [0x42]);
        await Write(pair.Right.Output, Slot, EmptySlotBody(9));

        InboundItem item = await connection.ReceiveAsync(timeout.Token);
        InboundDecodeLoss loss = Assert.IsType<InboundDecodeLoss>(item.DecodeLoss);
        Assert.Null(loss.InventoryScope);
        Assert.Null(loss.BeforePacketIndex);
        Assert.Null(item.Packet);
        Assert.Equal(Unscoped, loss.Frame.WireId);
        Assert.Equal(new byte[] { 0x42 }, loss.Frame.Payload.ToArray());
        Assert.Equal(9, Assert.IsType<ClientboundContainerSetSlotPacket>((await connection.ReceiveAsync(timeout.Token)).Packet).Slot);
    }

    [Fact]
    public async Task FrameModeKeepsItsSkipContractEvenWithTheOptIn()
    {
        using var timeout = Timeout();
        var pair = DuplexPipePair.Create();
        await using var connection = Connect(pair.Left, deliverLosses: true, frameMode: true);
        await Write(pair.Right.Output, Slot, LossBody(Slot, 0, 7, 36, null, false));
        await Write(pair.Right.Output, Slot, EmptySlotBody(9));

        InboundItem item = await connection.ReceiveAsync(timeout.Token);
        Assert.Null(item.DecodeLoss);
        Assert.Equal(9, Assert.IsType<ClientboundContainerSetSlotPacket>(item.Packet).Slot);
    }

    private static CancellationTokenSource Timeout() => new(TimeSpan.FromSeconds(10));

    private static JavaConnection Connect(System.IO.Pipelines.IDuplexPipe pipe, bool deliverLosses, bool useUnscopedBinding = false, bool frameMode = false)
    {
        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "1.21.5", 770), new ProtocolFeatures());
        (int WireId, string Name)[] entries = [(Delimiter, "bundle_delimiter"), (Slot, "container_set_slot"), (Contents, "container_set_content"), (Cursor, "set_cursor_item"), (PlayerSlot, "set_player_inventory")];
        foreach ((int wireId, string name) in entries)
            PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, wireId, "minecraft:" + name);
        var binding = new DescriptorFrameCodecBinding(builder.Build(), ItemTestRegistries.Context);
        var connection = new JavaConnection(pipe, new JavaConnectionOptions
        {
            ReadIdleTimeout = TimeSpan.Zero,
            DeliverUnmodeledPacketLosses = deliverLosses,
        });
        connection.BindCodec(useUnscopedBinding ? new UnscopedLossBinding(binding) : binding, PacketFlow.Clientbound);
        connection.SetPhase(ProtocolPhase.Play);
        if (frameMode)
            connection.SetDecodeFilter(PacketDecodeFilter.All);
        connection.Start();
        return connection;
    }

    private static async Task Write(System.IO.Pipelines.PipeWriter output, int wireId, byte[] body)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        writer.WriteVarInt(wireId);
        writer.WriteBytes(body);
        await FramingTests.WriteRawFrameAsync(output, bytes.WrittenSpan.ToArray());
    }

    private static byte[] EmptySlotBody(int slot)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        writer.WriteVarInt(0);
        writer.WriteVarInt(8);
        writer.WriteShort((short)slot);
        writer.WriteVarInt(0);
        return bytes.WrittenSpan.ToArray();
    }

    private static byte[] LossBody(int wireId, int? containerId, int? stateId, int? slot, int? itemCount, bool unmodeledCarried)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        if (wireId is Slot or Contents)
        {
            writer.WriteVarInt(containerId!.Value);
            writer.WriteVarInt(stateId!.Value);
        }
        if (wireId == Slot)
            writer.WriteShort((short)slot!.Value);
        else if (wireId == PlayerSlot)
            writer.WriteVarInt(slot!.Value);
        else if (wireId == Contents)
        {
            writer.WriteVarInt(itemCount!.Value);
            if (unmodeledCarried)
                for (int i = 0; i < itemCount.Value; i++)
                    writer.WriteVarInt(0);
        }
        writer.WriteVarInt(1);
        writer.WriteVarInt(ItemTestRegistries.DiamondSword);
        writer.WriteVarInt(1);
        writer.WriteVarInt(0);
        writer.WriteVarInt(WeaponComponent);
        writer.WriteVarInt(0);
        return bytes.WrittenSpan.ToArray();
    }

    private sealed class UnscopedLossBinding(IFrameCodecBinding inner) : IFrameCodecBinding
    {
        public bool TryDecode(in FrameDecodeContext context, out object packet)
        {
            if (context.WireId == Unscoped)
                throw new UnmodeledItemComponentException(WeaponComponent, Identifier.Minecraft("weapon"));
            return inner.TryDecode(in context, out packet);
        }

        public bool TryEncode(object packet, ProtocolPhase phase, PacketFlow flow, IBufferWriter<byte> output) => inner.TryEncode(packet, phase, flow, output);
        public bool IsTerminal(in FrameDecodeContext context, out ProtocolPhase nextPhase) => inner.IsTerminal(in context, out nextPhase);
        public bool IsCompressionEnablePoint(in FrameDecodeContext context) => inner.IsCompressionEnablePoint(in context);
        public bool IsEncryptionEnablePoint(in FrameDecodeContext context) => inner.IsEncryptionEnablePoint(in context);
        public bool IsBundleDelimiter(in FrameDecodeContext context) => inner.IsBundleDelimiter(in context);
    }
}
