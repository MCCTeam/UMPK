using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Xunit;

namespace Umpk.Game.Tests.Items;

public sealed class MinimumAttackChargeTests
{
    // Native ItemStack.isSameItemSameComponents measurements agree across 1.21.11, 26.1, 26.1.1, 26.1.2, 26.2 and 26.3.
    [Theory]
    [InlineData(0, int.MinValue, false)]
    [InlineData(int.MinValue, 0, false)]
    [InlineData(0x7FC00001, 0x7FC00002, true)]
    [InlineData(0x7FC00001, unchecked((int)0xFFC00002), true)]
    [InlineData(0x3F800000, 0x3F800000, true)]
    [InlineData(0x3F800000, 0, false)]
    public void NativeComponentEqualityControlsStackComparisonAndPatchMinimization(int leftBits, int rightBits, bool same)
    {
        var left = new MinimumAttackChargeComponent(BitConverter.Int32BitsToSingle(leftBits));
        var right = new MinimumAttackChargeComponent(BitConverter.Int32BitsToSingle(rightBits));
        ItemStack first = ItemTestData.Stack("stone", 1).With(DataComponents.MinimumAttackCharge, left);
        ItemStack second = ItemTestData.Stack("stone", 1).With(DataComponents.MinimumAttackCharge, right);

        Assert.Equal(same, left.Equals(right));
        Assert.Equal(same, ((object)left).Equals(right));
        Assert.Equal(same, left == right);
        Assert.Equal(same, first.IsSameItemSameComponents(second));
        if (same)
            Assert.Equal(left.GetHashCode(), right.GetHashCode());

        DataComponentMap prototype = DataComponentMap.FromPrototype(new Dictionary<DataComponentType, object>
        {
            [DataComponents.MinimumAttackCharge] = left,
        });
        DataComponentMap patched = prototype.With(DataComponents.MinimumAttackCharge, right);
        Assert.Equal(same, patched.HasNoPatch);
        Assert.Equal(same ? leftBits : rightBits, BitConverter.SingleToInt32Bits(patched.Get(DataComponents.MinimumAttackCharge)!.Value));
    }
}
