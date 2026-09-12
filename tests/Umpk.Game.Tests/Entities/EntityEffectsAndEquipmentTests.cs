using Umpk.Game.Entities;
using Xunit;

namespace Umpk.Game.Tests.Entities;

public sealed class EntityEffectsAndEquipmentTests
{
    private sealed record FakeSlot(bool IsEmpty) : IMetadataSlot;

    [Fact]
    public void AddEffect_Then_Refresh_Replaces_Same_Id()
    {
        var e = EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie);
        var speed = EntityTestFixtures.Effects[1];

        bool replacedFirst = e.AddOrRefreshEffect(new EffectInstance(speed, 0, 200, EffectFlags.ShowParticles));
        bool replacedSecond = e.AddOrRefreshEffect(new EffectInstance(speed, 2, 600, EffectFlags.ShowIcon));

        Assert.False(replacedFirst);
        Assert.True(replacedSecond);
        Assert.Single(e.Effects);
        Assert.True(e.TryGetEffect(speed.NetworkId, out EffectInstance? effect));
        Assert.Equal(2, effect.Amplifier);
        Assert.Equal(3, effect.Level);
        Assert.Equal(600, effect.Duration);
    }

    [Fact]
    public void RemoveEffect_Removes_By_Network_Id()
    {
        var e = EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie);
        var slowness = EntityTestFixtures.Effects[2];
        e.AddOrRefreshEffect(new EffectInstance(slowness, 0, 100, EffectFlags.None));

        Assert.True(e.RemoveEffect(slowness.NetworkId));
        Assert.False(e.RemoveEffect(slowness.NetworkId));
        Assert.Empty(e.Effects);
    }

    [Fact]
    public void EffectFlags_Decode_Predicates()
    {
        var speed = EntityTestFixtures.Effects[1];
        var effect = new EffectInstance(speed, 0, -1, EffectFlags.Ambient | EffectFlags.ShowIcon);

        Assert.True(effect.IsAmbient);
        Assert.False(effect.ShowParticles);
        Assert.True(effect.ShowIcon);
        Assert.True(effect.IsInfinite);
    }

    [Fact]
    public void Equipment_Slots_Set_And_Get()
    {
        var e = EntityTestFixtures.NewEntity(1, EntityTestFixtures.Player);
        var helmet = new FakeSlot(false);

        e.SetEquipment(EquipmentSlot.Head, helmet);
        e.SetEquipment(EquipmentSlot.MainHand, null);

        Assert.True(e.TryGetEquipment(EquipmentSlot.Head, out IMetadataSlot? head));
        Assert.Same(helmet, head);
        Assert.True(e.TryGetEquipment(EquipmentSlot.MainHand, out IMetadataSlot? hand));
        Assert.Null(hand);
        Assert.False(e.TryGetEquipment(EquipmentSlot.Feet, out _));
    }

    [Fact]
    public void Equipment_All_Six_Vanilla_Slots_Addressable()
    {
        var e = EntityTestFixtures.NewEntity(1, EntityTestFixtures.Player);
        foreach (EquipmentSlot slot in new[]
        {
            EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Feet,
            EquipmentSlot.Legs, EquipmentSlot.Chest, EquipmentSlot.Head,
        })
            e.SetEquipment(slot, new FakeSlot(false));

        Assert.Equal(6, e.Equipment.Count);
    }
}
