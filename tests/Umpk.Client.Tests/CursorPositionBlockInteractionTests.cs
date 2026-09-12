using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The block-place / item-use send path across the 1.8 boundary.</summary>
/// <remarks>
/// Protocol 47 uses <c>minecraft:block_place</c> at wire ID 0x08. It does not have <c>use_item_on</c>, which starts in 1.9. <c>InteractionActions</c> must select the packet form for the negotiated protocol.
/// <para>The wire order is packed block position, face byte, item stack, then three byte cursor coordinates. Face 255 at position (-1, -1, -1) means to use the held item in the air.</para>
/// </remarks>
public sealed class CursorPositionBlockInteractionTests
{
    private static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    private static InteractionActions Actions(RecordingSink recorder, int protocol)
    {
        JavaVersion version = Version(protocol);
        var state = new ClientState(new ClientFeatures().Normalized());
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };
        return new InteractionActions(recorder, services, new SequenceTracker());
    }

    /// <summary>The gate reads the outbound table rather than a wire-id lookup. Protocol 1.8 can send <c>block_place</c> and cannot send <c>use_item_on</c> or <c>use_item</c>; 1.9 is the mirror.</summary>
    [Theory]
    [InlineData(47, false, false, true)]
    [InlineData(107, true, true, false)]
    [InlineData(340, true, true, false)]
    [InlineData(776, true, true, false)]
    public void TheOutboundTable_Splits_AtTheNineBoundary(
        int protocol, bool useItemOn, bool useItem, bool blockPlace)
    {
        var wire = new WireIndex(Version(protocol));

        Assert.Equal(useItemOn, wire.CanSendPlay(ItemPackets.Serverbound.UseItemOn));
        Assert.Equal(useItem, wire.CanSendPlay(ItemPackets.Serverbound.UseItem));
        Assert.Equal(blockPlace, wire.CanSendPlay(ItemPackets.Serverbound.LegacyBlockPlace));
    }

    [Fact]
    public async Task PlaceBlock_On1_8_Sends_TheLegacyBlockPlaceRecord()
    {
        var recorder = new RecordingSink();

        await Actions(recorder, 47).PlaceBlockAsync(
            new BlockPos(10, 64, -3), Direction.Up, new Vec3d(0.5, 1.0, 0.5));

        var sent = Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(recorder.Packets));
        Assert.Equal(new BlockPos(10, 64, -3), sent.Position);
        Assert.Equal((int)Direction.Up, sent.Face);

        // (int)(0.5 * 16) is 8; 1.0 clamps to the top of the byte's documented 0..15 domain.
        Assert.Equal(8, sent.CursorX);
        Assert.Equal(15, sent.CursorY);
        Assert.Equal(8, sent.CursorZ);

        Assert.Empty(recorder.Frames);
    }

    [Fact]
    public async Task UseItem_On1_8_Sends_TheAirSentinel()
    {
        var recorder = new RecordingSink();

        await Actions(recorder, 47).UseItemAsync();

        var sent = Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(recorder.Packets));
        Assert.Equal(new BlockPos(-1, -1, -1), sent.Position);
        Assert.Equal(255, sent.Face);
        Assert.Equal(0, sent.CursorX);
        Assert.Equal(0, sent.CursorY);
        Assert.Equal(0, sent.CursorZ);
    }

    /// <summary>1.9 and later must keep using the modern records; the routing is a 1.8 branch only.</summary>
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(776)]
    public async Task PlaceBlock_OnLaterVersions_Keeps_TheModernRecord(int protocol)
    {
        var recorder = new RecordingSink();

        await Actions(recorder, protocol).PlaceBlockAsync(
            new BlockPos(10, 64, -3), Direction.Up, new Vec3d(0.5, 1.0, 0.5));

        var sent = Assert.IsType<ServerboundUseItemOnPacket>(Assert.Single(recorder.Packets));
        Assert.Equal(new BlockPos(10, 64, -3), sent.Position);
        Assert.Equal(0.5f, sent.CursorX);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(776)]
    public async Task UseItem_OnLaterVersions_Keeps_TheModernRecord(int protocol)
    {
        var recorder = new RecordingSink();

        await Actions(recorder, protocol).UseItemAsync();

        Assert.IsType<ServerboundUseItemPacket>(Assert.Single(recorder.Packets));
    }

    /// <summary>The frame placed on the wire, encoded through the bound protocol descriptor at protocol 47 rather than through a codec picked by the test. A marker throws here, and a bare <c>ServerboundPlay(id) &gt;= 0</c> check would pass for one, which is why this is the assertion that matters. The bytes are vanilla's: 8-byte packed position, one face byte, a 2-byte empty item stack (short -1), three cursor bytes.</summary>
    [Fact]
    public void TheLegacyFrame_Encodes_ThroughTheShippedTableAt47()
    {
        JavaVersion version = Version(47);
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        Assert.True(
            registry.TryGetOutbound(
                ItemPackets.Serverbound.LegacyBlockPlace, out int wireId, out BoundPacketCodec bound),
            "block_place has no outbound binding at protocol 47");
        Assert.Equal(0x08, wireId);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(
            ref writer,
            new ServerboundLegacyBlockPlacePacket(new BlockPos(1, 2, 3), 1, ItemStack.Empty, 8, 15, 8),
            new PacketCodecContext(JavaGameData.Registries(47), IConnectionCodecState.Empty));

        // The legacy packed position is (x & X_MASK) << 38 | (y & Y_MASK) << 26 | (z & Z_MASK), with NUM_X_BITS = NUM_Z_BITS = 26 and NUM_Y_BITS = 12, so (1, 2, 3) packs to (1 << 38) | (2 << 26) | 3 = 0x0000004008000003.
        Assert.Equal<byte[]>(
            [0x00, 0x00, 0x00, 0x40, 0x08, 0x00, 0x00, 0x03, 0x01, 0xFF, 0xFF, 0x08, 0x0F, 0x08],
            buffer.WrittenSpan.ToArray());
        Assert.Equal(14, buffer.WrittenCount);
    }

    /// <summary>Cross-era rejection: the same record must NOT be sendable on 1.9, where the wire moved to <c>use_item_on</c> with a different field order and a float cursor. A binding that leaked forward would look fine in a round trip and be wrong on the wire.</summary>
    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(776)]
    public void TheLegacyFrame_IsRefused_AfterTheBoundary(int protocol)
    {
        PhaseRegistry registry = Version(protocol).Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);

        Assert.False(registry.TryGetOutbound(ItemPackets.Serverbound.LegacyBlockPlace, out _, out _));
    }

    /// <summary>The held stack is the client's own, which is what keeps vanilla from pushing a corrective set-slot after every placement: <c>processPlayerBlockPlacement</c> acts with the current held item and compares the packet's stack only to decide whether to resync.</summary>
    [Fact]
    public async Task PlaceBlock_On1_8_Carries_TheSelectedHotbarStack()
    {
        var recorder = new RecordingSink();
        JavaVersion version = Version(47);
        var state = new ClientState(new ClientFeatures().Normalized());
        var services = new ClientSessionServices
        {
            Version = version,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = state,
            Wire = new WireIndex(version),
            Logger = NullLogger.Instance,
            Scheduler = new Umpk.Hosting.ChannelSessionScheduler(),
        };

        ItemStack stone = TestItems.Stone(42);
        var slots = new ItemStack[45];
        Array.Fill(slots, ItemStack.Empty);
        slots[36 + 4] = stone;
        state.Inventory.ReplaceContents(InventoryState.PlayerWindowId, slots);
        state.Self.HeldSlot = 4;

        await new InteractionActions(recorder, services, new SequenceTracker())
            .PlaceBlockAsync(new BlockPos(0, 64, 0), Direction.Up, new Vec3d(0.5, 0.5, 0.5));

        var sent = Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(recorder.Packets));
        Assert.Equal(stone, sent.HeldItem);
        Assert.Equal(42, sent.HeldItem.Count);
    }
}
