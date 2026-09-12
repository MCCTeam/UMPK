using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Text;
using Xunit;

namespace Umpk.Game.Tests.Items;

public class ItemStackTests
{
    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.True(ItemStack.Empty.IsEmpty);
        Assert.Equal(0, ItemStack.Empty.Count);
    }

    [Fact]
    public void ZeroCount_IsEmpty()
    {
        var stack = ItemTestData.Stack("stone", 0);
        Assert.True(stack.IsEmpty);
    }

    [Fact]
    public void MaxStackSize_ComesFromItemDefinition()
    {
        Assert.Equal(64, ItemTestData.Stack("stone", 1).MaxStackSize);
        Assert.Equal(1, ItemTestData.Stack("diamond_sword", 1).MaxStackSize);
        Assert.Equal(16, ItemTestData.Stack("ender_pearl", 1).MaxStackSize);
    }

    [Fact]
    public void MaxStackSize_ComponentOverridesDefinition()
    {
        var stack = ItemTestData.Stack("stone", 1).With(DataComponents.MaxStackSize, new MaxStackSizeComponent(16));
        Assert.Equal(16, stack.MaxStackSize);
    }

    [Fact]
    public void WithCount_NonPositive_YieldsEmpty()
    {
        Assert.True(ItemTestData.Stack("stone", 5).WithCount(0).IsEmpty);
        Assert.True(ItemTestData.Stack("stone", 5).Shrink(5).IsEmpty);
    }

    [Fact]
    public void Accessors_ReadFromComponents()
    {
        var stack = ItemTestData.Stack("diamond_sword", 1)
            .With(DataComponents.Damage, new DamageComponent(120))
            .With(DataComponents.MaxDamage, new MaxDamageComponent(1561))
            .With(DataComponents.CustomName, new CustomNameComponent(Component.Text("Excalibur")))
            .With(DataComponents.Lore, new LoreComponent([Component.Text("Legendary")]))
            .With(DataComponents.Enchantments, new EnchantmentsComponent(
                [new EnchantmentInstance(ItemTestData.Enchantment("sharpness"), 5)]));

        Assert.Equal(120, stack.Damage);
        Assert.Equal(1561, stack.MaxDamage);
        Assert.Equal("Excalibur", stack.CustomName!.ToPlainText());
        Assert.Single(stack.Lore);
        Assert.Single(stack.Enchantments);
        Assert.Equal(5, stack.Enchantments[0].Level);
    }

    [Fact]
    public void Equality_SameItemCountComponents()
    {
        var a = ItemTestData.Stack("stone", 3).With(DataComponents.Damage, new DamageComponent(1));
        var b = ItemTestData.Stack("stone", 3).With(DataComponents.Damage, new DamageComponent(1));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DiffersOnCount()
    {
        Assert.NotEqual(ItemTestData.Stack("stone", 3), ItemTestData.Stack("stone", 4));
    }

    [Fact]
    public void Equality_DiffersOnComponents()
    {
        var a = ItemTestData.Stack("stone", 1).With(DataComponents.Damage, new DamageComponent(1));
        var b = ItemTestData.Stack("stone", 1).With(DataComponents.Damage, new DamageComponent(2));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AllEmptyStacks_AreEqual()
    {
        Assert.Equal(ItemStack.Empty, ItemTestData.Stack("stone", 0));
        Assert.Equal(0, ItemStack.Empty.GetHashCode());
    }

    [Fact]
    public void IsSameItemSameComponents_IgnoresCount()
    {
        var a = ItemTestData.Stack("stone", 1);
        var b = ItemTestData.Stack("stone", 40);
        Assert.True(a.IsSameItemSameComponents(b));
        Assert.False(a.IsSameItem(ItemTestData.Stack("dirt", 1)));
    }

    [Fact]
    public void IsSameItemSameComponents_FalseWhenComponentsDiffer()
    {
        var a = ItemTestData.Stack("stone", 1);
        var b = ItemTestData.Stack("stone", 1).With(DataComponents.Damage, new DamageComponent(1));
        Assert.False(a.IsSameItemSameComponents(b));
        Assert.True(a.IsSameItem(b));
    }

    [Fact]
    public void Constructor_RejectsUnboundHandle()
    {
        Assert.Throws<ArgumentException>(() => new ItemStack(default, 1));
    }
}
