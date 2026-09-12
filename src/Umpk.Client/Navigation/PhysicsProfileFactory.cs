using Umpk.Physics;
using Umpk.Protocol.Java;

namespace Umpk.Client.Navigation;

/// <summary>Builds the production <see cref="PhysicsProfile"/> from a session's <see cref="JavaVersion"/>. This is the field-by-field mapping from the dataset's generated <see cref="ProtocolFeatures"/> (built by <c>Umpk.Data.Java</c> from <c>features.json</c>) onto <see cref="PhysicsProfile.FromFeatures"/>'s six raw flags. The mapping lives here - in <c>Umpk.Client</c>, which already references both <c>Umpk.Physics</c> and <c>Umpk.Protocol.Java</c> - rather than in either of those two projects, so neither takes a dependency on the other (<see cref="PhysicsProfile.FromFeatures"/>'s own remarks: "the engine never reads JSON... directly"). <see cref="PhysicsEngineHolder"/> is the sole production caller; <c>Umpk.Client.Tests.PhysicsProfileConformanceTests</c> exercises this for every protocol in <c>JavaVersions.All</c> and pins it against the dataset.</summary>
internal static class PhysicsProfileFactory
{
    /// <summary>
    /// Maps <paramref name="version"/>'s <see cref="ProtocolFeatures"/> onto a <see cref="PhysicsProfile"/>. Four of <see cref="PhysicsProfile.FromFeatures"/>'s six flags have a direct <c>features.json</c> source and are passed straight through (fluidMovement, elytra, swimPose, crawlPose). The other two have NO direct dataset field; this is where that gap is closed, and the reasoning is stated here rather than left implicit:
    /// <list type="bullet">
    /// <item><description>
    /// <c>modernCrouchHeight</c> is derived from <see cref="ProtocolFeatures.CrawlPose"/>. The 1.14 hitbox rewrite introduced the pose-keyed dimension table that carries both the crawl pose and the 1.5-block crouch height together; no shipped version has one of those without the other, and <see cref="PhysicsProfile.ForProtocol"/>'s own pre-existing switch already treats them as a single boundary (protocol 477). The dataset's <c>physics.poseBoundingBoxEra</c> field is NOT a substitute: it cuts at 1.9 (pre1.9/modern), a different boundary entirely, and nothing in the codebase reads it.
    /// </description></item>
    /// <item><description>
    /// <c>dirtyCheckedPositionCadence</c> is derived from <see cref="PhysicsProfile.PositionSendCadenceForProtocol"/>. The position-send cadence is a client send-loop behavior, not a wire/world fact the dataset captures at all, so it is computed straight from the protocol number - mirroring <see cref="PhysicsProfile.ForProtocol"/>'s own pre-existing boundary rather than a new one.
    /// </description></item>
    /// </list>
    /// </summary>
    public static PhysicsProfile FromJavaVersion(JavaVersion version)
    {
        ProtocolFeatures features = version.Features;

        // The dataset's fluidMovement flag is a 2-value string ("legacy"/"swimmingUpdate"), the same shape ProtocolFeatures itself already translates for other flags (see e.g. its PosLayout property: equals "legacy" -> the old value, else -> the modern one). Mirrored here, rather than added as a derived property on ProtocolFeatures, because Umpk.Physics owns FluidMovementEra, the enum this value feeds.
        bool swimmingUpdate = !string.Equals(features.FluidMovement, "legacy", StringComparison.Ordinal);

        // Same 2-value-string convention for the water-travel axis ("legacy"/"sprintAware"), which gates the sprint slow-down arm and the vertical sink model (boundary 340 -> 393).
        bool sprintAwareWaterTravel = !string.Equals(features.WaterTravel, "legacy", StringComparison.Ordinal);

        return PhysicsProfile.FromFeatures(
            swimmingUpdate: swimmingUpdate,
            sprintAwareWaterTravel: sprintAwareWaterTravel,
            waterClimbBump: features.WaterClimbBump,
            elytraAvailable: features.Elytra,
            swimPoseAvailable: features.SwimPose,
            crawlPoseAvailable: features.CrawlPose,
            modernCrouchHeight: features.CrawlPose,
            dirtyCheckedPositionCadence: PhysicsProfile.PositionSendCadenceForProtocol(version.Version.Protocol)
                == PositionSendCadence.DirtyChecked);
    }
}
