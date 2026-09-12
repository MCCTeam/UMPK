using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>
/// Proves the steady-state tick allocates nothing (the zero-allocation tick contract).
/// <para>The engine tick is written to allocate zero bytes in EVERY JIT tier (in particular, block flag tests are bitwise rather than the boxing <c>Enum.HasFlag</c>), so the assertion holds in Debug (optimizer disabled) as well as Release. What remains tier-dependent is third-party/framework code the tick calls before tier-1 promotion: under CPU contention the background tier-up worker may not promote <see cref="PlayerPhysics.Step"/> before a fixed warmup completes, and tier-0 codegen of surrounding plumbing can heap-allocate. That is the documented flake (passes in isolation, fails under parallel full-suite load). We defeat it by warming and re-measuring until the steady state reads exactly zero, bounded by an attempt cap so a genuine allocation regression still fails deterministically. Promotion is monotonic, so a single zero reading is conclusive.</para>
/// <para>Retry burn is bounded two ways: when the engine assembly itself was compiled with the JIT optimizer disabled (Debug build, detected via <see cref="System.Diagnostics.DebuggableAttribute"/>), tier-up can never change the outcome, so the FIRST measurement is conclusive and no retries run; under an optimizing JIT, a streak of identical nonzero readings is a stable plateau no pending promotion explains away, so the loop bails early instead of burning all 40 attempts.</para>
/// </summary>
//
public sealed class AllocationTests
{
    private const int Warmup = 50_000;
    private const int Measured = 50_000;
    private const int PromotionAttempts = 40;

    // Consecutive identical nonzero readings that make a failure conclusive under an optimizing JIT.
    private const int ConclusiveFailureStreak = 10;

    // Debug-built engine assembly: the optimizer is off for its methods, tier-up cannot occur, and one measurement decides the test.
    private static readonly bool JitOptimizerDisabled =
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>(typeof(PlayerPhysics).Assembly)?.IsJITOptimizerDisabled) ?? false;

    private static long MeasurePerStepBytes(PlayerPhysics engine, MovementInput input)
    {
        for (int i = 0; i < Warmup; i++)
            engine.Step(input);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Measured; i++)
            engine.Step(input);

        long after = GC.GetAllocatedBytesForCurrentThread();
        return (after - before) / Measured;
    }

    // retry until the JIT has promoted Step() to optimized codegen (bounded), bailing early when the outcome is already conclusive (optimizer disabled, or a stable nonzero plateau under an optimizing JIT).
    private static void AssertZeroAllocationPerStep(FixtureWorld world, Vec3d start, MovementInput input)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.Modern);
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(start, 0f, 0f);

        int maxAttempts = JitOptimizerDisabled ? 1 : PromotionAttempts;
        long perStep = -1;
        long previous = -1;
        int identicalStreak = 0;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            perStep = MeasurePerStepBytes(engine, input);
            if (perStep == 0)
                return;

            identicalStreak = perStep == previous ? identicalStreak + 1 : 1;
            previous = perStep;
            if (identicalStreak >= ConclusiveFailureStreak)
            {
                break; // stable nonzero plateau: a real regression, not a pending tier-up
            }
        }

        Assert.Equal(0, perStep);
    }

    private static FixtureWorld FlatFloor() =>
        new FixtureWorld().Floor(-5, 5, -5, 400, 63, BlockKind.Stone);

    [Fact]
    public void Step_AllocatesZeroBytesPerTick()
    {
        AssertZeroAllocationPerStep(
            FlatFloor(),
            new Vec3d(0.5, 64, 0.5),
            new MovementInput { Forward = true, Sprint = true });
    }

    [Fact]
    public void StepWithJump_AllocatesZeroBytesPerTick()
    {
        AssertZeroAllocationPerStep(
            FlatFloor(),
            new Vec3d(0.5, 64, 0.5),
            new MovementInput { Forward = true, Jump = true, Sprint = true });
    }

    [Fact]
    public void StepIntoWall_WithStepUp_AllocatesZeroBytesPerTick()
    {
        // Exercise the step-up path (stackalloc, no heap) walking into a slab wall repeatedly.
        var world = new FixtureWorld()
            .Floor(-5, 5, -5, 60, 63, BlockKind.Stone)
            .Fill(-5, 64, 3, 5, 64, 60, BlockKind.Slab);
        AssertZeroAllocationPerStep(
            world,
            new Vec3d(0.5, 64, 2.0),
            new MovementInput { Forward = true });
    }
}
