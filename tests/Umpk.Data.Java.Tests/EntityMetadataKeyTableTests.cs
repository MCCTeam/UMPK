using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>The per-era tier-2 entity-metadata index table. Every key here is declared on vanilla's <c>Entity</c> or <c>LivingEntity</c>, so its index is the same for every entity type on a given era and moves only where the base classes gained a field. There are exactly six such boundaries.</summary>
/// <remarks>Ground truth per band is quoted on <see cref="JavaEntityMetadataKeys"/>. The table matters because a wrong index is not a crash: the tier-2 accessor treats a kind mismatch as a graceful miss, so an off-by-one silently reports "the server sent nothing" for the custom name on every entity.</remarks>
public sealed class EntityMetadataKeyTableTests
{
    private static int Index(int protocol, MetadataKey key)
    {
        Assert.True(
            JavaGameData.EntityMetadataKeys(protocol).TryResolveIndex(default, key, out int index),
            $"{key.Name} does not resolve at protocol {protocol}");
        return index;
    }

    private static bool Resolves(int protocol, MetadataKey key) =>
        JavaGameData.EntityMetadataKeys(protocol).TryResolveIndex(default, key, out _);

    /// <summary>The base-<c>Entity</c> block, identical on every era: flags, air, name, visible, silent.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(210)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(758)]
    [InlineData(763)]
    [InlineData(770)]
    [InlineData(776)]
    public void TheSharedEntityBlock_IsAlwaysZeroThroughFour(int protocol)
    {
        Assert.Equal(0, Index(protocol, EntityMetadataKeys.SharedFlags));
        Assert.Equal(1, Index(protocol, EntityMetadataKeys.AirSupply));
        Assert.Equal(2, Index(protocol, EntityMetadataKeys.CustomName));
        Assert.Equal(3, Index(protocol, EntityMetadataKeys.CustomNameVisible));
        Assert.Equal(4, Index(protocol, EntityMetadataKeys.Silent));
    }

    /// <summary>The three fields that arrive later, each pushing everything below it down by one: no-gravity at 1.10, pose at 1.14, ticks-frozen at 1.17.</summary>
    [Theory]
    [InlineData(47, false, false, false)]
    [InlineData(107, false, false, false)]
    [InlineData(110, false, false, false)]
    [InlineData(210, true, false, false)]
    [InlineData(340, true, false, false)]
    [InlineData(393, true, false, false)]
    [InlineData(404, true, false, false)]
    [InlineData(477, true, true, false)]
    [InlineData(578, true, true, false)]
    [InlineData(735, true, true, false)]
    [InlineData(754, true, true, false)]
    [InlineData(755, true, true, true)]
    [InlineData(776, true, true, true)]
    public void TheLateArrivals_ExistOnlyFromTheirOwnBand(
        int protocol, bool noGravity, bool pose, bool ticksFrozen)
    {
        Assert.Equal(noGravity, Resolves(protocol, EntityMetadataKeys.NoGravity));
        Assert.Equal(pose, Resolves(protocol, EntityMetadataKeys.Pose));
        Assert.Equal(ticksFrozen, Resolves(protocol, EntityMetadataKeys.TicksFrozen));

        if (noGravity)
            Assert.Equal(5, Index(protocol, EntityMetadataKeys.NoGravity));

        if (pose)
            Assert.Equal(6, Index(protocol, EntityMetadataKeys.Pose));

        if (ticksFrozen)
            Assert.Equal(7, Index(protocol, EntityMetadataKeys.TicksFrozen));

    }

    /// <summary>Health is the one <c>LivingEntity</c> key, so it sits one past the living flags byte and shifts with every base-class insertion above it. Its 8 and 9 are independently corroborated by the recorded 1.16.x and 1.17+ corpus frames asserted in <c>EntityMetadataApplierTests</c>.</summary>
    [Theory]
    [InlineData(47, 6)]
    [InlineData(107, 6)]
    [InlineData(110, 6)]
    [InlineData(210, 7)]
    [InlineData(340, 7)]
    [InlineData(393, 7)]
    [InlineData(404, 7)]
    [InlineData(477, 8)]
    [InlineData(578, 8)]
    [InlineData(735, 8)]
    [InlineData(754, 8)]
    [InlineData(755, 9)]
    [InlineData(758, 9)]
    [InlineData(763, 9)]
    [InlineData(770, 9)]
    [InlineData(774, 9)]
    [InlineData(776, 9)]
    public void Health_SitsOnePastTheLivingFlagsByte(int protocol, int expected) =>
        Assert.Equal(expected, Index(protocol, EntityMetadataKeys.Health));

    [Theory]
    [InlineData(47, 10)]
    [InlineData(107, 5)]
    [InlineData(210, 6)]
    [InlineData(477, 7)]
    [InlineData(755, 8)]
    [InlineData(776, 8)]
    public void DroppedItem_CarriedStackUsesItsEntityClassIndex(int protocol, int expected)
    {
        RegistryEntry<EntityTypeDefinition> item = Registry.Direct(
            1,
            Identifier.Minecraft("item"),
            new EntityTypeDefinition(0.25f, 0.25f));
        RegistryEntry<EntityTypeDefinition> zombie = Registry.Direct(
            2,
            Identifier.Minecraft("zombie"),
            new EntityTypeDefinition(1.95f, 0.6f));

        Assert.True(JavaGameData.EntityMetadataKeys(protocol)
            .TryResolveIndex(item, EntityMetadataKeys.CarriedItem, out int actual));
        Assert.Equal(expected, actual);
        Assert.False(JavaGameData.EntityMetadataKeys(protocol)
            .TryResolveIndex(zombie, EntityMetadataKeys.CarriedItem, out _));
    }

    /// <summary>The source is cached per protocol, as the block-shape source is.</summary>
    [Fact]
    public void TheSource_IsCachedPerProtocol() =>
        Assert.Same(JavaGameData.EntityMetadataKeys(770), JavaGameData.EntityMetadataKeys(770));

    /// <summary>Two different eras must not share a table.</summary>
    [Fact]
    public void TwoWireLayoutsDoNotShareATable()
    {
        Assert.True(JavaGameData.EntityMetadataKeys(754).TryResolveIndex(default, EntityMetadataKeys.Health, out int old));
        Assert.True(JavaGameData.EntityMetadataKeys(755).TryResolveIndex(default, EntityMetadataKeys.Health, out int now));
        Assert.NotEqual(old, now);
    }
}
