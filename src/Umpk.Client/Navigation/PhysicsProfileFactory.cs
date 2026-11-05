using Umpk.Physics;
using Umpk.Protocol.Java;

namespace Umpk.Client.Navigation;

/// <summary>Builds the session physics profile from generated movement features and the native pose and cadence selectors owned by <see cref="PhysicsProfile"/>.</summary>
internal static class PhysicsProfileFactory
{
    /// <summary>Maps the descriptor's movement flags and selects its player dimensions and position cadence.</summary>
    /// <remarks>Pose dimensions are independent of crawl availability: 1.8 sneaking retains the standing box, 1.9 shortens it and 1.14 introduces the pose table. The explicit profile selector keeps that boundary separate from bootstrap defaults.</remarks>
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
                == PositionSendCadence.DirtyChecked) with
        {
            PoseDimensions = PhysicsProfile.PoseDimensionsForProtocol(version.Version.Protocol),
        };
    }
}
