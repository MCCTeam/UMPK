using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class EnchantmentFinalLevelAdmissionTests
{
    private const int First = 12;
    private const int Second = 13;
    private static readonly PacketCodecContext Context = CreateContext();

    public static IEnumerable<object[]> LevelCases()
    {
        int[] protocols = [766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
        string[] scenarios = ["empty", "zero", "upper", "negative", "wide", "min-int", "max-int", "negative-shadowed-zero", "wide-shadowed-upper", "valid-shadowed-negative", "valid-shadowed-wide", "other-key-cannot-shadow", "interleaved-shadow", "invalid-valid-invalid"];
        foreach (int protocol in protocols)
            foreach (bool stored in new[] { false, true })
                foreach (string scenario in scenarios)
                    yield return [protocol, stored, scenario];
    }

    [Theory]
    [MemberData(nameof(LevelCases))]
    public void BoundComponentAdmitsOnlyNativeFinalLevelsAndPreservesRawEntries(int protocol, bool stored, string scenario)
    {
        (bool accepted, (int Id, int Level)[] expected) = Scenario(scenario);
        bool tooltip = scenario != "upper";
        byte[] frame = Compose(protocol, stored, (ref PacketWriter writer) =>
        {
            writer.WriteVarInt(expected.Length);
            foreach ((int id, int level) in expected)
            {
                writer.WriteVarInt(id);
                writer.WriteVarInt(level);
            }
            if (protocol < 770)
                writer.WriteBool(tooltip);
        });
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        if (!accepted)
        {
            Assert.Throws<ProtocolViolationException>(() => bound.Decode(frame, Context));
            return;
        }

        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.Decode(frame, Context));
        Assert.Equal(0, packet.ContainerId);
        Assert.Equal(1, packet.StateId);
        Assert.Equal(7, packet.Slot);
        Assert.Equal(ItemTestRegistries.DiamondSword, packet.Item.Item.NetworkId);
        Assert.Equal(1, packet.Item.Count);
        IReadOnlyList<EnchantmentInstance> actual;
        bool actualTooltip;
        if (stored)
        {
            Assert.True(packet.Item.Components.TryGet(DataComponents.StoredEnchantments, out StoredEnchantmentsComponent? component));
            actual = component!.Enchantments;
            actualTooltip = component.ShowInTooltip;
        }
        else
        {
            Assert.True(packet.Item.Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? component));
            actual = component!.Enchantments;
            actualTooltip = component.ShowInTooltip;
        }
        Assert.Equal(expected.Length, actual.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Enchantment.NetworkId);
            Assert.Equal(expected[index].Level, actual[index].Level);
        }
        Assert.Equal(protocol >= 770 || tooltip, actualTooltip);
        var buffer = new ArrayBufferWriter<byte>();
        var encodedWriter = new PacketWriter(buffer);
        bound.Encode(ref encodedWriter, packet, Context);
        Assert.Equal(frame, buffer.WrittenSpan.ToArray());
    }

    [Theory]
    [InlineData(766, false)]
    [InlineData(766, true)]
    [InlineData(769, false)]
    [InlineData(769, true)]
    [InlineData(770, false)]
    [InlineData(770, true)]
    [InlineData(777, false)]
    [InlineData(777, true)]
    public void FinalMapGuardPreservesPayloadFramingAndLegacyConstructorOrder(int protocol, bool stored)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        byte[] complete = Compose(protocol, stored, (ref PacketWriter writer) =>
        {
            writer.WriteVarInt(2);
            writer.WriteVarInt(First);
            writer.WriteVarInt(-1);
            writer.WriteVarInt(First);
            writer.WriteVarInt(0);
            if (protocol < 770)
                writer.WriteBool(false);
        });
        _ = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.Decode(complete, Context));
        Assert.Throws<ProtocolViolationException>(() => bound.Decode(complete[..^1], Context));
        byte[] trailing = [.. complete, 0];
        Assert.Throws<ProtocolViolationException>(() => bound.Decode(trailing, Context));

        foreach (int count in new[] { -1, int.MaxValue })
        {
            byte[] invalidCount = Compose(protocol, stored, (ref PacketWriter writer) => writer.WriteVarInt(count));
            Assert.Throws<ProtocolViolationException>(() => bound.Decode(invalidCount, Context));
        }
        byte[] overlongLevel = Compose(protocol, stored, (ref PacketWriter writer) =>
        {
            writer.WriteVarInt(1);
            writer.WriteVarInt(First);
            writer.WriteBytes([0x80, 0x80, 0x80, 0x80, 0x80, 0x80]);
        });
        Assert.Throws<ProtocolViolationException>(() => bound.Decode(overlongLevel, Context));

        if (protocol < 770)
        {
            byte[] missingTooltip = Compose(protocol, stored, (ref PacketWriter writer) =>
            {
                writer.WriteVarInt(1);
                writer.WriteVarInt(First);
                writer.WriteVarInt(-1);
            });
            ProtocolViolationException failure = Assert.Throws<ProtocolViolationException>(() => bound.Decode(missingTooltip, Context));
            Assert.Contains("ran past the end of the payload", failure.Message, StringComparison.Ordinal);
        }
    }

    private static (bool Accepted, (int Id, int Level)[] Entries) Scenario(string name) => name switch
    {
        "empty" => (true, []),
        "zero" => (true, [(First, 0)]),
        "upper" => (true, [(First, 255)]),
        "negative" => (false, [(First, -1)]),
        "wide" => (false, [(First, 256)]),
        "min-int" => (false, [(First, int.MinValue)]),
        "max-int" => (false, [(First, int.MaxValue)]),
        "negative-shadowed-zero" => (true, [(First, -1), (First, 0)]),
        "wide-shadowed-upper" => (true, [(First, 256), (First, 255)]),
        "valid-shadowed-negative" => (false, [(First, 1), (First, -1)]),
        "valid-shadowed-wide" => (false, [(First, 1), (First, 256)]),
        "other-key-cannot-shadow" => (false, [(First, -1), (Second, 1)]),
        "interleaved-shadow" => (true, [(First, -1), (Second, 1), (First, 0)]),
        "invalid-valid-invalid" => (false, [(First, -1), (First, 0), (First, 256)]),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private delegate void PayloadWriter(ref PacketWriter writer);

    private static byte[] Compose(int protocol, bool stored, PayloadWriter write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        if (protocol < 768)
            writer.WriteByte(0);
        else
            writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteShort(7);
        writer.WriteVarInt(1);
        writer.WriteVarInt(ItemTestRegistries.DiamondSword);
        writer.WriteVarInt(1);
        writer.WriteVarInt(0);
        writer.WriteVarInt(WireId(protocol, stored));
        write(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static int WireId(int protocol, bool stored) => (protocol, stored) switch
    {
        (766 or 767, false) => 9,
        (766 or 767, true) => 23,
        (768 or 769, false) => 10,
        (768 or 769, true) => 33,
        (770 or 771 or 772 or 773, false) => 10,
        (770 or 771 or 772 or 773, true) => 34,
        (774 or 775 or 776 or 777, false) => 13,
        (774, true) => 41,
        (775 or 776, true) => 42,
        (777, true) => 45,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    private static PacketCodecContext CreateContext()
    {
        Registry<EnchantmentDefinition> enchantments = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(First, Identifier.Minecraft("sharpness"), new EnchantmentDefinition(5))
            .Add(Second, Identifier.Minecraft("efficiency"), new EnchantmentDefinition(5))
            .Build();
        RegistrySnapshot snapshot = ItemTestRegistries.Context.Registries.Snapshot.With(enchantments);
        return new PacketCodecContext(RegistryAccess.FromSnapshot(snapshot), IConnectionCodecState.Empty);
    }
}
