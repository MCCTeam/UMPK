using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class VanillaAttackChargeTests
{
    [Fact]
    public void ChargeGatesMatchAllNativeIntegerAndFloatingPointControls()
    {
        using Stream? stream = typeof(VanillaAttackChargeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.AttackCharge.native.json");
        Assert.NotNull(stream);
        using JsonDocument fixture = JsonDocument.Parse(stream);
        Assert.Equal(6, fixture.RootElement.GetProperty("sources").GetArrayLength());
        JsonElement cases = fixture.RootElement.GetProperty("cases");
        Assert.Equal(1024, cases.GetArrayLength());
        foreach (JsonElement row in cases.EnumerateArray())
        {
            int requiredBits = row[0].GetInt32();
            long speedBits = row[1].GetInt64();
            int ticks = row[2].GetInt32();
            int tolerance = row[3].GetInt32();
            bool actual = VanillaAttackCharge.IsBlocked(ticks, BitConverter.Int64BitsToDouble(speedBits), BitConverter.Int32BitsToSingle(requiredBits), tolerance);
            Assert.True(actual == row[4].GetBoolean(), $"Native charge mismatch: charge={requiredBits:X8}, speed={speedBits:X16}, ticks={ticks}, tolerance={tolerance}.");
        }
    }

    [Fact]
    public void ItemIdentityAndPlayerReplacementResetTheCounterAfterIncrement()
    {
        var charge = new VanillaAttackCharge();
        RegistryAccess registries = JavaGameData.Registries(777);
        Assert.True(registries.Items.TryGet(Identifier.Minecraft("stone"), out RegistryEntry<ItemDefinition> stone));
        Assert.True(registries.Items.TryGet(Identifier.Minecraft("diamond_spear"), out RegistryEntry<ItemDefinition> spear));
        ItemStack ordinary = new(stone, 1);
        charge.Advance(1, ItemStack.Empty);
        Assert.Equal(1, charge.StrengthTicks);
        charge.Advance(1, ordinary);
        Assert.Equal(0, charge.StrengthTicks);
        charge.Advance(1, ordinary.With(DataComponents.MinimumAttackCharge, new MinimumAttackChargeComponent(1)));
        Assert.Equal(1, charge.StrengthTicks);
        charge.Advance(1, ordinary.WithCount(12));
        Assert.Equal(2, charge.StrengthTicks);
        charge.Advance(1, new ItemStack(spear, 1));
        Assert.Equal(0, charge.StrengthTicks);
        charge.Advance(1, new ItemStack(spear, 1));
        Assert.Equal(1, charge.StrengthTicks);
        charge.Synchronize(2);
        Assert.Equal(0, charge.StrengthTicks);
        charge.Advance(2, new ItemStack(spear, 1));
        Assert.Equal(0, charge.StrengthTicks);
    }

    [Fact]
    public void AnAttackResetRetainsTheLastHeldItemIdentity()
    {
        var charge = new VanillaAttackCharge();
        Assert.True(JavaGameData.Registries(777).Items.TryGet(Identifier.Minecraft("stone"), out RegistryEntry<ItemDefinition> stone));
        ItemStack held = new(stone, 1);
        charge.Advance(1, held);
        charge.Advance(1, held);
        charge.Advance(1, held);
        Assert.Equal(2, charge.StrengthTicks);
        charge.Reset();
        Assert.Equal(0, charge.StrengthTicks);
        charge.Advance(1, held);
        Assert.Equal(1, charge.StrengthTicks);
    }
}
