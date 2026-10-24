using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class WorldTickStateNativeTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            JsonElement cases = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
            for (int index = 0; index < cases.GetArrayLength(); index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
        }
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void ManagerMatchesNativeRateBitsLatchAndStepConsumption(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement input = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()][index];
        bool stepping = input.GetProperty("kind").GetString() == "step";
        bool frozen = input.GetProperty("frozen").GetBoolean();
        var state = new WorldTickState();
        if (stepping)
            state.ApplyState(20, frozen);

        JsonElement snapshots = input.GetProperty("states");
        AssertState(snapshots[0], state);
        if (input.GetProperty("world_present").GetBoolean())
        {
            int pattern = input.GetProperty("pattern").GetInt32();
            if (stepping)
                state.SetSteps(pattern);
            else
                state.ApplyState(BitConverter.Int32BitsToSingle(pattern), frozen);
        }

        AssertState(snapshots[1], state);
        foreach (JsonElement expected in snapshots.EnumerateArray().Skip(2))
        {
            state.BeginTick();
            AssertState(expected, state);
        }
    }

    private static void AssertState(JsonElement expected, WorldTickState state)
    {
        Assert.Equal(expected.GetProperty("rate_bits").GetInt32(), BitConverter.SingleToInt32Bits(state.TickRate));
        Assert.Equal(expected.GetProperty("nanos").GetInt64(), state.NanosecondsPerTick);
        Assert.Equal(expected.GetProperty("millis_bits").GetInt32(), BitConverter.SingleToInt32Bits(state.MillisecondsPerTick));
        Assert.Equal(expected.GetProperty("target_50_bits").GetInt32(), BitConverter.SingleToInt32Bits(state.TargetMilliseconds));
        Assert.Equal(expected.GetProperty("runsNormally").GetBoolean(), state.RunsNormally);
        Assert.Equal(expected.GetProperty("isFrozen").GetBoolean(), state.IsFrozen);
        Assert.Equal(expected.GetProperty("isSteppingForward").GetBoolean(), state.IsSteppingForward);
        Assert.Equal(expected.GetProperty("frozenTicksToRun").GetInt32(), state.FrozenTicksToRun);
    }

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Protocol.Java.Tests", "Fixtures", "TickControl.native.json")));
}
