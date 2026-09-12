using System.Buffers;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Decodes hand-written <c>open_screen</c> and <c>container_set_slot</c> frames through the real transport and the real generated per-protocol descriptor, following the legacy-item-frame model. The payload bytes are written by hand so these pin the wire layout rather than round-tripping our own encoder, so the expected framing is independent of the implementation under test.</summary>
public sealed class MenuFrameDecodeTests
{
    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    /// <summary>1.8 clientbound play <c>minecraft:open_screen</c>.</summary>
    private const int OpenScreenWireId1_8 = 0x2D;

    /// <summary>1.9-1.12.2 clientbound play <c>minecraft:open_screen</c>.</summary>
    private const int OpenScreenWireId1_9 = 0x13;

    /// <summary>1.13-1.13.2 clientbound play <c>minecraft:open_screen</c>.</summary>
    private const int OpenScreenWireId1_13 = 0x14;

    /// <summary>1.9-1.12.2 clientbound play <c>minecraft:container_set_slot</c>.</summary>
    private const int SetSlotWireId1_9 = 0x16;

    /// <summary>1.8 clientbound play <c>minecraft:set_slot</c>.</summary>
    private const int SetSlotWireId1_8 = 0x2F;

    [Theory]
    [InlineData(47, OpenScreenWireId1_8, "minecraft:chest", 27)]
    [InlineData(107, OpenScreenWireId1_9, "minecraft:chest", 27)]
    [InlineData(110, OpenScreenWireId1_9, "minecraft:furnace", 3)]
    [InlineData(210, OpenScreenWireId1_9, "minecraft:brewing_stand", 5)]
    [InlineData(315, OpenScreenWireId1_9, "minecraft:shulker_box", 27)]
    [InlineData(340, OpenScreenWireId1_9, "minecraft:hopper", 5)]
    [InlineData(393, OpenScreenWireId1_13, "minecraft:chest", 54)]
    [InlineData(404, OpenScreenWireId1_13, "minecraft:enchanting_table", 2)]
    public async Task LegacyOpenScreenFrame_Decodes(int protocol, int wireId, string windowType, int slots)
    {
        // These fourteen protocols share the legacy string layout and each descriptor selects it.
        ClientboundOpenScreenPacket decoded = await DecodeOpenScreenAsync(protocol, wireId, windowType, slots);

        Assert.Equal(9, decoded.ContainerId);
        Assert.Equal(windowType, decoded.LegacyType);
        Assert.Equal(slots, decoded.LegacySlotCount);
        Assert.Equal(-1, decoded.MenuTypeId);
        Assert.Null(decoded.LegacyEntityId);
    }

    [Theory]
    [InlineData(47, OpenScreenWireId1_8)]
    [InlineData(340, OpenScreenWireId1_9)]
    [InlineData(404, OpenScreenWireId1_13)]
    public async Task LegacyOpenScreenFrame_HorseCarriesTrailingEntityId(int protocol, int wireId)
    {
        // "EntityHorse" is the one window type whose frame has a trailing int. Reading the band without it would desynchronise the decoder on the next packet, so it is pinned on every era.
        ClientboundOpenScreenPacket decoded =
            await DecodeOpenScreenAsync(protocol, wireId, "EntityHorse", slots: 17, horseEntityId: 4242);

        Assert.Equal("EntityHorse", decoded.LegacyType);
        Assert.Equal(17, decoded.LegacySlotCount);
        Assert.Equal(4242, decoded.LegacyEntityId);
    }

    [Theory]
    [InlineData(47, SetSlotWireId1_8)]
    [InlineData(107, SetSlotWireId1_9)]
    [InlineData(340, SetSlotWireId1_9)]
    public async Task LegacySetSlotFrame_NegativeContainerIdSurvivesDecode(int protocol, int wireId)
    {
        // The container id is a signed byte on this band. Reading it unsigned turned -1 into 255 and -2 into 254, so the cursor update and the player-inventory write a server-side give arrives on were both dropped by window routing. 0xFF / 0xFE are the literal bytes on the wire.
        ClientboundContainerSetSlotPacket cursor = await DecodeSetSlotAsync(protocol, wireId, containerId: 0xFF);
        Assert.Equal(-1, cursor.ContainerId);

        ClientboundContainerSetSlotPacket playerInventory = await DecodeSetSlotAsync(protocol, wireId, containerId: 0xFE);
        Assert.Equal(-2, playerInventory.ContainerId);
    }

    [Theory]
    [InlineData(47, SetSlotWireId1_8)]
    [InlineData(340, SetSlotWireId1_9)]
    public async Task LegacySetSlotFrame_PositiveContainerIdIsUnchanged(int protocol, int wireId)
    {
        // The signed read must not disturb ordinary window ids; 100 is well inside the positive range.
        ClientboundContainerSetSlotPacket decoded = await DecodeSetSlotAsync(protocol, wireId, containerId: 100);
        Assert.Equal(100, decoded.ContainerId);
    }

    private static async Task<ClientboundOpenScreenPacket> DecodeOpenScreenAsync(
        int protocol, int wireId, string windowType, int slots, int? horseEntityId = null)
    {
        InboundItem item = await DecodeAsync(protocol, wireId, OpenScreenPayload(windowType, slots, horseEntityId));
        return Assert.IsType<ClientboundOpenScreenPacket>(item.Packet);
    }

    private static async Task<ClientboundContainerSetSlotPacket> DecodeSetSlotAsync(
        int protocol, int wireId, int containerId)
    {
        InboundItem item = await DecodeAsync(protocol, wireId, LegacySetSlotPayload(containerId));
        return Assert.IsType<ClientboundContainerSetSlotPacket>(item.Packet);
    }

    private static async Task<InboundItem> DecodeAsync(int protocol, int wireId, ReadOnlyMemory<byte> payload)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));

        var pair = DuplexPipePair.Create();
        await using var reader = new JavaConnection(pair.Right, new JavaConnectionOptions
        {
            ReadIdleTimeout = TimeSpan.Zero,
            UnknownPacketPolicy = UnknownPacketPolicy.Skip,
        });
        reader.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Clientbound);
        reader.SetCodecState(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        reader.SetPhase(ProtocolPhase.Play);
        reader.Start();

        await using var writer = new JavaConnection(pair.Left, new JavaConnectionOptions { ReadIdleTimeout = TimeSpan.Zero });
        await writer.SendFrameAsync(wireId, payload, Ct());

        return await reader.ReceiveAsync(Ct());
    }

    /// <summary>The 1.8-1.13.2 open-window body: unsigned byte window id, string window type, chat-JSON title, unsigned byte slot count, and a trailing int ONLY when the type is "EntityHorse". Written by hand to pin the layout across the legacy bands.</summary>
    private static ReadOnlyMemory<byte> OpenScreenPayload(string windowType, int slots, int? horseEntityId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteByte(9);
        w.WriteString(windowType);
        w.WriteString("{\"text\":\"Container\"}");
        w.WriteByte((byte)slots);
        if (horseEntityId is { } entityId)
            w.WriteInt(entityId);

        return buffer.WrittenMemory;
    }

    /// <summary>The 1.8-1.12.2 set-slot body: signed byte window id, short slot, legacy slot. The window id byte is written raw so a negative id is exercised as the literal wire byte.</summary>
    private static ReadOnlyMemory<byte> LegacySetSlotPayload(int containerId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteByte((byte)containerId);
        w.WriteShort(36);
        w.WriteShort(297);  // bread, a real non-empty stack
        w.WriteByte(1);
        w.WriteShort(0);
        w.WriteByte(0x00);  // NBT: TAG_End
        return buffer.WrittenMemory;
    }
}
