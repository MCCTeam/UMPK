using System.Reflection;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class NativeCooldownCounterTests
{
    public static IEnumerable<object[]> OriginalMethodObservations()
    {
        // Literal traces from the original 1.9 and 26.2 ItemCooldowns methods; both native runs produced identical observations.
        foreach (int protocol in new[] { 107, 776 })
        {
            yield return [protocol, 0, -2147483648, "10000", "10000"];
            yield return [protocol, 0, -1, "10000", "10000"];
            yield return [protocol, 0, 1, "10000", "10000"];
            yield return [protocol, 0, 2, "11000", "11000"];
            yield return [protocol, 0, 100, "11111", "11111"];
            yield return [protocol, 0, 2147483647, "11111", "11111"];
            yield return [protocol, 16777216, -2147483648, "10000", "10000"];
            yield return [protocol, 16777216, -1, "10000", "10000"];
            yield return [protocol, 16777216, 1, "00000", "10000"];
            yield return [protocol, 16777216, 2, "11000", "11000"];
            yield return [protocol, 16777216, 100, "11111", "11111"];
            yield return [protocol, 16777216, 2147483647, "00000", "10000"];
            yield return [protocol, 2147483646, -2147483648, "10000", "10000"];
            yield return [protocol, 2147483646, -1, "00000", "10000"];
            yield return [protocol, 2147483646, 1, "00000", "10000"];
            yield return [protocol, 2147483646, 2, "00000", "10000"];
            yield return [protocol, 2147483646, 100, "00000", "10000"];
            yield return [protocol, 2147483646, 2147483647, "00000", "10000"];
            yield return [protocol, -2147483648, -2147483648, "00000", "11111"];
            yield return [protocol, -2147483648, -1, "00000", "11111"];
            yield return [protocol, -2147483648, 1, "00000", "10000"];
            yield return [protocol, -2147483648, 2, "00000", "11000"];
            yield return [protocol, -2147483648, 100, "11111", "11111"];
            yield return [protocol, -2147483648, 2147483647, "11111", "11111"];
        }
    }

    [Theory]
    [MemberData(nameof(OriginalMethodObservations))]
    public void NativeIntIntervalsAndFloatQueriesMatchTheOriginalMethods(int protocol, int initialTick, int duration, string active, string entries)
    {
        var owner = new ItemCooldownState();
        typeof(ItemCooldownState).GetField("_tickCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, initialTick);
        var item = JavaGameData.Registries(protocol).Items[Identifier.Minecraft("diamond_hoe")];
        var stack = new ItemStack(item, 1);
        if (protocol >= 768)
            owner.SetGroup(item.Id, duration, item.NetworkId);
        else
            owner.SetItem(item.NetworkId, duration);
        for (int phase = 0; phase < active.Length; phase++)
        {
            Assert.Equal(active[phase] == '1', owner.IsOnCooldown(stack, protocol));
            Assert.Equal(entries[phase] == '1', owner.ItemProjection.ContainsKey(item.NetworkId));
            owner.Tick();
        }
    }

    [Theory]
    [InlineData(107)]
    [InlineData(776)]
    public void OneTickCanExpireSeveralIndependentEntries(int protocol)
    {
        var owner = new ItemCooldownState();
        for (int item = 1; item <= 8; item++)
            if (protocol >= 768)
                owner.SetGroup(Identifier.Parse($"umpk:group/{item}"), item == 8 ? 2 : 1, item);
            else
                owner.SetItem(item, item == 8 ? 2 : 1);
        owner.Tick();
        Assert.Equal(8, Assert.Single(owner.ItemProjection).Key);
        owner.Tick();
        Assert.Empty(owner.ItemProjection);
    }
}
