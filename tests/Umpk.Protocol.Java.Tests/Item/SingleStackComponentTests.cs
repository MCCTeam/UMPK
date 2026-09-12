using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary><c>minecraft:use_remainder</c> and <c>minecraft:sulfur_cube_content</c>, the two components that were listed-but-untyped while their payload - one bare nested item stack - was already modeled.</summary>
/// <remarks>
/// <para>An untyped component id is not inert: on a COMPACT component list (the one an item stack carries) the payload has no length prefix, so the first stack that patches one raises <see cref="UnmodeledItemComponentException"/> and costs the packet. A bucket, a honey bottle or any other item with <c>use_remainder</c> was enough.</para>
/// <para>Both eras' forms are pinned here because they are exactly the pair the codebase warns cannot be separated by a round trip: the count-first stack and the 26.1 template stack SWAP their first two fields, so a template frame read count-first takes the item id as the count. Only a frame-length or a field-value assertion tells them apart, which is what the byte assertions below are for.</para>
/// <para><c>use_remainder</c> carries the count-first stack through 1.21.11 and the template form from 26.1. The 26.2-only <c>sulfur_cube_content</c> component carries the template-stack form.</para>
/// </remarks>
public sealed class SingleStackComponentTests
{
    /// <summary>The 768-774 form: a count-first stack, so the first byte is the COUNT.</summary>
    [Fact]
    public void UseRemainder_OnTheCountFirstWireLayouts_RoundTripsAndLeadsWithTheCount()
    {
        ItemComponentTable table = ItemComponentTable.V1_21_5();
        Assert.True(table.Contains(DataComponents.UseRemainder), "use_remainder is untyped on the 770 table");
        ItemComponentCodec codec = table.ByKey(DataComponents.UseRemainder);

        var value = new UseRemainderComponent(
            new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 3));

        byte[] bytes = Encode(codec, value);
        Assert.Equal(3, bytes[0]);                        // count first
        Assert.Equal(ItemTestRegistries.Stone, bytes[1]); // then the item holder id

        var decoded = (UseRemainderComponent)Decode(codec, bytes);
        Assert.Equal(3, decoded.ConvertInto.Count);
        Assert.Equal(ItemTestRegistries.Stone, decoded.ConvertInto.Item.NetworkId);
    }

    /// <summary>The 775/776 form: an item-stack template, so the first byte is the ITEM ID.</summary>
    [Theory]
    [InlineData("use_remainder")]
    [InlineData("sulfur_cube_content")]
    public void TemplateWireLayoutComponents_RoundTripAndLeadWithTheItemId(string component)
    {
        ItemComponentTable table = ItemComponentTable.V26_2();
        DataComponentType key = component == "use_remainder"
            ? DataComponents.UseRemainder
            : DataComponents.SulfurCubeContent;
        Assert.True(table.Contains(key), $"{component} is untyped on the 776 table");
        ItemComponentCodec codec = table.ByKey(key);

        var stack = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 3);
        object value = component == "use_remainder"
            ? new UseRemainderComponent(stack)
            : new SulfurCubeContentComponent(stack);

        byte[] bytes = Encode(codec, value);
        Assert.Equal(ItemTestRegistries.Stone, bytes[0]); // item holder id first
        Assert.Equal(3, bytes[1]);                        // then the count

        object decoded = Decode(codec, bytes);
        ItemStack roundTripped = component == "use_remainder"
            ? ((UseRemainderComponent)decoded).ConvertInto
            : ((SulfurCubeContentComponent)decoded).AbsorbedBlockItemStack;
        Assert.Equal(3, roundTripped.Count);
        Assert.Equal(ItemTestRegistries.Stone, roundTripped.Item.NetworkId);
    }

    /// <summary>The two forms really are distinguishable on the wire, which is the whole reason the era split matters: reading a template frame with the count-first reader takes the item id as the count.</summary>
    [Fact]
    public void TheTwoStackFormsDisagree_OnTheSameValue()
    {
        var stack = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 3);

        ItemComponentCodec countFirst = ItemComponentTable.V1_21_5().ByKey(DataComponents.UseRemainder);
        ItemComponentCodec template = ItemComponentTable.V26_2().ByKey(DataComponents.UseRemainder);

        Assert.NotEqual(
            Encode(countFirst, new UseRemainderComponent(stack)),
            Encode(template, new UseRemainderComponent(stack)));
    }

    /// <summary>The component ids exist exactly where their wire contracts define them: <c>use_remainder</c> from 1.21.2 and <c>sulfur_cube_content</c> on protocol 776 only.</summary>
    [Fact]
    public void TheComponentsAreScopedToTheWireLayoutsVanillaHasThem()
    {
        Assert.False(ItemComponentTable.V1_20_5().Contains(DataComponents.UseRemainder));
        Assert.False(ItemComponentTable.V1_21().Contains(DataComponents.UseRemainder));
        Assert.True(ItemComponentTable.V1_21_2().Contains(DataComponents.UseRemainder));
        Assert.True(ItemComponentTable.V1_21_4().Contains(DataComponents.UseRemainder));
        Assert.True(ItemComponentTable.V26_1().Contains(DataComponents.UseRemainder));

        Assert.False(ItemComponentTable.V26_1().Contains(DataComponents.SulfurCubeContent));
        Assert.True(ItemComponentTable.V26_2().Contains(DataComponents.SulfurCubeContent));
    }

    private static byte[] Encode(ItemComponentCodec codec, object value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, value, ItemTestRegistries.Context);
        return buffer.WrittenSpan.ToArray();
    }

    private static object Decode(ItemComponentCodec codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        object value = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return value;
    }
}
