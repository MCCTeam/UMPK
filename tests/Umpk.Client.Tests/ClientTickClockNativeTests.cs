using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ClientTickClockNativeTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            JsonElement cases = document.RootElement.GetProperty("timer_groups")[release.GetProperty("timer_group").GetInt32()];
            for (int index = 0; index < cases.GetArrayLength(); index++)
                yield return [release.GetProperty("version").GetString()!, index];
        }
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void FrameAccumulatorMatchesNativeFloatResidualsAcrossRateAndFreezeChanges(string version, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        JsonElement input = document.RootElement.GetProperty("timer_groups")[release.GetProperty("timer_group").GetInt32()][index];
        bool hasLevel = input.GetProperty("world_present").GetBoolean();
        var manager = new WorldTickState();
        manager.ApplyState(BitConverter.Int32BitsToSingle(input.GetProperty("rate_bits").GetInt32()), input.GetProperty("frozen").GetBoolean());
        manager.BeginTick();
        var clock = new ClientTickClock(1000);
        foreach (JsonElement expected in input.GetProperty("states").EnumerateArray())
        {
            switch (expected.GetProperty("operation").GetString())
            {
                case "set_rate":
                    manager.ApplyState(7.3f, manager.IsFrozen);
                    break;
                case "set_frozen":
                    manager.ApplyState(manager.TickRate, true);
                    break;
                case "set_unfrozen":
                    manager.ApplyState(manager.TickRate, false);
                    break;
                case "set_steps":
                    manager.SetSteps(2);
                    break;
                case "tick_frozen":
                case "first_step":
                case "last_step":
                case "steps_spent":
                case "tick_unfrozen":
                    manager.BeginTick();
                    break;
            }

            float target = hasLevel ? manager.TargetMilliseconds : 50f;
            Assert.Equal(expected.GetProperty("manager").GetProperty("target_50_bits").GetInt32(), BitConverter.SingleToInt32Bits(target));
            Assert.Equal(expected.GetProperty("ticks").GetInt32(), clock.Advance(expected.GetProperty("millis").GetInt64(), target));
            Assert.Equal(expected.GetProperty("delta_bits").GetInt32(), BitConverter.SingleToInt32Bits(clock.DeltaTicks));
            Assert.Equal(expected.GetProperty("residual_bits").GetInt32(), BitConverter.SingleToInt32Bits(clock.ResidualTicks));
        }
    }

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Protocol.Java.Tests", "Fixtures", "TickControl.native.json")));
}
