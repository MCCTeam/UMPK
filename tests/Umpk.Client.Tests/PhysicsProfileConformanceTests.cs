using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Pins <see cref="PhysicsProfileFactory.FromJavaVersion"/> - the production <see cref="PhysicsProfile"/> construction path, wired into <c>PhysicsEngineHolder</c> - against the dataset for every supported protocol in <see cref="JavaVersions"/>.<c>All</c>.</summary>
/// <remarks>
/// <see cref="ProductionProfile_MatchesDataset_ForEveryProtocol"/> proves "production profile == dataset". It deliberately does NOT prove "dataset == vanilla" - that direction can never be caught by a test that reads its own expectations off the same dataset the production code reads. <see cref="DatasetFluidMovement_MatchesVanilla_ForEveryProtocol"/> closes exactly that gap for the one field where the two directions have actually disagreed, by stating the vanilla-derived expectation independently instead of reading it back off the dataset.
/// <para><c>PhysicsProfile.ForProtocol</c> classifies protocols 47-392 as <see cref="FluidMovementEra.Legacy"/>, while the dataset declares <c>swimmingUpdate</c>. The literal expectation table independently covers protocols 47 and 107-340 so dataset drift is visible.</para>
/// </remarks>
public sealed class PhysicsProfileConformanceTests
{
    public static TheoryData<int> AllProtocols()
    {
        var data = new TheoryData<int>();
        foreach (JavaVersion version in JavaVersions.All)
            data.Add(version.Version.Protocol);

        return data;
    }

    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void ProductionProfile_MatchesDataset_ForEveryProtocol(int protocol)
    {
        JavaVersion version = JavaVersions.All.Single(v => v.Version.Protocol == protocol);
        ProtocolFeatures features = version.Features;

        PhysicsProfile actual = PhysicsProfileFactory.FromJavaVersion(version);

        // These six fields are computed independently from the dataset declarations.
        FluidMovementEra expectedFluid = string.Equals(features.FluidMovement, "legacy", StringComparison.Ordinal)
            ? FluidMovementEra.Legacy
            : FluidMovementEra.SwimmingUpdate;
        Assert.Equal(expectedFluid, actual.FluidMovement);

        WaterTravelEra expectedWaterTravel = string.Equals(features.WaterTravel, "legacy", StringComparison.Ordinal)
            ? WaterTravelEra.Legacy
            : WaterTravelEra.SprintAware;
        Assert.Equal(expectedWaterTravel, actual.WaterTravel);

        Assert.Equal(features.WaterClimbBump, actual.WaterClimbBumpAvailable);
        Assert.Equal(features.Elytra, actual.ElytraAvailable);
        Assert.Equal(features.SwimPose, actual.SwimPoseAvailable);
        Assert.Equal(features.CrawlPose, actual.CrawlPoseAvailable);

        // These two fields follow the documented derivation rather than direct dataset values.
        PoseDimensionsEra expectedPose = features.CrawlPose ? PoseDimensionsEra.ModernCrawl : PoseDimensionsEra.PreCrawl;
        Assert.Equal(expectedPose, actual.PoseDimensions);

        PositionSendCadence expectedCadence = protocol <= 47 ? PositionSendCadence.Legacy : PositionSendCadence.DirtyChecked;
        Assert.Equal(expectedCadence, actual.PositionSend);
    }

    /// <summary>
    /// Pins the dataset's <c>fluidMovement</c>, stating the expectation independently of the dataset so this is not the tautology <see cref="ProductionProfile_MatchesDataset_ForEveryProtocol"/> necessarily is.
    /// <para>The expectation is a constant, and that IS the vanilla finding: the only behavior <see cref="FluidMovementEra"/> gates in the engine is <c>PlayerPhysics.TravelInWater</c>'s depth-strider / water-movement-efficiency blend of the water <c>slowDown</c> and <c>speed</c> scalars, and that blend - the <c>0.54600006F</c> target with its <c>3.0F</c> depth-strider cap, halved while off-ground - is unchanged across every protocol UMPK supports. The 1.8.4 and 1.9 water branches are identical statement for statement, so there is no 47-vs-107 boundary here for the dataset to encode.</para>
    /// <para>Note what this does NOT claim: 1.8's water travel does differ from 1.13+ vanilla elsewhere (modern uses <c>isSprinting() ? 0.9F : 0.8F</c> for the base slowdown and <c>getFluidFallingAdjustedMovement</c> rather than a flat <c>motionY -= 0.02</c>). That is a separate axis with a real boundary, pinned by <see cref="DatasetWaterTravel_MatchesVanilla_ForEveryProtocol"/>.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllProtocols))]
    public void DatasetFluidMovement_MatchesVanilla_ForEveryProtocol(int protocol)
    {
        JavaVersion version = JavaVersions.All.Single(v => v.Version.Protocol == protocol);

        Assert.Equal("swimmingUpdate", version.Features.FluidMovement);
    }

    /// <summary>The vanilla-derived expectation for every supported protocol's <c>waterTravel</c> value, written out as a literal table so the dataset can never drift silently: nothing here reads <c>features.json</c>, <c>ProtocolFeatures.WaterTravel</c>, <see cref="PhysicsProfileFactory"/> or <see cref="PhysicsProfile"/>. Same independence contract as <see cref="DatasetFluidMovement_MatchesVanilla_ForEveryProtocol"/>, but with a table that is not degenerate - it encodes a real boundary, so a wrong value on either side of 340/393 fails.</summary>
    public static TheoryData<int, string> WaterTravelExpectations() => new()
    {
        // 1.8-1.12.2: flat 0.8F slowdown with no sprinting arm, and a flat 0.02 vertical sink. Legacy water travel uses the player's water slowdown of 0.8.
        { 47, "legacy" },    // 1.8 - 1.8.9
        { 107, "legacy" },   // 1.9
        { 108, "legacy" },   // 1.9.1
        { 109, "legacy" },   // 1.9.2
        { 110, "legacy" },   // 1.9.3 / 1.9.4
        { 210, "legacy" },   // 1.10.x
        { 315, "legacy" },   // 1.11
        { 316, "legacy" },   // 1.11.1 / 1.11.2
        { 335, "legacy" },   // 1.12
        { 338, "legacy" },   // 1.12.1
        { 340, "legacy" },   // 1.12.2  <-- last legacy protocol

        // 1.13+: sprinting selects 0.9F instead of the normal water slowdown. Fluid-falling vertical adjustment begins in 1.14.
        { 393, "sprintAware" },   // 1.13   <-- first sprint-aware protocol
        { 401, "sprintAware" },   // 1.13.1
        { 404, "sprintAware" },   // 1.13.2
        { 477, "sprintAware" },   // 1.14
        { 480, "sprintAware" },   // 1.14.1
        { 485, "sprintAware" },   // 1.14.2
        { 490, "sprintAware" },   // 1.14.3
        { 498, "sprintAware" },   // 1.14.4
        { 573, "sprintAware" },   // 1.15
        { 575, "sprintAware" },   // 1.15.1
        { 578, "sprintAware" },   // 1.15.2
        { 735, "sprintAware" },   // 1.16
        { 736, "sprintAware" },   // 1.16.1
        { 751, "sprintAware" },   // 1.16.2
        { 753, "sprintAware" },   // 1.16.3
        { 754, "sprintAware" },   // 1.16.4 / 1.16.5
        { 755, "sprintAware" },   // 1.17
        { 756, "sprintAware" },   // 1.17.1
        { 757, "sprintAware" },   // 1.18 / 1.18.1
        { 758, "sprintAware" },   // 1.18.2
        { 759, "sprintAware" },   // 1.19
        { 760, "sprintAware" },   // 1.19.2
        { 761, "sprintAware" },   // 1.19.3
        { 762, "sprintAware" },   // 1.19.4
        { 763, "sprintAware" },   // 1.20 / 1.20.1
        { 764, "sprintAware" },   // 1.20.2
        { 765, "sprintAware" },   // 1.20.3 / 1.20.4
        { 766, "sprintAware" },   // 1.20.5 / 1.20.6
        { 767, "sprintAware" },   // 1.21 / 1.21.1
        { 768, "sprintAware" },   // 1.21.2 / 1.21.3
        { 769, "sprintAware" },   // 1.21.4
        { 770, "sprintAware" },   // 1.21.5
        { 771, "sprintAware" },   // 1.21.6
        { 772, "sprintAware" },   // 1.21.7 / 1.21.8
        { 773, "sprintAware" },   // 1.21.9
        { 774, "sprintAware" },   // 1.21.10
        { 775, "sprintAware" },   // 26.1
        { 776, "sprintAware" },   // 26.2
    };

    [Theory]
    [MemberData(nameof(WaterTravelExpectations))]
    public void DatasetWaterTravel_MatchesVanilla_ForEveryProtocol(int protocol, string expected)
    {
        JavaVersion version = JavaVersions.All.Single(v => v.Version.Protocol == protocol);

        Assert.Equal(expected, version.Features.WaterTravel);
    }

    /// <summary>Guards the literal table above against silently losing coverage: it must name every supported protocol exactly once. Without this, deleting a row would make the pin quietly weaker rather than red.</summary>
    [Fact]
    public void WaterTravelExpectations_CoverEveryShippedProtocol()
    {
        int[] tabled = [.. WaterTravelExpectations().Select(row => (int)row[0]).Order()];
        int[] shipped = [.. JavaVersions.All.Select(v => v.Version.Protocol).Order()];

        Assert.Equal(shipped, tabled);
    }

    /// <summary>
    /// The vanilla-derived expectation for every supported protocol's <c>waterClimbBump</c> value, written out as a literal table for the same reason <see cref="WaterTravelExpectations"/> is: nothing here reads <c>features.json</c>, <c>ProtocolFeatures.WaterClimbBump</c>, <see cref="PhysicsProfileFactory"/> or <see cref="PhysicsProfile"/>, so a dataset that drifts to the wrong side of the boundary is red rather than silently self-consistent.
    /// <para>The clause gated is vanilla's in-water climbable bump: <c>horizontalCollision &amp;&amp; onClimbable</c> replaces the vertical delta movement with <c>0.2</c> just before the <c>(slowDown, 0.8F, slowDown)</c> damping multiply. It is absent through 1.12.1 and present from 1.14.4 onward. Protocols 393, 401, and 404 do not apply the <c>0.2</c> bump.</para>
    /// <para>The boundary is 404 -&gt; 477, deliberately NOT the 340 -&gt; 393 of <see cref="WaterTravelExpectations"/>: protocols 393/401/404 are <c>sprintAware</c> AND bump-free, which is why this is its own axis rather than a widening of that one.</para>
    /// </summary>
    public static TheoryData<int, bool> WaterClimbBumpExpectations() => new()
    {
        // 1.8-1.13.2: the water arm goes straight from movement to the damping multipliers.
        { 47, false },   // 1.8 - 1.8.9
        { 107, false },   // 1.9
        { 108, false },   // 1.9.1
        { 109, false },   // 1.9.2
        { 110, false },   // 1.9.3 / 1.9.4
        { 210, false },   // 1.10.x
        { 315, false },   // 1.11
        { 316, false },   // 1.11.1 / 1.11.2
        { 335, false },   // 1.12
        { 338, false },   // 1.12.1
        { 340, false },   // 1.12.2
        { 393, false },   // 1.13
        { 401, false },   // 1.13.1
        { 404, false },   // 1.13.2  <-- LAST protocol without the bump

        // The clause exists from 1.14 onward.
        { 477, true },    // 1.14  <-- FIRST protocol with the bump
        { 480, true },    // 1.14.1
        { 485, true },    // 1.14.2
        { 490, true },    // 1.14.3
        { 498, true },    // 1.14.4
        { 573, true },    // 1.15
        { 575, true },    // 1.15.1
        { 578, true },    // 1.15.2
        { 735, true },    // 1.16
        { 736, true },    // 1.16.1
        { 751, true },    // 1.16.2
        { 753, true },    // 1.16.3
        { 754, true },    // 1.16.4 / 1.16.5
        { 755, true },    // 1.17
        { 756, true },    // 1.17.1
        { 757, true },    // 1.18 / 1.18.1
        { 758, true },    // 1.18.2
        { 759, true },    // 1.19
        { 760, true },    // 1.19.2
        { 761, true },    // 1.19.3
        { 762, true },    // 1.19.4
        { 763, true },    // 1.20 / 1.20.1
        { 764, true },    // 1.20.2
        { 765, true },    // 1.20.3 / 1.20.4
        { 766, true },    // 1.20.5 / 1.20.6
        { 767, true },    // 1.21 / 1.21.1
        { 768, true },    // 1.21.2 / 1.21.3
        { 769, true },    // 1.21.4
        { 770, true },    // 1.21.5
        { 771, true },    // 1.21.6
        { 772, true },    // 1.21.7 / 1.21.8
        { 773, true },    // 1.21.9
        { 774, true },    // 1.21.10
        { 775, true },    // 26.1
        { 776, true },    // 26.2
    };

    [Theory]
    [MemberData(nameof(WaterClimbBumpExpectations))]
    public void DatasetWaterClimbBump_MatchesVanilla_ForEveryProtocol(int protocol, bool expected)
    {
        JavaVersion version = JavaVersions.All.Single(v => v.Version.Protocol == protocol);

        Assert.Equal(expected, version.Features.WaterClimbBump);
    }

    /// <summary>The same coverage guard <see cref="WaterTravelExpectations_CoverEveryShippedProtocol"/> gives the water-travel table: the climbable-bump table must name every supported protocol exactly once.</summary>
    [Fact]
    public void WaterClimbBumpExpectations_CoverEveryShippedProtocol()
    {
        int[] tabled = [.. WaterClimbBumpExpectations().Select(row => (int)row[0]).Order()];
        int[] shipped = [.. JavaVersions.All.Select(v => v.Version.Protocol).Order()];

        Assert.Equal(shipped, tabled);
    }
}
