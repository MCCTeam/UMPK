using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Era coverage for <see cref="PhysicsProfile"/>: the legacy-vs-modern fluid movement divergence, the <see cref="PhysicsProfile.ForProtocol"/> feature-era breakpoints, and the <see cref="PhysicsProfile.FromFeatures"/> flag mapping.</summary>
public sealed class PhysicsProfileSelectionTests
{
    // (a) Legacy fluid vs modern swimming-update motion divergence. The pre-1.13 profile (ForProtocol, FluidMovementEra.Legacy) drives the legacy water friction/acceleration path (PlayerPhysics.TravelInWater without the swimming-update blend); the modern profile applies the water-movement-efficiency blend to slowDown/speed. With a non-zero WaterMovementEfficiency the same submerged forward input yields measurably different motion.
    [Fact]
    public void LegacyFluid_And_ModernSwim_ProduceDifferentWaterMotion()
    {
        PhysicsProfile legacy = PhysicsProfile.ForProtocol(47);
        Assert.Equal(FluidMovementEra.Legacy, legacy.FluidMovement);
        Assert.Equal(FluidMovementEra.SwimmingUpdate, PhysicsProfile.Modern.FluidMovement);

        // The swimming-update water blend only engages when water-movement efficiency is non-zero (e.g. Depth Strider); the legacy era ignores it entirely, which is the divergence under test.
        PhysicsConditions conditions = PhysicsConditions.Default with { WaterMovementEfficiency = 1.0f };

        Vec3d RunForward(PhysicsProfile profile)
        {
            var world = new FixtureWorld().Fill(-3, 40, -3, 3, 90, 3, BlockKind.Water);
            var engine = new PlayerPhysics(world, profile);
            engine.SetConditions(conditions);
            engine.Reset(new Vec3d(0.5, 60, 0.5), 0f, 0f);
            engine.SetRotation(0f, 0f);
            var forward = new MovementInput { Forward = true };
            for (int i = 0; i < 20; i++)
                engine.Step(forward);

            return engine.State.Position;
        }

        Vec3d legacyPos = RunForward(legacy);
        Vec3d modernPos = RunForward(PhysicsProfile.Modern);

        double dx = legacyPos.X - modernPos.X;
        double dy = legacyPos.Y - modernPos.Y;
        double dz = legacyPos.Z - modernPos.Z;
        double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        Assert.True(
            distance > 0.05,
            $"legacy and modern water motion did not diverge (dist={distance}); legacy={legacyPos}, modern={modernPos}");
    }

    // (b) ForProtocol feature-era breakpoints. Expected values read from PhysicsProfile.ForProtocol: <= 47: Legacy fluid, no elytra/swim/crawl, PreCrawl pose, Legacy cadence 48.. 392: Legacy fluid, elytra, no swim/crawl, PreCrawl pose, DirtyChecked cadence 393.. 476: SwimmingUpdate, elytra, swim pose, no crawl, PreCrawl pose, DirtyChecked
    // >= 477: Modern (SwimmingUpdate, elytra, swim + crawl, ModernCrawl pose, DirtyChecked)
    [Theory]
    [InlineData(46, FluidMovementEra.Legacy, false, false, false, PoseDimensionsEra.PreCrawl, PositionSendCadence.Legacy)]
    [InlineData(47, FluidMovementEra.Legacy, false, false, false, PoseDimensionsEra.PreCrawl, PositionSendCadence.Legacy)]
    [InlineData(48, FluidMovementEra.Legacy, true, false, false, PoseDimensionsEra.PreCrawl, PositionSendCadence.DirtyChecked)]
    [InlineData(392, FluidMovementEra.Legacy, true, false, false, PoseDimensionsEra.PreCrawl, PositionSendCadence.DirtyChecked)]
    [InlineData(393, FluidMovementEra.SwimmingUpdate, true, true, false, PoseDimensionsEra.PreCrawl, PositionSendCadence.DirtyChecked)]
    [InlineData(476, FluidMovementEra.SwimmingUpdate, true, true, false, PoseDimensionsEra.PreCrawl, PositionSendCadence.DirtyChecked)]
    [InlineData(477, FluidMovementEra.SwimmingUpdate, true, true, true, PoseDimensionsEra.ModernCrawl, PositionSendCadence.DirtyChecked)]
    [InlineData(776, FluidMovementEra.SwimmingUpdate, true, true, true, PoseDimensionsEra.ModernCrawl, PositionSendCadence.DirtyChecked)]
    public void ForProtocol_PinsFeatureWireLayoutBreakpoints(
        int protocol, FluidMovementEra fluid, bool elytra, bool swimPose, bool crawlPose,
        PoseDimensionsEra poseDims, PositionSendCadence positionSend)
    {
        PhysicsProfile p = PhysicsProfile.ForProtocol(protocol);
        Assert.Equal(fluid, p.FluidMovement);
        Assert.Equal(elytra, p.ElytraAvailable);
        Assert.Equal(swimPose, p.SwimPoseAvailable);
        Assert.Equal(crawlPose, p.CrawlPoseAvailable);
        Assert.Equal(poseDims, p.PoseDimensions);
        Assert.Equal(positionSend, p.PositionSend);
    }

    // (c) FromFeatures maps each input feature flag to its own profile field independently.
    [Fact]
    public void FromFeatures_MapsEachFlagToItsField()
    {
        PhysicsProfile modern = PhysicsProfile.FromFeatures(
            swimmingUpdate: true, sprintAwareWaterTravel: true, waterClimbBump: true,
            elytraAvailable: true, swimPoseAvailable: true, crawlPoseAvailable: true,
            modernCrouchHeight: true, dirtyCheckedPositionCadence: true);
        Assert.Equal(FluidMovementEra.SwimmingUpdate, modern.FluidMovement);
        Assert.Equal(WaterTravelEra.SprintAware, modern.WaterTravel);
        Assert.True(modern.WaterClimbBumpAvailable);
        Assert.True(modern.ElytraAvailable);
        Assert.True(modern.SwimPoseAvailable);
        Assert.True(modern.CrawlPoseAvailable);
        Assert.Equal(PoseDimensionsEra.ModernCrawl, modern.PoseDimensions);
        Assert.Equal(PositionSendCadence.DirtyChecked, modern.PositionSend);

        PhysicsProfile legacy = PhysicsProfile.FromFeatures(
            swimmingUpdate: false, sprintAwareWaterTravel: false, waterClimbBump: false,
            elytraAvailable: false, swimPoseAvailable: false, crawlPoseAvailable: false,
            modernCrouchHeight: false, dirtyCheckedPositionCadence: false);
        Assert.Equal(FluidMovementEra.Legacy, legacy.FluidMovement);
        Assert.Equal(WaterTravelEra.Legacy, legacy.WaterTravel);
        Assert.False(legacy.WaterClimbBumpAvailable);
        Assert.False(legacy.ElytraAvailable);
        Assert.False(legacy.SwimPoseAvailable);
        Assert.False(legacy.CrawlPoseAvailable);
        Assert.Equal(PoseDimensionsEra.PreCrawl, legacy.PoseDimensions);
        Assert.Equal(PositionSendCadence.Legacy, legacy.PositionSend);

        // Mixed flags prove independence: swim+elytra+dirty-cadence on, crawl and modern crouch off, and the two fluid axes set opposite ways (the real 47-340 shape: swimmingUpdate + legacy water travel), which no single-axis mapping could produce.
        PhysicsProfile mixed = PhysicsProfile.FromFeatures(
            swimmingUpdate: true, sprintAwareWaterTravel: false, waterClimbBump: false,
            elytraAvailable: true, swimPoseAvailable: true, crawlPoseAvailable: false,
            modernCrouchHeight: false, dirtyCheckedPositionCadence: true);
        Assert.Equal(FluidMovementEra.SwimmingUpdate, mixed.FluidMovement);
        Assert.Equal(WaterTravelEra.Legacy, mixed.WaterTravel);
        Assert.False(mixed.WaterClimbBumpAvailable);
        Assert.True(mixed.ElytraAvailable);
        Assert.True(mixed.SwimPoseAvailable);
        Assert.False(mixed.CrawlPoseAvailable);
        Assert.Equal(PoseDimensionsEra.PreCrawl, mixed.PoseDimensions);
        Assert.Equal(PositionSendCadence.DirtyChecked, mixed.PositionSend);

        // The climbable-bump axis is its own field, not an alias of either neighbour it could be mistaken for. This is the real 393-404 shape - sprint-aware water travel WITHOUT the bump - and it also sets the bump opposite to crawlPose, the flag that merely shares its 477 boundary. No mapping that folded the bump into waterTravel or crawlPose could produce it.
        PhysicsProfile bumpIsIndependent = PhysicsProfile.FromFeatures(
            swimmingUpdate: true, sprintAwareWaterTravel: true, waterClimbBump: false,
            elytraAvailable: true, swimPoseAvailable: true, crawlPoseAvailable: false,
            modernCrouchHeight: false, dirtyCheckedPositionCadence: true);
        Assert.Equal(WaterTravelEra.SprintAware, bumpIsIndependent.WaterTravel);
        Assert.False(bumpIsIndependent.WaterClimbBumpAvailable);

        PhysicsProfile bumpWithoutCrawl = PhysicsProfile.FromFeatures(
            swimmingUpdate: true, sprintAwareWaterTravel: true, waterClimbBump: true,
            elytraAvailable: true, swimPoseAvailable: true, crawlPoseAvailable: false,
            modernCrouchHeight: false, dirtyCheckedPositionCadence: true);
        Assert.True(bumpWithoutCrawl.WaterClimbBumpAvailable);
        Assert.False(bumpWithoutCrawl.CrawlPoseAvailable);
    }
}
