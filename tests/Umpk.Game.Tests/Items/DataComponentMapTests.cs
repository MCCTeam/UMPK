using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Xunit;

namespace Umpk.Game.Tests.Items;

public class DataComponentMapTests
{
    private static IReadOnlyDictionary<DataComponentType, object> Prototype(params (DataComponentType Type, object Value)[] entries)
    {
        var map = new Dictionary<DataComponentType, object>();
        foreach (var (type, value) in entries)
            map[type] = value;

        return map;
    }

    [Fact]
    public void EmptyMap_HasNoComponents()
    {
        Assert.True(DataComponentMap.Empty.IsEmpty);
        Assert.Equal(0, DataComponentMap.Empty.Count);
        Assert.True(DataComponentMap.Empty.HasNoPatch);
    }

    [Fact]
    public void PrototypeValue_IsVisibleWithoutPatch()
    {
        var map = DataComponentMap.FromPrototype(Prototype((DataComponents.MaxDamage, new MaxDamageComponent(1561))));
        Assert.True(map.TryGet(DataComponents.MaxDamage, out var value));
        Assert.Equal(1561, value.Value);
        Assert.True(map.HasNoPatch);
    }

    [Fact]
    public void Patch_OverridesPrototype()
    {
        var map = DataComponentMap.Create(
            Prototype((DataComponents.Damage, new DamageComponent(0))),
            [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(42))]);

        Assert.True(map.TryGet(DataComponents.Damage, out var value));
        Assert.Equal(42, value.Value);
    }

    [Fact]
    public void RemoveMarker_HidesPrototypeComponent()
    {
        var map = DataComponentMap.Create(
            Prototype((DataComponents.Unbreakable, new UnbreakableComponent())),
            [DataComponentEntry.Remove(DataComponents.Unbreakable)]);

        Assert.False(map.TryGet(DataComponents.Unbreakable, out _));
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public void With_AddsPatchEntry()
    {
        var map = DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(5));
        Assert.True(map.TryGet(DataComponents.Damage, out var value));
        Assert.Equal(5, value.Value);
        Assert.Single(map.Patch);
    }

    [Fact]
    public void With_SettingBackToPrototypeDefault_DropsPatch()
    {
        var proto = Prototype((DataComponents.Damage, new DamageComponent(0)));
        var map = DataComponentMap.FromPrototype(proto)
            .With(DataComponents.Damage, new DamageComponent(7))
            .With(DataComponents.Damage, new DamageComponent(0));

        Assert.True(map.HasNoPatch);
        Assert.Empty(map.Patch);
    }

    [Fact]
    public void Without_OnPrototypeComponent_EmitsRemovalInPatch()
    {
        var map = DataComponentMap.FromPrototype(Prototype((DataComponents.Food, new FoodComponent(4, 2.4f))))
            .Without(DataComponents.Food);

        Assert.False(map.Has(DataComponents.Food));
        var patch = map.Patch;
        Assert.Single(patch);
        Assert.True(patch[0].IsRemoval);
        Assert.Equal(DataComponents.Food, patch[0].Type);
    }

    [Fact]
    public void Without_OnNonPrototypeAddedComponent_JustDropsIt()
    {
        var map = DataComponentMap.Empty
            .With(DataComponents.Damage, new DamageComponent(3))
            .Without(DataComponents.Damage);

        Assert.False(map.Has(DataComponents.Damage));
        Assert.Empty(map.Patch);
    }

    [Fact]
    public void Patch_ExtractionRoundTrips_AddAndRemove()
    {
        var proto = Prototype(
            (DataComponents.MaxStackSize, new MaxStackSizeComponent(64)),
            (DataComponents.Rarity, new RarityComponent("common")));

        var original = DataComponentMap.Create(
            proto,
            [
                DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(9)),
                DataComponentEntry.Remove(DataComponents.Rarity),
            ]);

        // Reconstruct from the extracted patch against the same prototype (what the wire codecs do).
        var rebuilt = DataComponentMap.Create(proto, original.Patch);

        Assert.True(original.EffectiveEquals(rebuilt));
        Assert.True(rebuilt.TryGet(DataComponents.Damage, out var dmg));
        Assert.Equal(9, dmg.Value);
        Assert.False(rebuilt.Has(DataComponents.Rarity));
        Assert.True(rebuilt.Has(DataComponents.MaxStackSize));
    }

    [Fact]
    public void EffectiveView_MergesPrototypeAndPatchAdditions()
    {
        var map = DataComponentMap.Create(
            Prototype((DataComponents.MaxStackSize, new MaxStackSizeComponent(64))),
            [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(1))]);

        Assert.Equal(2, map.Count);
        Assert.True(map.Has(DataComponents.MaxStackSize));
        Assert.True(map.Has(DataComponents.Damage));
    }

    [Fact]
    public void EffectiveEquals_IgnoresHowSplitWasProduced()
    {
        // Same effective view: one via prototype, one via patch.
        var viaPrototype = DataComponentMap.FromPrototype(Prototype((DataComponents.Damage, new DamageComponent(5))));
        var viaPatch = DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(5));

        Assert.True(viaPrototype.EffectiveEquals(viaPatch));
        Assert.True(viaPatch.EffectiveEquals(viaPrototype));
    }

    [Fact]
    public void Create_DropsSetEntriesEqualToPrototypeDefault()
    {
        // A value equal to the prototype default is redundant and omitted from the patch.
        var proto = Prototype((DataComponents.Damage, new DamageComponent(7)));
        var map = DataComponentMap.Create(
            proto,
            [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(7))]);

        Assert.True(map.HasNoPatch);
        Assert.Empty(map.Patch);
        Assert.True(map.TryGet(DataComponents.Damage, out var value));
        Assert.Equal(7, value.Value);
    }

    [Fact]
    public void Create_DropsRemovalsForTypesAbsentFromPrototype()
    {
        // Removing a type absent from the prototype is redundant and omitted from the patch.
        var map = DataComponentMap.Create(
            Prototype((DataComponents.MaxStackSize, new MaxStackSizeComponent(64))),
            [DataComponentEntry.Remove(DataComponents.Damage)]);

        Assert.True(map.HasNoPatch);
        Assert.Empty(map.Patch);
    }

    [Fact]
    public void Equals_IsStructural_PrototypePlusPatch()
    {
        var protoA = Prototype((DataComponents.MaxStackSize, new MaxStackSizeComponent(64)));
        var protoB = Prototype((DataComponents.MaxStackSize, new MaxStackSizeComponent(64)));

        var left = DataComponentMap.Create(protoA, [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(3))]);
        var right = DataComponentMap.Create(protoB, [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(3))]);

        Assert.True(left.Equals(right));
        Assert.True(right.Equals(left));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());

        var differentPatch = DataComponentMap.Create(protoA, [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(4))]);
        Assert.False(left.Equals(differentPatch));
    }

    [Fact]
    public void Equals_DistinguishesSplit_WhereEffectiveEqualsDoesNot()
    {
        // Same effective view, different prototype/patch split: structurally different (vanilla equals compares prototype AND patch), but effective-equal (the ItemStack path).
        var viaPrototype = DataComponentMap.FromPrototype(Prototype((DataComponents.Damage, new DamageComponent(5))));
        var viaPatch = DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(5));

        Assert.True(viaPrototype.EffectiveEquals(viaPatch));
        Assert.False(viaPrototype.Equals(viaPatch));
        Assert.False(viaPatch.Equals(viaPrototype));
    }

    [Fact]
    public void Equals_ComparesRemovalMarkers()
    {
        var proto = Prototype((DataComponents.Unbreakable, new UnbreakableComponent()));
        var removed = DataComponentMap.Create(proto, [DataComponentEntry.Remove(DataComponents.Unbreakable)]);
        var untouched = DataComponentMap.FromPrototype(proto);

        Assert.False(removed.Equals(untouched));
        Assert.True(removed.Equals(DataComponentMap.Create(proto, [DataComponentEntry.Remove(DataComponents.Unbreakable)])));
    }

    [Fact]
    public void HashSet_Usage_DeduplicatesEqualMaps()
    {
        var proto = Prototype((DataComponents.MaxStackSize, new MaxStackSizeComponent(64)));
        var set = new HashSet<DataComponentMap>
        {
            DataComponentMap.Create(proto, [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(1))]),
            DataComponentMap.Create(proto, [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(1))]),
            DataComponentMap.Create(proto, [DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(2))]),
        };

        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void CanonicalizedCreate_MakesEqualEffectiveMapsEqual()
    {
        // A non-canonical wire patch (set-to-default + stray removal) must produce a map equal to the plain prototype map, because Create canonicalizes both away.
        var proto = Prototype((DataComponents.Damage, new DamageComponent(0)));
        var nonCanonical = DataComponentMap.Create(
            proto,
            [
                DataComponentEntry.Set(DataComponents.Damage, new DamageComponent(0)),
                DataComponentEntry.Remove(DataComponents.Food),
            ]);
        var plain = DataComponentMap.FromPrototype(proto);

        Assert.True(nonCanonical.Equals(plain));
        Assert.Equal(plain.GetHashCode(), nonCanonical.GetHashCode());
    }
}
