using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Pins the runtime <c>minecraft:enchantment</c> and <c>minecraft:mob_effect</c> registries that <see cref="JavaGameData"/> builds from the generated per-version identity tables.</summary>
/// <remarks>
/// <para>These tests assert literal wire ids so the expected values remain independent of the generated identity tables.</para>
/// <para>Both registries are intentionally empty on some protocols, for different compatibility reasons: see <see cref="EnchantmentRegistryIsPopulatedExactlyWhereTheReportExists"/> and <see cref="MobEffectRegistryIsPopulatedExactlyWhereTheReportExists"/>.</para>
/// </remarks>
public sealed class EnchantmentAndMobEffectRegistryTests
{
    private static Registry<EnchantmentDefinition> Enchantments(int protocol) =>
        JavaGameData.Registries(protocol).Enchantments;

    private static Registry<MobEffectDefinition> MobEffects(int protocol) =>
        JavaGameData.Registries(protocol).MobEffects;

    private static IEnumerable<int> AllProtocols() =>
        JavaVersions.All.Select(v => v.Version.Protocol).Distinct();

    /// <summary>
    /// The enchantment registry is populated on protocols 477-766 and empty elsewhere.
    /// <para>Earlier item codecs carry enchantments as NBT and require no registry lookup.</para>
    /// <para>Protocol 767 and later synchronize enchantments during configuration. Static data must remain empty there because servers may provide datapack-specific ids.</para>
    /// </summary>
    [Fact]
    public void EnchantmentRegistryIsPopulatedExactlyWhereTheReportExists()
    {
        List<int> populated = [.. AllProtocols().Where(p => Enchantments(p).Count > 0).Order()];
        List<int> expected = [.. AllProtocols().Where(p => p is >= 477 and <= 766).Order()];

        Assert.Equal(expected, populated);
    }

    /// <summary>
    /// The mob-effect registry is populated on 47 and on 477-776, and empty on 107-404.
    /// <para>Protocol 47 uses one-based numeric ids. Protocols 107-404 have no static table.</para>
    /// </summary>
    [Fact]
    public void MobEffectRegistryIsPopulatedExactlyWhereTheReportExists()
    {
        List<int> populated = [.. AllProtocols().Where(p => MobEffects(p).Count > 0).Order()];
        List<int> expected = [.. AllProtocols().Where(p => p == 47 || p >= 477).Order()];

        Assert.Equal(expected, populated);
    }

    /// <summary><c>minecraft:attribute</c> is populated on protocols 735 and later and empty on the rest.</summary>
    /// <remarks>
    /// <para><c>AttributeDefinition</c> needs <c>DefaultValue</c>, <c>MinValue</c>, and <c>MaxValue</c> must be present because <c>AttributeInstance</c> uses them for initialization and clamping. Missing definitions fail validation instead of producing zero-filled values.</para>
    /// <para>The coverage boundary below is a literal predicate rather than a read of the dataset, and <c>Umpk.Data.Java.Tests.AttributeRegistryTests</c> pins the counts, the holder ids, the vanilla ranges, and the horse/generic <c>jump_strength</c> era collision that makes raw keying necessary.</para>
    /// </remarks>
    [Fact]
    public void AttributeRegistryIsPopulatedExactlyWhereTheReportExists()
    {
        List<int> populated = [.. AllProtocols().Where(p => JavaGameData.Registries(p).Attributes.Count > 0).Order()];
        List<int> expected = [.. AllProtocols().Where(p => p >= 735).Order()];

        Assert.Equal(expected, populated);
    }

    /// <summary>The enchantment count at each era boundary the dataset actually has. The count moves at 735 (1.16 adds soul_speed), 759 (1.19 adds swift_sneak) and 766 (1.20.6 adds breach, density and wind_burst), and nowhere else across 477-766.</summary>
    [Theory]
    [InlineData(477, 37)] // 1.14
    [InlineData(578, 37)] // 1.15.2, last of the 37-entry band
    [InlineData(735, 38)] // 1.16
    [InlineData(758, 38)] // 1.18.2, last of the 38-entry band
    [InlineData(759, 39)] // 1.19
    [InlineData(765, 39)] // 1.20.4, last of the 39-entry band
    [InlineData(766, 42)] // 1.20.6
    public void EnchantmentCountMatchesTheReport(int protocol, int expected)
    {
        Assert.Equal(expected, Enchantments(protocol).Count);
    }

    /// <summary>Literal enchantment wire ids, per era boundary. The ids that MOVE are the point: an enchantment inserted alphabetically shifts everything after it, so <c>sharpness</c> is 11 on 1.14/1.15, 12 from 1.16 (soul_speed inserted before it) and 13 from 1.19 (swift_sneak), while <c>depth_strider</c> and <c>protection</c> never move and <c>mending</c> moves every time. If a registration-order change therefore changes the asserted ids.</summary>
    [Theory]
    // depth_strider: id 8 on every protocol that has the registry at all. This is the id the 766 wire-bytes test sends, and the pre-1.13 numeric-id table uses the same 8, so the legacy and component eras agree.
    [InlineData(477, "depth_strider", 8)]
    [InlineData(735, "depth_strider", 8)]
    [InlineData(759, "depth_strider", 8)]
    [InlineData(766, "depth_strider", 8)]
    // protection: first entry alphabetically-by-registration, id 0 on every era.
    [InlineData(477, "protection", 0)]
    [InlineData(766, "protection", 0)]
    // sharpness: shifts twice.
    [InlineData(477, "sharpness", 11)]
    [InlineData(578, "sharpness", 11)]
    [InlineData(735, "sharpness", 12)]
    [InlineData(758, "sharpness", 12)]
    [InlineData(759, "sharpness", 13)]
    [InlineData(766, "sharpness", 13)]
    // mending: last-ish entry, so it absorbs every insertion.
    [InlineData(477, "mending", 35)]
    [InlineData(735, "mending", 36)]
    [InlineData(759, "mending", 37)]
    [InlineData(766, "mending", 40)]
    // The era-introducing entries themselves, absent before their era.
    [InlineData(735, "soul_speed", 11)]
    [InlineData(759, "swift_sneak", 12)]
    [InlineData(766, "breach", 38)]
    [InlineData(766, "density", 37)]
    [InlineData(766, "wind_burst", 39)]
    public void EnchantmentResolvesToItsLiteralWireId(int protocol, string name, int expectedId)
    {
        Registry<EnchantmentDefinition> registry = Enchantments(protocol);
        Identifier id = Identifier.Minecraft(name);

        Assert.True(registry.TryGet(id, out RegistryEntry<EnchantmentDefinition> byName),
            $"protocol {protocol}: minecraft:{name} is not in the enchantment registry.");
        Assert.Equal(expectedId, byName.NetworkId);

        // ... and the reverse direction, which is the one a wire decode uses.
        Assert.True(registry.TryGet(expectedId, out RegistryEntry<EnchantmentDefinition> byId),
            $"protocol {protocol}: enchantment id {expectedId} does not resolve.");
        Assert.Equal(id, byId.Id);
        Assert.False(byId.IsDefault);
    }

    /// <summary>Entries introduced in later eras must not resolve in earlier protocols.</summary>
    [Theory]
    [InlineData(477, "soul_speed")]   // 1.16
    [InlineData(578, "soul_speed")]   // 1.16
    [InlineData(735, "swift_sneak")]  // 1.19
    [InlineData(758, "swift_sneak")]  // 1.19
    [InlineData(759, "breach")]       // 1.20.6
    [InlineData(765, "wind_burst")]   // 1.20.6
    public void EnchantmentAbsentBeforeItsWireLayout(int protocol, string name)
    {
        Assert.False(Enchantments(protocol).TryGet(Identifier.Minecraft(name), out _),
            $"protocol {protocol}: minecraft:{name} should not exist yet.");
    }

    /// <summary>Mob-effect counts at each boundary the dataset has: 23 on the curated 1.8 table, 32 from 1.14, 33 from 1.19 (darkness), 39 from 1.20.6 (the wind-charge/trial-chamber effects), 40 from 1.21.11.</summary>
    [Theory]
    [InlineData(47, 23)]   // 1.8, curated
    [InlineData(477, 32)]  // 1.14
    [InlineData(758, 32)]  // 1.18.2
    [InlineData(759, 33)]  // 1.19
    [InlineData(765, 33)]  // 1.20.4
    [InlineData(766, 39)]  // 1.20.6
    [InlineData(773, 39)]  // 1.21.9
    [InlineData(774, 40)]  // 1.21.11
    [InlineData(776, 40)]  // 26.2
    public void MobEffectCountMatchesTheReport(int protocol, int expected)
    {
        Assert.Equal(expected, MobEffects(protocol).Count);
    }

    /// <summary>
    /// Literal mob-effect wire ids. The load-bearing fact here is the ONE-BASED to ZERO-BASED shift the dataset records at protocol 764 (1.20.2): <c>minecraft:speed</c> is id 1 on 47-763 and id 0 from 764 on, and every other effect shifts down by one with it.
    /// <para>Before the shift, speed has explicit id 1 and id 0 is unused. After the shift, ids use dense registration order from 0, so speed takes id 0. The dataset boundary is protocol 764.</para>
    /// </summary>
    [Theory]
    // The 1.8 curated table: 1-based, and its ids are the ones the 1.8 effect packets carry.
    [InlineData(47, "speed", 1)]
    [InlineData(47, "jump_boost", 8)]
    // One-based era.
    [InlineData(477, "speed", 1)]
    [InlineData(477, "jump_boost", 8)]
    [InlineData(477, "levitation", 25)]
    [InlineData(477, "slow_falling", 28)]
    [InlineData(477, "dolphins_grace", 30)]
    [InlineData(759, "speed", 1)]
    [InlineData(759, "darkness", 33)]
    // 0-based from 1.20.2 on: every id above drops by exactly one.
    [InlineData(764, "speed", 0)]
    [InlineData(764, "jump_boost", 7)]
    [InlineData(764, "levitation", 24)]
    [InlineData(764, "slow_falling", 27)]
    [InlineData(764, "dolphins_grace", 29)]
    [InlineData(764, "darkness", 32)]
    [InlineData(766, "speed", 0)]
    [InlineData(766, "infested", 38)]
    [InlineData(776, "speed", 0)]
    [InlineData(776, "dolphins_grace", 29)]
    public void MobEffectResolvesToItsLiteralWireId(int protocol, string name, int expectedId)
    {
        Registry<MobEffectDefinition> registry = MobEffects(protocol);
        Identifier id = Identifier.Minecraft(name);

        Assert.True(registry.TryGet(id, out RegistryEntry<MobEffectDefinition> byName),
            $"protocol {protocol}: minecraft:{name} is not in the mob-effect registry.");
        Assert.Equal(expectedId, byName.NetworkId);

        Assert.True(registry.TryGet(expectedId, out RegistryEntry<MobEffectDefinition> byId),
            $"protocol {protocol}: mob-effect id {expectedId} does not resolve.");
        Assert.Equal(id, byId.Id);
        Assert.False(byId.IsDefault);
    }

    /// <summary>The 1-based era leaves id 0 UNUSED, and the 0-based era uses it. This is the single assertion that separates the two eras with no reference to any effect's name, so a rename cannot hide the shift.</summary>
    [Theory]
    [InlineData(47, false)]
    [InlineData(477, false)]
    [InlineData(763, false)]
    [InlineData(764, true)]
    [InlineData(776, true)]
    public void MobEffectIdZeroIsUsedOnlyFromTheZeroBasedWireLayout(int protocol, bool used)
    {
        Assert.Equal(used, MobEffects(protocol).TryGet(0, out _));
    }
}
