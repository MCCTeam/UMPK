using System.Reflection;
using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests.Parity;

/// <summary>
/// Characterization-trace floor: committed per-tick traces of UMPK's own engine over scripted inputs on the course fixtures. These traces are self-characterization of UMPK's engine, NOT vanilla-parity ground truth: they are recorded from UMPK itself and replayed against UMPK, so the <c>MaxPositionDeviation == 0</c> / <c>MaxVelocityDeviation == 0</c> assertions certify only a determinism/regression floor (the engine reproduces its own committed output bit-for-bit) and must not be overread as agreement with vanilla. The vanilla-recorded parity trace is still pending the manual instrumented-recording session; see README.
///
/// <para>Traces live under <c>Traces/</c> (copied to the output dir). If a trace file is missing the test regenerates and writes it into the source tree so it can be committed; a present trace is replayed and must reproduce bit-identically (epsilon 0).</para>
///
/// <para>When an engine change is DELIBERATE, set <c>UMPK_TRACE_DUMP_DIR</c> to write what the engine now produces into a scratch directory WITHOUT touching the committed files, so the candidate can be diffed against the committed trace field by field and every moved number justified before it is installed. The dump never overwrites a committed trace: silently regenerating expectations in place is how a test comes to pin broken behaviour as the contract.</para>
/// </summary>
public sealed class CharacterizationTraceTests
{
    private static readonly PhysicsConditions Survival = PhysicsConditions.Default;

    public static IEnumerable<object[]> Scenarios()
    {
        yield return ["flat_sprint"];
        yield return ["stair_ascend"];
        yield return ["slime_bounce"];
        yield return ["ladder_climb"];
        yield return ["crawl_tunnel"];
        yield return ["jump_arc"];
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void CharacterizationTrace_ReproducesBitIdentically(string scenario)
    {
        (FixtureWorld world, Vec3d start, float yaw, float pitch, IReadOnlyList<MovementInput> inputs) = Build(scenario);

        PhysicsTrace fresh = TraceReplay.Record(world, PhysicsProfile.Modern, Survival, start, yaw, pitch, inputs, "modern");

        string? dumpDir = Environment.GetEnvironmentVariable("UMPK_TRACE_DUMP_DIR");
        if (!string.IsNullOrEmpty(dumpDir))
        {
            Directory.CreateDirectory(dumpDir);
            File.WriteAllText(Path.Combine(dumpDir, scenario + ".umpktrace"), fresh.Serialize());
        }

        string path = TracePath(scenario);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, fresh.Serialize());
        }

        PhysicsTrace committed = PhysicsTrace.Parse(File.ReadAllText(path));

        // The committed trace must reproduce with zero deviation (deterministic engine).
        TraceReplay.ReplayResult result = TraceReplay.Replay(committed, world, PhysicsProfile.Modern, Survival);
        Assert.Equal(0.0, result.MaxPositionDeviation, 12);
        Assert.Equal(0.0, result.MaxVelocityDeviation, 12);
        Assert.Equal(0, result.PoseMismatches);
        Assert.True(result.TickCount > 0);

        // And a freshly recorded run must byte-match the committed file (catches silent drift).
        Assert.Equal(committed.Serialize(), fresh.Serialize());
    }

    private static (FixtureWorld, Vec3d, float, float, IReadOnlyList<MovementInput>) Build(string scenario)
    {
        switch (scenario)
        {
            case "flat_sprint":
                {
                    var w = new FixtureWorld().Floor(-5, 5, -5, 200, 63, BlockKind.Stone);
                    return (w, new Vec3d(0.5, 64, 0.5), 0f, 0f, Repeat(new MovementInput { Forward = true, Sprint = true }, 60));
                }

            case "stair_ascend":
                {
                    var w = new FixtureWorld().Floor(-5, 5, -5, 40, 63, BlockKind.Stone);
                    for (int i = 0; i < 6; i++)
                        w.Fill(-5, 64 + i, 5 + i * 2, 5, 64 + i, 40, BlockKind.Slab);

                    return (w, new Vec3d(0.5, 64, 0.5), 0f, 0f, Repeat(new MovementInput { Forward = true, Jump = true }, 80));
                }

            case "slime_bounce":
                {
                    var w = new FixtureWorld().Floor(-5, 5, -5, 5, 63, BlockKind.SlimeBlock);
                    return (w, new Vec3d(0.5, 72, 0.5), 0f, 0f, Repeat(MovementInput.None, 80));
                }

            case "ladder_climb":
                {
                    var w = new FixtureWorld()
                        .Floor(-5, 5, -5, 5, 63, BlockKind.Stone)
                        .Fill(0, 64, 1, 0, 80, 1, BlockKind.Stone)
                        .Fill(0, 64, 0, 0, 80, 0, BlockKind.Ladder);
                    return (w, new Vec3d(0.5, 64, 0.5), 0f, 0f, Repeat(new MovementInput { Forward = true }, 80));
                }

            case "crawl_tunnel":
                {
                    // A continuous 1-block-tall tunnel (floor y=63, ceiling y=65). The player starts inside it so the crawl (swim) pose is forced and crawl locomotion is exercised.
                    var w = new FixtureWorld()
                        .Floor(-5, 5, -5, 60, 63, BlockKind.Stone)
                        .Floor(-5, 5, -5, 60, 65, BlockKind.Stone);
                    return (w, new Vec3d(0.5, 64, 0.5), 0f, 0f, Repeat(new MovementInput { Forward = true }, 80));
                }

            case "jump_arc":
                {
                    var w = new FixtureWorld().Floor(-5, 5, -5, 200, 63, BlockKind.Stone);
                    return (w, new Vec3d(0.5, 64, 0.5), 45f, 0f, Repeat(new MovementInput { Forward = true, Jump = true, Sprint = true }, 60));
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "unknown scenario");
        }
    }

    private static MovementInput[] Repeat(MovementInput input, int count)
    {
        var arr = new MovementInput[count];
        Array.Fill(arr, input);
        return arr;
    }

    private static string TracePath(string scenario)
    {
        // Resolve the source-tree Traces/ dir from the test assembly location so regenerated traces land next to the committed fixtures rather than only in bin/.
        string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        // bin/Release/net10.0 -> project root
        string projectRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", ".."));
        return Path.Combine(projectRoot, "Parity", "Traces", scenario + ".umpktrace");
    }
}
