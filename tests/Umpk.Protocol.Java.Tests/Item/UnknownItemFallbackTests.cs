using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>The legacy (1.8-1.12.2) unknown-item policy. The pre-flattening id space has a long tail (server-side modifications, curated-dataset gaps), so an id the registry cannot resolve degrades to the named <c>minecraft:unknown</c> placeholder instead of raising and dropping the connection. The placeholder keeps the original <c>(id&lt;&lt;16)|damage</c> composite as its network id, so the degrade is still frame-exact: re-encoding the decoded stack reproduces the original bytes. This is deliberately NOT the flattened policy; those readers still throw (see <see cref="ItemStackCodecTests.UnknownItemId_Throws"/> and <c>ContainerContentCodecTests.SetContent_UnknownItemId_FollowsSharedClientPolicy</c>), because a flattened registry is gapless by construction and an unresolvable id there is a real desync.</summary>
public class UnknownItemFallbackTests
{
    /// <summary>An item id present in no test registry entry, base or subtype.</summary>
    private const int UnmappedItemId = 4000;

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void UnmappedLegacyId_DegradesToPlaceholder_WithoutThrowing(int damage)
    {
        ItemStack stack = ReadLegacy(LegacyStackBytes(UnmappedItemId, count: 3, damage));

        Assert.Equal(Identifier.Minecraft("unknown"), stack.Item.Id);
        Assert.Equal(3, stack.Count);
        Assert.Equal((UnmappedItemId << 16) | damage, stack.Item.NetworkId);
    }

    [Fact]
    public void UnmappedLegacyId_CarriesNoSyntheticDamageComponent()
    {
        // The placeholder's own identity carries the damage (it is the full composite), so unlike the base-item fallback there is nothing left to carry as a component.
        ItemStack stack = ReadLegacy(LegacyStackBytes(UnmappedItemId, count: 1, damage: 9));
        Assert.False(stack.Components.TryGet(DataComponents.Damage, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void UnmappedLegacyId_ReEncodesToTheOriginalBytes(int damage)
    {
        // The placeholder retains the unmapped id and damage value for frame-exact re-encoding.
        byte[] original = LegacyStackBytes(UnmappedItemId, count: 2, damage);
        ItemStack stack = ReadLegacy(original);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        ItemStackCodecs.WriteLegacyStack(ref writer, stack, ItemTestRegistries.Context);

        Assert.Equal(original, buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void MappedLegacyIds_KeepTheirExistingResolution()
    {
        // Guard against the degrade path swallowing ids that do resolve: the exact composite still wins, and the base-item fallback still carries the wire damage as a component.
        ItemStack wool = ReadLegacy(LegacyStackBytes(
            ItemTestRegistries.LegacyBlockItemId, count: 1, ItemTestRegistries.LegacyBlockSubtype));
        Assert.Equal(Identifier.Minecraft("legacy_red_wool"), wool.Item.Id);

        ItemStack sword = ReadLegacy(LegacyStackBytes(ItemTestRegistries.LegacyBaseItemId, count: 1, damage: 50));
        Assert.Equal(Identifier.Minecraft("legacy_diamond_sword"), sword.Item.Id);
        Assert.True(sword.Components.TryGet(DataComponents.Damage, out DamageComponent? d));
        Assert.Equal(50, d!.Value);
    }

    private static ItemStack ReadLegacy(byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        ItemStack stack = ItemStackCodecs.ReadLegacyStack(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return stack;
    }

    /// <summary>A 1.8 legacy slot: short item id, byte count, short damage, NBT-or-0.</summary>
    private static byte[] LegacyStackBytes(int itemId, int count, int damage)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteShort((short)itemId);
        w.WriteByte((byte)count);
        w.WriteShort((short)damage);
        w.WriteByte(0x00); // TAG_End
        return buffer.WrittenSpan.ToArray();
    }
}
