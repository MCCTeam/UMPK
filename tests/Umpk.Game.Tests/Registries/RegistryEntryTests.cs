using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Game.Tests.Registries;

public class RegistryEntryTests
{
    private static Registry<EnchantmentDefinition> Build() =>
        new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment, 2)
            .Add(0, Identifier.Minecraft("sharpness"), new EnchantmentDefinition(5))
            .Add(1, Identifier.Minecraft("unbreaking"), new EnchantmentDefinition(3))
            .Build();

    [Fact]
    public void Handle_CarriesIdKeyAndValue()
    {
        var registry = Build();
        var entry = registry[0];
        Assert.Equal(0, entry.NetworkId);
        Assert.Equal(Identifier.Minecraft("sharpness"), entry.Id);
        Assert.Equal(5, entry.Value.MaxLevel);
    }

    [Fact]
    public void SameEntry_IsEqualAndSameHash()
    {
        var registry = Build();
        var a = registry[0];
        var b = registry[Identifier.Minecraft("sharpness")];
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentEntries_AreNotEqual()
    {
        var registry = Build();
        Assert.NotEqual(registry[0], registry[1]);
        Assert.True(registry[0] != registry[1]);
    }

    [Fact]
    public void DefaultHandle_ThrowsOnValueAndIsDefault()
    {
        RegistryEntry<EnchantmentDefinition> def = default;
        Assert.True(def.IsDefault);
        Assert.Throws<InvalidOperationException>(() => def.Value);
        Assert.Equal("<unbound>", def.ToString());
    }

    [Fact]
    public void Handle_ComparesByNetworkIdAndKey()
    {
        // Handles from independently built registries with the same slot resolve to distinct value references, so they are not equal even with matching id/key.
        var first = Build()[0];
        var second = Build()[0];
        Assert.NotEqual(first, second);
    }
}
