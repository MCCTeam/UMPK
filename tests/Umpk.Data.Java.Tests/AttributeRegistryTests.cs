using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Pins the runtime <c>minecraft:attribute</c> registry and its protocol-specific value ranges.</summary>
/// <remarks>
/// <para>From 1.20.5 onward the packet identifies attributes by holder VarInt, so each populated registry must preserve the protocol's numeric ids and value ranges.</para>
/// <para>The registry is keyed by the raw dataset name (<c>minecraft:generic.movement_speed</c> on 766/767, <c>minecraft:movement_speed</c> from 768), never by a canonicalised one. See <see cref="HorseAndGenericJumpStrength_AreSeparateRows_WithDifferentRanges"/> for the era collision that requires distinct data rows; canonicalisation happens only at lookup, in <c>Umpk.Game.Entities.AttributeIds</c>.</para>
/// </remarks>
public sealed class AttributeRegistryTests
{
    private static Registry<AttributeDefinition> Attributes(int protocol) =>
        JavaGameData.Registries(protocol).Attributes;

    private static IEnumerable<int> AllProtocols() =>
        JavaVersions.All.Select(v => v.Version.Protocol).Distinct();

    /// <summary>Protocols below 735 have no attribute registry and name attributes by string. Later protocols carry the protocol-specific entry counts asserted here.</summary>
    [Theory]
    [InlineData(47, 0)]
    [InlineData(340, 0)]
    [InlineData(578, 0)]
    [InlineData(735, 13)]
    [InlineData(764, 14)]
    [InlineData(765, 14)]
    [InlineData(766, 22)]
    [InlineData(767, 31)]
    [InlineData(768, 32)]
    [InlineData(774, 35)]
    [InlineData(776, 40)]
    public void AttributeRegistry_IsPopulated_OnEveryProtocolWhoseDatasetCarriesOne(int protocol, int expected)
        => Assert.Equal(expected, Attributes(protocol).Count);

    /// <summary>The movement-speed holder ids and its valid value range.</summary>
    [Theory]
    [InlineData(735, "minecraft:generic.movement_speed", 3)]
    [InlineData(766, "minecraft:generic.movement_speed", 17)]
    [InlineData(767, "minecraft:generic.movement_speed", 21)]
    [InlineData(768, "minecraft:movement_speed", 21)]
    [InlineData(774, "minecraft:movement_speed", 22)]
    [InlineData(776, "minecraft:movement_speed", 26)]
    public void AttributeRegistry_MovementSpeed_CarriesVanillaRange(int protocol, string rawName, int holderId)
    {
        Assert.True(Attributes(protocol).TryGet(Identifier.Parse(rawName), out RegistryEntry<AttributeDefinition> entry));
        Assert.Equal(holderId, entry.NetworkId);
        Assert.Equal(0.7, entry.Value.DefaultValue);
        Assert.Equal(0.0, entry.Value.MinValue);
        Assert.Equal(1024.0, entry.Value.MaxValue);
        Assert.True(entry.Value.IsRanged);
    }

    /// <summary>
    /// Raw names keep the two jump-strength definitions separate across their era boundary. <c>minecraft:horse.jump_strength</c> and <c>minecraft:generic.jump_strength</c> both canonicalise to <c>minecraft:jump_strength</c> and carry different defaults AND different ranges. <c>AttributeInstance</c> consumes both numbers (it seeds <c>BaseValue</c> from the default and clamps into <c>[Min,Max]</c>), so a canonically-keyed table would emit one of these two rows incorrectly on every protocol of the other era.
    /// <para>Earlier layouts use (<c>"horse.jump_strength", RangedAttribute(..., 0.7, 0.0, 2.0)</c>) and later layouts use the updated value. (<c>"generic.jump_strength", RangedAttribute(..., 0.42F, 0.0, 32.0)</c>).</para>
    /// </summary>
    [Fact]
    public void HorseAndGenericJumpStrength_AreSeparateRows_WithDifferentRanges()
    {
        Assert.True(Attributes(735).TryGet(Identifier.Minecraft("horse.jump_strength"), out RegistryEntry<AttributeDefinition> horse));
        Assert.Equal(0.7, horse.Value.DefaultValue);
        Assert.Equal(0.0, horse.Value.MinValue);
        Assert.Equal(2.0, horse.Value.MaxValue);

        Assert.True(Attributes(766).TryGet(Identifier.Minecraft("generic.jump_strength"), out RegistryEntry<AttributeDefinition> generic));
        Assert.Equal(0.42, generic.Value.DefaultValue);
        Assert.Equal(0.0, generic.Value.MinValue);
        Assert.Equal(32.0, generic.Value.MaxValue);

        // And the era boundary is real: 735 has ONLY the horse row, 766 has ONLY the generic one.
        Assert.False(Attributes(735).TryGet(Identifier.Minecraft("generic.jump_strength"), out _));
        Assert.False(Attributes(766).TryGet(Identifier.Minecraft("horse.jump_strength"), out _));
    }

    /// <summary>The 767 era gate this item reads instead of a protocol literal: 766's table has neither <c>movement_efficiency</c> nor <c>sneaking_speed</c>, 767's has both plus <c>water_movement_efficiency</c>. That IS the 1.20.6/1.21 behavioural boundary, expressed by the data rather than by an <c>if (protocol &gt;= 767)</c> in engine code.</summary>
    [Theory]
    [InlineData(766, "minecraft:generic.movement_efficiency", false)]
    [InlineData(766, "minecraft:player.sneaking_speed", false)]
    [InlineData(766, "minecraft:generic.water_movement_efficiency", false)]
    [InlineData(767, "minecraft:generic.movement_efficiency", true)]
    [InlineData(767, "minecraft:player.sneaking_speed", true)]
    [InlineData(767, "minecraft:generic.water_movement_efficiency", true)]
    [InlineData(774, "minecraft:movement_efficiency", true)]
    [InlineData(774, "minecraft:sneaking_speed", true)]
    public void AttributeWireLayoutGate_FallsOutOfTheDataset(int protocol, string rawName, bool present)
        => Assert.Equal(present, Attributes(protocol).TryGet(Identifier.Parse(rawName), out _));

    /// <summary>Holder ids for the three movement attributes, pinned to their protocol-specific registry ids.</summary>
    [Theory]
    [InlineData(767, "minecraft:generic.movement_efficiency", 20)]
    [InlineData(767, "minecraft:player.sneaking_speed", 25)]
    [InlineData(767, "minecraft:generic.water_movement_efficiency", 30)]
    [InlineData(774, "minecraft:movement_efficiency", 21)]
    [InlineData(774, "minecraft:sneaking_speed", 26)]
    [InlineData(774, "minecraft:water_movement_efficiency", 32)]
    public void AttributeRegistry_HolderIds_MatchTheReport(int protocol, string rawName, int holderId)
    {
        Assert.True(Attributes(protocol).TryGet(Identifier.Parse(rawName), out RegistryEntry<AttributeDefinition> entry));
        Assert.Equal(holderId, entry.NetworkId);
    }

    /// <summary>No protocol may carry a zero-filled attribute definition because a <c>[0,0]</c> range would clamp every value to zero.</summary>
    [Fact]
    public void NoAttribute_IsAZeroFilledPlaceholder()
    {
        foreach (int protocol in AllProtocols())
            foreach (RegistryEntry<AttributeDefinition> entry in Attributes(protocol))
            {
                AttributeDefinition d = entry.Value;
                Assert.False(
                    d.MinValue == 0.0 && d.MaxValue == 0.0,
                    $"proto {protocol}: {entry.Id} has a degenerate [0,0] range");
                Assert.InRange(d.DefaultValue, d.MinValue, d.MaxValue);
            }

    }
}
