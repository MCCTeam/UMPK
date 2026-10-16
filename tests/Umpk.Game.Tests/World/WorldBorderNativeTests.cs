using System.Text.Json;
using Umpk.Game.World;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Game.Tests.World;

public sealed class WorldBorderNativeTests
{
    [Fact]
    public void MillisecondInterpolationDividesElapsedTimeBeforeSubtractingFromTheTarget()
    {
        const long duration = 9_007_199_254_740_993;
        var time = new BorderTimeProvider();
        var border = new WorldBorderState(0, 0, 100, 20, duration, 5, 15, 100, WorldBorderInterpolationClock.Milliseconds, time);

        time.TimestampNanos = 1_000_000;

        // Native MovingBorderExtent.getSize uses (Util.getMillis() - lerpBegin) / lerpDuration. Reconstructing elapsed time from double duration minus remaining loses this millisecond.
        double expected = 100 + (1.0 / (double)duration) * (20 - 100);
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(border.Size));
    }

    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            for (int index = 0; index < 15; index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void ExtentReplayMatchesNativeClockBoundsAndContainment(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement group = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
        WorldBorderInterpolationClock clock = WorldBorderState.InterpolationClockForProtocol(protocol);
        Assert.Equal(group.GetProperty("clock").GetString() == "world_ticks", clock == WorldBorderInterpolationClock.WorldTicks);
        JsonElement scenario = group.GetProperty("cases")[index];
        var time = new BorderTimeProvider { TimestampNanos = 1_000_900_000 };
        var border = new WorldBorderState(0, 0, 100, 100, 0, 5, 15, 29_999_984, clock, time);
        switch (scenario.GetProperty("kind").GetString())
        {
            case "lerp":
                border = border.WithLerp(100, 20, scenario.GetProperty("duration").GetInt64());
                break;
            case "static":
                border = border.WithCenter(scenario.GetProperty("center").GetDouble(), 0).WithAbsoluteMaxSize(scenario.GetProperty("absolute").GetInt32());
                break;
            case "initialize_listener":
                long duration = scenario.GetProperty("duration").GetInt64();
                border = new(99, 0, duration > 0 ? 100 : 20, 20, duration, 5, 15, 100, clock, time);
                break;
            default:
                Assert.Fail("Unknown native border scenario.");
                break;
        }

        foreach (JsonElement state in scenario.GetProperty("states").EnumerateArray())
        {
            time.TimestampNanos = state.GetProperty("clock_nanos").GetInt64();
            string operation = state.GetProperty("operation").GetString()!;
            switch (operation)
            {
                case "center_and_warnings":
                    border = border.WithCenter(3.25, -2).WithWarnings(7, 9);
                    break;
                case "absolute_max":
                    border = border.WithAbsoluteMaxSize(25);
                    break;
                case "tick" or "freeze_wait_and_tick" or "single_step" or "frozen_tick" or "unfreeze_tick" or "final_tick":
                    // Tick eligibility is an input recorded from the original manager. Session freeze ownership is outside this extent test.
                    if (state.GetProperty("runs_normally").GetBoolean())
                        border = border.AdvanceTick();
                    break;
            }

            EqualBits(state, "getSize", border.Size);
            EqualBits(state, "getLerpTarget", border.TargetSize);
            EqualBits(state, "getCenterX", border.CenterX);
            EqualBits(state, "getCenterZ", border.CenterZ);
            EqualBits(state, "getMinX", border.MinX);
            EqualBits(state, "getMaxX", border.MaxX);
            EqualBits(state, "getMinZ", border.MinZ);
            EqualBits(state, "getMaxZ", border.MaxZ);
            EqualBits(state, "getWarningTime", border.WarningTimeSeconds);
            Assert.Equal(state.GetProperty("getWarningBlocks").GetInt32(), border.WarningBlocks);
            Assert.Equal(state.GetProperty("getAbsoluteMaxSize").GetInt32(), border.AbsoluteMaxSize);
            Assert.Equal(state.GetProperty("remaining").GetInt64(), border.LerpRemainingTime);
            foreach (JsonElement point in state.GetProperty("block_points").EnumerateArray())
                Assert.Equal(point[1].GetBoolean(), border.IsWithinBounds(point[0].GetInt32(), 0, WorldBorderState.ContainmentEraForProtocol(protocol)));
        }
    }

    private static void EqualBits(JsonElement state, string property, double actual) => Assert.Equal(BitConverter.DoubleToInt64Bits(state.GetProperty(property).GetDouble()), BitConverter.DoubleToInt64Bits(actual));

    private static JsonDocument ReadNative() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Game.Tests", "Fixtures", "WorldBorder.native.json")));
}

internal sealed class BorderTimeProvider : TimeProvider
{
    internal long TimestampNanos { get; set; }
    public override long TimestampFrequency => 1_000_000_000;
    public override long GetTimestamp() => TimestampNanos;
}
