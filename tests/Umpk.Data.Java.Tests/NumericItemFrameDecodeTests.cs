using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>End-to-end decode of representative pre-flattening frames: a 1.12.2 <c>container_set_slot</c> (wire 0x16) carrying dirt and a 1.8 <c>set_slot</c> (wire 0x2F) carrying bread, both driven through the real transport, the real generated descriptor, and the real item registry from <see cref="JavaGameData"/>. The payload bytes are written by hand so the test pins the wire layout, not a round trip of our own encoder.</summary>
public class NumericItemFrameDecodeTests
{
    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;

    /// <summary>1.8 clientbound play <c>minecraft:set_slot</c>.</summary>
    private const int SetSlotWireId1_8 = 0x2F;

    /// <summary>1.9-1.12.2 clientbound play <c>minecraft:container_set_slot</c>; 0x16 across the whole band, and the id used across the supported band.</summary>
    private const int SetSlotWireId1_9 = 0x16;

    [Theory]
    [InlineData(340, SetSlotWireId1_9, 3, 0, "dirt")]        // 1.12.2 join inventory
    [InlineData(340, SetSlotWireId1_9, 297, 0, "bread")]
    [InlineData(340, SetSlotWireId1_9, 263, 0, "coal")]
    [InlineData(47, SetSlotWireId1_8, 297, 0, "bread")]      // 1.8 /give bread
    [InlineData(47, SetSlotWireId1_8, 3, 0, "dirt")]
    [InlineData(47, SetSlotWireId1_8, 1, 0, "stone")]
    // These three registered ids must resolve instead of degrading to minecraft:unknown.
    [InlineData(47, SetSlotWireId1_8, 31, 0, "tallgrass")]
    [InlineData(47, SetSlotWireId1_8, 62, 0, "lit_furnace")]
    [InlineData(47, SetSlotWireId1_8, 383, 0, "spawn_egg")]
    [InlineData(107, SetSlotWireId1_9, 3, 0, "dirt")]        // rest of the band: 1.9, 1.10, 1.11, 1.12
    [InlineData(210, SetSlotWireId1_9, 3, 0, "dirt")]
    [InlineData(316, SetSlotWireId1_9, 297, 0, "bread")]
    [InlineData(335, SetSlotWireId1_9, 297, 0, "bread")]
    public async Task LegacySetSlotFrame_ResolvesItem(int protocol, int wireId, int itemId, int damage, string name)
    {
        ClientboundContainerSetSlotPacket decoded =
            await DecodeSetSlotAsync(protocol, wireId, itemId, count: 1, damage);

        Assert.Equal(Identifier.Minecraft(name), decoded.Item.Item.Id);
        Assert.Equal(1, decoded.Item.Count);
        Assert.Equal((itemId << 16) | damage, decoded.Item.Item.NetworkId);
    }

    /// <summary>The same (id, damage) pair off the wire must resolve to the same identity on 1.8 as on 1.12.2, and that identity is the era's OWN registry name with the damage carried separately.</summary>
    /// <remarks>
    /// <para>Both protocol bands resolve the same bytes to the same base registry identity.</para>
    /// <para>The base registry key is the identity and the damage value is separate variant data.</para>
    /// <para>Protocols 107-340 also keep variation damage separate instead of synthesizing <c>&lt;name&gt;_&lt;damage&gt;</c> identifiers such as <c>dye_5</c> and <c>coal_1</c>.</para>
    /// </remarks>
    [Theory]
    [InlineData(35, 14, "wool")]     // red wool: the live 1.8 vs 1.12.2 disagreement
    [InlineData(5, 4, "planks")]     // acacia planks: creativegive minecraft:planks failed on 1.8
    [InlineData(351, 5, "dye")]      // purple dye
    [InlineData(263, 1, "coal")]     // charcoal, the one minecraft-data called coal_1
    [InlineData(1, 1, "stone")]      // granite
    [InlineData(165, 0, "slime")]    // slime_block is the 1.13 name, not this era's
    public async Task LegacyDamageVariant_HasTheSameIdentityOn1_8_AndOn1_12_2(int itemId, int damage, string name)
    {
        ClientboundContainerSetSlotPacket on47 =
            await DecodeSetSlotAsync(47, SetSlotWireId1_8, itemId, count: 1, damage);
        ClientboundContainerSetSlotPacket on340 =
            await DecodeSetSlotAsync(340, SetSlotWireId1_9, itemId, count: 1, damage);

        foreach (ItemStack stack in new[] { on47.Item, on340.Item })
        {
            // Identity is the base registry name, and the network id is the BASE composite, not the damaged one: the damage is not part of what the item is.
            Assert.Equal(Identifier.Minecraft(name), stack.Item.Id);
            Assert.Equal(itemId << 16, stack.Item.NetworkId);

            // Nothing is lost: the wire damage comes back as a component (and is absent when zero).
            if (damage == 0)
                Assert.False(stack.Components.TryGet(DataComponents.Damage, out _));

            else
            {
                Assert.True(stack.Components.TryGet(DataComponents.Damage, out DamageComponent? carried));
                Assert.Equal(damage, carried!.Value);
            }
        }

        Assert.Equal(on47.Item.Item.Id, on340.Item.Item.Id);
        Assert.Equal(on47.Item.Item.NetworkId, on340.Item.Item.NetworkId);
    }

    /// <summary>The whole pre-flattening band carries exactly one row per item id, so damage variants never resolve to distinct identifiers.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(110)]
    [InlineData(210)]
    [InlineData(315)]
    [InlineData(316)]
    [InlineData(335)]
    [InlineData(338)]
    [InlineData(340)]
    public void EveryPreFlatteningItemKey_IsABaseComposite(int protocol)
    {
        Registry<ItemDefinition> items = JavaGameData.Registries(protocol).Items;
        Assert.True(items.Count > 0);
        foreach (RegistryEntry<ItemDefinition> entry in items)
            Assert.Equal(0, entry.NetworkId & 0xFFFF);

    }

    [Theory]
    [InlineData(47, SetSlotWireId1_8)]
    [InlineData(107, SetSlotWireId1_9)]
    [InlineData(340, SetSlotWireId1_9)]
    public async Task LegacySetSlotFrame_UnmappedItemId_DegradesInsteadOfFaulting(int protocol, int wireId)
    {
        // Item id 4000 is absent from every legacy dataset and must degrade without faulting.
        ClientboundContainerSetSlotPacket decoded =
            await DecodeSetSlotAsync(protocol, wireId, itemId: 4000, count: 7, damage: 5);

        Assert.Equal(Identifier.Minecraft("unknown"), decoded.Item.Item.Id);
        Assert.Equal(7, decoded.Item.Count);
        // The placeholder keeps the original (id, damage) composite, so nothing about the frame is lost.
        Assert.Equal((4000 << 16) | 5, decoded.Item.Item.NetworkId);
    }

    private static async Task<ClientboundContainerSetSlotPacket> DecodeSetSlotAsync(
        int protocol, int wireId, int itemId, int count, int damage)
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
        await writer.SendFrameAsync(wireId, LegacySetSlotPayload(itemId, count, damage), Ct());

        InboundItem item = await reader.ReceiveAsync(Ct());
        return Assert.IsType<ClientboundContainerSetSlotPacket>(item.Packet);
    }

    /// <summary>The 1.8/1.12.2 set-slot body: byte window id, short slot, then the legacy slot (short item id, byte count, short damage, NBT-or-0). Written by hand to pin the wire layout.</summary>
    private static ReadOnlyMemory<byte> LegacySetSlotPayload(int itemId, int count, int damage)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteByte(0);              // window id 0 (the player inventory)
        w.WriteShort(36);            // slot 36 (first hotbar slot)
        w.WriteShort((short)itemId);
        w.WriteByte((byte)count);
        w.WriteShort((short)damage);
        w.WriteByte(0x00);           // NBT: TAG_End, no item tag
        return buffer.WrittenMemory;
    }
}
