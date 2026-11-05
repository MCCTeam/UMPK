using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class EnchantmentDataHashTests
{
    private const int First = 12;
    private const int Second = 13;
    private static readonly PacketCodecContext Context = CreateContext();

    public static IEnumerable<object[]> Cases()
    {
        foreach (int protocol in new[] { 770, 771, 772, 773, 774, 775, 776, 777 })
            foreach (bool stored in new[] { false, true })
                foreach (string scenario in new[] { "empty", "one", "two", "upper", "zero", "two-keys", "reordered", "invalid-earlier-shadowed-positive", "positive-then-zero", "positive-then-positive" })
                    yield return [protocol, stored, scenario];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BoundReceivedStackUsesNativeDataHashOrFailsBeforeClickPayloadCompletes(int protocol, bool stored, string scenario)
    {
        byte[] received = Compose(protocol, stored, Entries(scenario));
        BoundPacketCodec inbound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(inbound.Decode(received, Context));
        Assert.Equal(1, packet.Item.Count);
        IReadOnlyList<EnchantmentInstance> values = Values(packet.Item, stored);
        Assert.Equal(Entries(scenario).Select(entry => entry.Level), values.Select(entry => entry.Level));
        Assert.Equal(received, Encode(inbound, packet));

        BoundPacketCodec outbound = BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:container_click");
        var click = new ServerboundContainerClickPacket(0, 1, 7, 0, 0, 0, null, [new PredictedSlot(7, packet.Item)], packet.Item);
        if (scenario is "zero" or "positive-then-zero")
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() => Encode(outbound, click));
            Assert.Contains("1..255", error.Message, StringComparison.Ordinal);
            return;
        }

        byte[] sent = Encode(outbound, click);
        var reader = new PacketReader(sent);
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(1, reader.ReadVarInt());
        Assert.Equal(7, reader.ReadShort());
        Assert.Equal(0, reader.ReadByte());
        Assert.Equal(0, reader.ReadVarInt());
        Assert.Equal(1, reader.ReadVarInt());
        Assert.Equal(7, reader.ReadShort());
        AssertHash(ItemStackCodecs.ReadHashedStack(ref reader, Context), packet.Item, protocol, stored, NativeHash(scenario));
        AssertHash(ItemStackCodecs.ReadHashedStack(ref reader, Context), packet.Item, protocol, stored, NativeHash(scenario));
        Assert.Equal(0, reader.Remaining);
        var decoded = Assert.IsType<ServerboundContainerClickPacket>(outbound.Decode(sent, Context));
        Assert.Equal(7, decoded.Slot);
        Assert.Equal(1, decoded.StateId);
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
    public void NestedEnchantmentKeepsKnownDataErrorsDistinctFromUnavailableHashes(int protocol, bool stored, bool zero)
    {
        ItemComponentTable table = Table(protocol);
        ItemStack nested = Decode(protocol, stored, zero ? "zero" : "positive-then-positive");
        ItemStack outer = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 1)
            .With(DataComponents.Container, new ContainerComponent([new ContainerSlotEntry(0, nested)]));
        if (zero)
        {
            Assert.Throws<ArgumentException>(() => ItemStackCodecs.ToHashedStack(outer, table, Context));
            return;
        }
        ItemStack canonical = outer.With(DataComponents.Container, new ContainerComponent([new ContainerSlotEntry(0, Decode(protocol, stored, "two"))]));
        HashedStackValue actual = ItemStackCodecs.ToHashedStack(outer, table, Context);
        HashedStackValue expected = ItemStackCodecs.ToHashedStack(canonical, table, Context);
        Assert.Equal(expected.AddedComponentHashes, actual.AddedComponentHashes);
        Assert.Single(actual.AddedComponentHashes);
    }

    [Theory]
    [InlineData(770)]
    [InlineData(777)]
    public void DistinctUnresolvedWireHoldersCannotBecomeAnInventedKnownMap(int protocol)
    {
        byte[] received = Compose(protocol, false, [(998, 0), (999, 1)]);
        BoundPacketCodec inbound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        ItemStack stack = Assert.IsType<ClientboundContainerSetSlotPacket>(inbound.Decode(received, Context)).Item;
        Assert.Equal(2, Values(stack, false).Count);
        Assert.All(Values(stack, false), instance => Assert.True(instance.Enchantment.IsDefault));
        stack = stack.With(DataComponents.MapId, new MapIdComponent(29));
        HashedStackValue hashed = ItemStackCodecs.ToHashedStack(stack, Table(protocol), Context);
        (int id, int hash) = Assert.Single(hashed.AddedComponentHashes);
        Assert.Equal(Table(protocol).WireId(DataComponents.MapId), id);
        Assert.Equal(new HashOps().Int(29), hash);
    }

    private static void AssertHash(HashedStackValue value, ItemStack item, int protocol, bool stored, int expected)
    {
        Assert.Equal(item.Item.NetworkId, value.ItemId);
        Assert.Equal(item.Count, value.Count);
        (int componentId, int hash) = Assert.Single(value.AddedComponentHashes);
        Assert.Equal(WireId(protocol, stored), componentId);
        Assert.Equal(expected, hash);
        Assert.Empty(value.RemovedComponentIds);
    }

    private static ItemStack Decode(int protocol, bool stored, string scenario) =>
        Assert.IsType<ClientboundContainerSetSlotPacket>(BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot").Decode(Compose(protocol, stored, Entries(scenario)), Context)).Item;

    private static IReadOnlyList<EnchantmentInstance> Values(ItemStack stack, bool stored) => stored
        ? Assert.IsType<StoredEnchantmentsComponent>(stack.Components.Get(DataComponents.StoredEnchantments)).Enchantments
        : Assert.IsType<EnchantmentsComponent>(stack.Components.Get(DataComponents.Enchantments)).Enchantments;

    private static (int Id, int Level)[] Entries(string scenario) => scenario switch
    {
        "empty" => [],
        "one" => [(First, 1)],
        "two" => [(First, 2)],
        "upper" => [(First, 255)],
        "zero" => [(First, 0)],
        "two-keys" => [(First, 1), (Second, 1)],
        "reordered" => [(Second, 1), (First, 1)],
        "invalid-earlier-shadowed-positive" => [(First, -1), (First, 1)],
        "positive-then-zero" => [(First, 1), (First, 0)],
        "positive-then-positive" => [(First, 1), (First, 2)],
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    // Original TypedDataComponent.encodeValue(RegistryOps<HashOps>) and getOrThrow/asInt witnesses at 1.21.5 and 26.3.
    private static int NativeHash(string scenario) => scenario switch
    {
        "empty" => -982207288,
        "one" or "invalid-earlier-shadowed-positive" => 323656802,
        "two" or "positive-then-positive" => 182816843,
        "upper" => 1892810400,
        "two-keys" or "reordered" => 1439783631,
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static byte[] Compose(int protocol, bool stored, (int Id, int Level)[] entries)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteShort(7);
        writer.WriteVarInt(1);
        writer.WriteVarInt(ItemTestRegistries.DiamondSword);
        writer.WriteVarInt(1);
        writer.WriteVarInt(0);
        writer.WriteVarInt(WireId(protocol, stored));
        writer.WriteVarInt(entries.Length);
        foreach ((int id, int level) in entries)
        {
            writer.WriteVarInt(id);
            writer.WriteVarInt(level);
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Encode(BoundPacketCodec bound, object packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, packet, Context);
        return buffer.WrittenSpan.ToArray();
    }

    private static int WireId(int protocol, bool stored) => stored
        ? protocol switch { <= 773 => 34, 774 => 41, 775 or 776 => 42, 777 => 45, _ => throw new ArgumentOutOfRangeException(nameof(protocol)) }
        : protocol <= 773 ? 10 : 13;

    private static ItemComponentTable Table(int protocol) => protocol switch
    {
        770 => ItemStackCodecs.ComponentsV1_21_5,
        777 => ItemStackCodecs.ComponentsV26_3,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    private static PacketCodecContext CreateContext()
    {
        Registry<EnchantmentDefinition> registry = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(First, Identifier.Minecraft("unbreaking"), new EnchantmentDefinition(3))
            .Add(Second, Identifier.Minecraft("fortune"), new EnchantmentDefinition(3))
            .Build();
        return new PacketCodecContext(RegistryAccess.FromSnapshot(ItemTestRegistries.Context.Registries.Snapshot.With(registry)), IConnectionCodecState.Empty);
    }
}
