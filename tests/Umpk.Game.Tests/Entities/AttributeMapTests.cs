using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Game.Tests.Entities;

public sealed class AttributeMapTests
{
    private static RegistryEntry<AttributeDefinition> MaxHealth => EntityTestFixtures.Attributes[0];

    private static RegistryEntry<AttributeDefinition> Speed => EntityTestFixtures.Attributes[1];

    [Fact]
    public void Base_Value_Seeds_From_Definition_Default()
    {
        var map = new AttributeMap();
        AttributeInstance instance = map.GetOrCreate(MaxHealth);

        Assert.Equal(20.0, instance.BaseValue);
        Assert.Equal(20.0, instance.Value);
    }

    [Fact]
    public void AddValue_Then_MultipliedBase_Then_MultipliedTotal_Vanilla_Order()
    {
        // base 20; +add_value 5 -> 25; +multiplied_base 0.1 (of 25) -> 25 + 2.5 = 27.5;
        // *multiplied_total (1 + 0.2) -> 33.0
        var instance = new AttributeInstance(MaxHealth) { BaseValue = 20 };
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("add"), 5, AttributeModifierOperation.AddValue));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("mb"), 0.1, AttributeModifierOperation.AddMultipliedBase));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("mt"), 0.2, AttributeModifierOperation.AddMultipliedTotal));

        Assert.Equal(33.0, instance.Value, 6);
    }

    [Fact]
    public void MultipliedBase_Uses_Post_AddValue_Base_For_Each_Modifier()
    {
        // base 10; two add_value +5 each -> 20; two multiplied_base 0.5 each accumulate on 20: 20 + 20*0.5 + 20*0.5 = 40
        var instance = new AttributeInstance(Speed) { BaseValue = 10 };
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("a1"), 5, AttributeModifierOperation.AddValue));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("a2"), 5, AttributeModifierOperation.AddValue));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("b1"), 0.5, AttributeModifierOperation.AddMultipliedBase));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("b2"), 0.5, AttributeModifierOperation.AddMultipliedBase));

        // Speed max is 1024 so no clamp interference.
        Assert.Equal(40.0, instance.Value, 6);
    }

    [Fact]
    public void MultipliedTotal_Compounds()
    {
        // base 10; two multiplied_total 1.0 each: 10 * 2 * 2 = 40
        var instance = new AttributeInstance(Speed) { BaseValue = 10 };
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("t1"), 1.0, AttributeModifierOperation.AddMultipliedTotal));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("t2"), 1.0, AttributeModifierOperation.AddMultipliedTotal));

        Assert.Equal(40.0, instance.Value, 6);
    }

    [Fact]
    public void Value_Clamps_To_Definition_Max()
    {
        // Speed max is 1024; drive way over.
        var instance = new AttributeInstance(Speed) { BaseValue = 2000 };
        Assert.Equal(1024.0, instance.Value, 6);
    }

    [Fact]
    public void Value_Clamps_To_Definition_Min()
    {
        // max_health min is 1; a negative add_value drives it under.
        var instance = new AttributeInstance(MaxHealth) { BaseValue = 20 };
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("drain"), -100, AttributeModifierOperation.AddValue));
        Assert.Equal(1.0, instance.Value, 6);
    }

    [Fact]
    public void Modifier_Is_Deduplicated_By_Id()
    {
        var instance = new AttributeInstance(Speed) { BaseValue = 0 };
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("dup"), 5, AttributeModifierOperation.AddValue));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("dup"), 9, AttributeModifierOperation.AddValue));

        Assert.Single(instance.Modifiers);
        Assert.Equal(9.0, instance.Value, 6);
    }

    [Fact]
    public void RemoveModifier_Restores_Value()
    {
        var instance = new AttributeInstance(Speed) { BaseValue = 1 };
        var id = Identifier.Minecraft("boost");
        instance.AddOrReplaceModifier(new AttributeModifier(id, 3, AttributeModifierOperation.AddValue));
        Assert.Equal(4.0, instance.Value, 6);

        Assert.True(instance.RemoveModifier(id));
        Assert.Equal(1.0, instance.Value, 6);
    }

    [Fact]
    public void GetOrCreate_Returns_Same_Instance()
    {
        var map = new AttributeMap();
        AttributeInstance first = map.GetOrCreate(Speed);
        AttributeInstance second = map.GetOrCreate(Speed);

        Assert.Same(first, second);
        Assert.Equal(1, map.Count);
    }

    [Fact]
    public void GetOrCreate_On_Default_Handle_Throws()
    {
        var map = new AttributeMap();
        Assert.Throws<ArgumentException>(() => map.GetOrCreate(default));
    }

    // sanitizeValue semantics (NaN and non-ranged pass-through)

    private static RegistryEntry<AttributeDefinition> BuildAttribute(string path, AttributeDefinition definition) =>
        new RegistryBuilder<AttributeDefinition>(RegistryIds.Attribute)
            .Add(0, Identifier.Minecraft(path), definition)
            .Build()[0];

    [Fact]
    public void NaN_Value_Clamps_To_Min_For_Ranged_Attribute()
    {
        // Vanilla sanitize value behavior: isNaN(value) ? minValue: clamp The range is preserved.
        var instance = new AttributeInstance(MaxHealth) { BaseValue = double.NaN };
        Assert.Equal(1.0, instance.Value, 6); // max_health min is 1

        // NaN produced by modifier math, not just the base, sanitizes the same way.
        var infected = new AttributeInstance(MaxHealth) { BaseValue = double.PositiveInfinity };
        infected.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("neg"), double.NegativeInfinity, AttributeModifierOperation.AddValue));
        Assert.Equal(1.0, infected.Value, 6);
    }

    [Fact]
    public void NonRanged_Attribute_Does_Not_Clamp()
    {
        // The base sanitization is the identity: no clamping even when min/max fields carry values.
        var plain = BuildAttribute("plain", new AttributeDefinition(10, 0, 100, IsRanged: false));
        var instance = new AttributeInstance(plain) { BaseValue = 5000 };
        Assert.Equal(5000.0, instance.Value, 6);

        instance.BaseValue = -5000;
        Assert.Equal(-5000.0, instance.Value, 6);
    }

    [Fact]
    public void NonRanged_Attribute_Passes_NaN_Through()
    {
        var plain = BuildAttribute("plain", new AttributeDefinition(10, 0, 100, IsRanged: false));
        var instance = new AttributeInstance(plain) { BaseValue = double.NaN };
        Assert.True(double.IsNaN(instance.Value));
    }

    /// <summary><c>ValueExcluding</c> leaves one modifier out and resolves everything else exactly as <c>Value</c> does. The concrete consumer is the physics layer: it applies the sprint <c>minecraft:sprinting</c> multiply itself, from the tick's input, so the value pushed into <c>PhysicsConditions.BaseMovementSpeedAttribute</c> has to be read through this. A server echoes its own copy of that modifier back over <c>ClientboundUpdateAttributesPacket</c>, so reading <c>Value</c> there is a 1.69x overspeed rather than a 1.3x one.</summary>
    [Fact]
    public void ValueExcluding_LeavesTheNamedModifierOut_AndKeepsTheRest()
    {
        var instance = new AttributeInstance(Speed) { BaseValue = 0.1 };
        instance.AddOrReplaceModifier(new AttributeModifier(
            Identifier.Minecraft("sprinting"), 0.30000001192092896, AttributeModifierOperation.AddMultipliedTotal));
        instance.AddOrReplaceModifier(new AttributeModifier(
            Identifier.Minecraft("soul_speed"), 0.03, AttributeModifierOperation.AddValue));

        // With everything: (0.1 + 0.03) * 1.30000001192... ; without sprinting: 0.13 flat.
        Assert.Equal(0.13 * 1.30000001192092896, instance.Value, 12);
        Assert.Equal(0.13, instance.ValueExcluding(Identifier.Minecraft("sprinting")), 12);

        // Excluding an id no modifier carries is exactly Value.
        Assert.Equal(instance.Value, instance.ValueExcluding(Identifier.Minecraft("nothing")), 15);
    }

    /// <summary>The exclusion happens in every operation phase, not only the multiply one, and it does not disturb the vanilla ordering: an excluded ADD_VALUE must also be missing from the base that ADD_MULTIPLIED_BASE is taken against.</summary>
    [Fact]
    public void ValueExcluding_AppliesToEveryOperationPhase()
    {
        var instance = new AttributeInstance(Speed) { BaseValue = 10 };
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("a"), 10, AttributeModifierOperation.AddValue));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("b"), 0.5, AttributeModifierOperation.AddMultipliedBase));
        instance.AddOrReplaceModifier(new AttributeModifier(Identifier.Minecraft("c"), 1.0, AttributeModifierOperation.AddMultipliedTotal));

        // All three: (10 + 10) + 20*0.5 = 30, then *2 = 60.
        Assert.Equal(60.0, instance.Value, 6);

        // Without "a" the multiplied_base is taken against 10, not 20: (10 + 5) * 2 = 30.
        Assert.Equal(30.0, instance.ValueExcluding(Identifier.Minecraft("a")), 6);

        // Without "b": 20 * 2 = 40. Without "c": 30.
        Assert.Equal(40.0, instance.ValueExcluding(Identifier.Minecraft("b")), 6);
        Assert.Equal(30.0, instance.ValueExcluding(Identifier.Minecraft("c")), 6);
    }
}
