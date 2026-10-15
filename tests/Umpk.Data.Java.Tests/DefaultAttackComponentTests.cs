using System.Text.Json;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Data.Java.Tests;

public sealed class DefaultAttackComponentTests
{
    [Theory]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public void EveryItemRetainsItsNativeAttackDefaults(int protocol)
    {
        using JsonDocument measured = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "data", "java", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "item-attack.json")));
        RegistryAccess registries = JavaGameData.Registries(protocol);
        Assert.True(registries.TryGetRegistry(Identifier.Minecraft("sound_event"), out IRegistry sounds));
        foreach (JsonProperty entry in measured.RootElement.GetProperty("items").EnumerateObject())
        {
            var item = new ItemStack(registries.Items[Identifier.Parse(entry.Name)], 1);
            Assert.True(item.Components.HasNoPatch);
            JsonElement expected = entry.Value.GetProperty("piercing_weapon");
            PiercingWeaponComponent? actual = item.Components.Get(DataComponents.PiercingWeapon);
            if (expected.ValueKind == JsonValueKind.Null)
                Assert.Null(actual);
            else
            {
                Assert.NotNull(actual);
                Assert.Equal(!expected.TryGetProperty("deals_knockback", out JsonElement knockback) || knockback.GetBoolean(), actual.DealsKnockback);
                Assert.Equal(expected.TryGetProperty("dismounts", out JsonElement dismounts) && dismounts.GetBoolean(), actual.Dismounts);
                AssertSound(expected, "sound", actual.Sound, sounds);
                AssertSound(expected, "hit_sound", actual.HitSound, sounds);
            }
            JsonElement charge = entry.Value.GetProperty("minimum_attack_charge");
            Assert.Equal(charge.ValueKind == JsonValueKind.Null ? (float?)null : charge.GetSingle(), item.Components.Get(DataComponents.MinimumAttackCharge)?.Value);
        }
    }

    private static void AssertSound(JsonElement expected, string field, SoundEventRef? actual, IRegistry sounds)
    {
        if (!expected.TryGetProperty(field, out JsonElement name))
        {
            Assert.Null(actual);
            return;
        }
        Assert.NotNull(actual);
        Assert.NotNull(actual.RegistryId);
        Assert.True(sounds.TryGetKey(actual.RegistryId.Value, out Identifier key));
        Assert.Equal(Identifier.Parse(name.GetString()!), key);
        Assert.Null(actual.SoundId);
        Assert.Null(actual.FixedRange);
    }
}
