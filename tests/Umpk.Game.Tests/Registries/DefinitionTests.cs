using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Game.Tests.Registries;

public class DefinitionTests
{
    [Fact]
    public void BlockDefinition_StateCountAndOwnsState()
    {
        var def = new BlockDefinition(45, 84, 50);
        Assert.Equal(40, def.StateCount);
        Assert.True(def.OwnsState(45));
        Assert.True(def.OwnsState(84));
        Assert.True(def.OwnsState(50));
        Assert.False(def.OwnsState(44));
        Assert.False(def.OwnsState(85));
    }

    [Fact]
    public void BlockDefinition_SingleState_HasCountOne()
    {
        var def = new BlockDefinition(1, 1, 1);
        Assert.Equal(1, def.StateCount);
        Assert.True(def.OwnsState(1));
    }

    [Fact]
    public void DimensionTypeDefinition_MaxY()
    {
        var def = new DimensionTypeDefinition(-64, 384, true);
        Assert.Equal(320, def.MaxY);
        Assert.True(def.HasSkylight);
    }

    [Fact]
    public void SimpleDefinitions_CarryTheirValues()
    {
        Assert.Equal(16, new ItemDefinition(16).MaxStackSize);
        Assert.Equal(64, new ItemDefinition().MaxStackSize);
        Assert.Equal(3, new EnchantmentDefinition(3).MaxLevel);
        Assert.Equal(1, new EnchantmentDefinition().MaxLevel);

        var entity = new EntityTypeDefinition(0.6f, 1.8f);
        Assert.Equal(0.6f, entity.Width);
        Assert.Equal(1.8f, entity.Height);

        var attr = new AttributeDefinition(20, 0, 1024);
        Assert.Equal(20, attr.DefaultValue);
        Assert.Equal(0, attr.MinValue);
        Assert.Equal(1024, attr.MaxValue);
    }
}
