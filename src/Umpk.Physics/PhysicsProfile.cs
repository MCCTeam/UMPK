namespace Umpk.Physics;

/// <summary>The fluid-movement era selector (the <c>FluidMovement</c> toggle). Named for the dataset's swimming-update feature flag, but the one thing it currently gates in the engine (<c>PlayerPhysics.TravelInWater</c>'s depth-strider water-movement-efficiency blend) is not actually a 1.13 change - see <see cref="PhysicsProfile.ForProtocol"/>'s remarks for the behavior evidence that this blend has existed unchanged since Depth Strider shipped in 1.8. Swim POSES (a real 1.13 addition) are a separate field, <see cref="PhysicsProfile.SwimPoseAvailable"/>, not driven by this toggle.</summary>
public enum FluidMovementEra
{
    /// <summary>The pre-blend water friction/acceleration model (no depth-strider blend applied).</summary>
    Legacy = 0,

    /// <summary>The water model with the depth-strider water-movement-efficiency blend applied.</summary>
    SwimmingUpdate = 1,
}

/// <summary>
/// The water-travel era selector (the <c>WaterTravel</c> toggle). Unlike <see cref="FluidMovementEra"/>, which gates only the depth-strider blend and is the swimming-update feature for every supported protocol; this axis gates the two constants in <c>PlayerPhysics.TravelInWater</c> that vanilla really did change at 1.13: the horizontal water slow-down gains a sprinting arm, and the flat vertical sink is replaced by the fluid-falling adjustment.
///
/// <para>The boundary is protocol 340 to 393 (1.12.2 to 1.13). Legacy protocols use a flat <c>0.8F</c> slowdown and <c>motionY -= 0.02</c>. Modern protocols use a 0.9 sprint slowdown or the ordinary water slowdown, plus the fluid-falling adjustment.</para>
/// </summary>
public enum WaterTravelEra
{
    /// <summary>Protocols 47-340 (1.8-1.12.2): flat <c>0.8F</c> horizontal slow-down with no sprint arm, and a flat <c>-0.02</c> vertical sink.</summary>
    Legacy = 0,

    /// <summary>Protocols 393+ (1.13+): a 0.9 sprint slowdown or 0.8 otherwise, plus the modern vertical tail.</summary>
    SprintAware = 1,
}

/// <summary>The pose-dimension era (the <c>PoseDimensionsEra</c> toggle). Controls crouch height (1.65 pre-1.14, 1.5 after) and whether swim/crawl boxes exist.</summary>
public enum PoseDimensionsEra
{
    /// <summary>Pre-1.14: crouch height 1.65, no crawl box.</summary>
    PreCrawl = 0,

    /// <summary>1.14+: crouch height 1.5, swim/crawl boxes 0.6 tall.</summary>
    ModernCrawl = 1,
}

/// <summary>The position-send cadence era (the <c>PositionSendCadence</c> toggle). The engine does not send packets, but exposes this so the integration layer can pick the right cadence without re-deriving it from the protocol version.</summary>
public enum PositionSendCadence
{
    /// <summary>Pre-1.9 legacy send-every-tick cadence.</summary>
    Legacy = 0,

    /// <summary>1.9+ dirty-checked cadence with the 20-tick keepalive resend.</summary>
    DirtyChecked = 1,
}

/// <summary>
/// The per-version physics tuning the engine consumes. It carries the behavior toggles for mechanics that changed across 1.8-26.2 plus the pose dimensions for the era. Constant scalars that never changed across the supported range live in <see cref="PhysicsConstants"/>; only values that actually vary belong here.
///
/// <para>The shape mirrors the dataset's <c>features.json</c> physics-era fields; production builds one of these per session via <see cref="FromFeatures"/>, fed by the generated <c>Umpk.Data.Java</c>/<c>ProtocolFeatures</c> data (<c>Umpk.Client.Navigation.PhysicsProfileFactory</c> is the adapter - it lives in <c>Umpk.Client</c>, not here, so this project never takes a dependency on <c>Umpk.Protocol.Java</c>). A conformance test (<c>Umpk.Client.Tests.PhysicsProfileConformanceTests</c>) pins that every shipped protocol's production profile matches the dataset. Curated in-code defaults for protocols 47/770/776 are provided by <see cref="ForProtocol"/> for tests and bootstrap, documented as such.</para>
/// </summary>
public sealed record PhysicsProfile
{
    /// <summary>The fluid-movement era (depth-strider blend only).</summary>
    public required FluidMovementEra FluidMovement { get; init; }

    /// <summary>The water-travel era (sprint slow-down arm and the vertical sink model).</summary>
    public required WaterTravelEra WaterTravel { get; init; }

    /// <summary>
    /// Whether vanilla's water arm carries the in-water climbable bump (1.14+): when the player is horizontally colliding AND on a climbable block, the vertical delta movement is replaced by <c>0.2</c> just before the <c>(slowDown, 0.8F, slowDown)</c> damping multiply. It is what makes a ladder or vine usable while swimming.
    ///
    /// <para>The boundary is protocol 404 to 477 (1.13.2 to 1.14). Earlier protocols omit this branch; protocols from 1.14 onward apply the <c>0.2</c> vertical bump.</para>
    ///
    /// <para>This is deliberately NOT folded into <see cref="WaterTravel"/>: that axis's boundary is 340 -> 393, so protocols 393/401/404 are <see cref="WaterTravelEra.SprintAware"/> and still bump-free, and no ordering of one field can express both. It is equally deliberately not aliased onto <see cref="CrawlPoseAvailable"/>, which happens to share the 477 boundary: a coincident boundary is not a shared cause, and a future pose-only correction must not move water travel with it.</para>
    /// </summary>
    public required bool WaterClimbBumpAvailable { get; init; }

    /// <summary>Whether elytra (fall-flying) gliding is available (1.9+).</summary>
    public required bool ElytraAvailable { get; init; }

    /// <summary>Whether the swim pose is available (1.13+).</summary>
    public required bool SwimPoseAvailable { get; init; }

    /// <summary>Whether the crawl pose is available (1.14+).</summary>
    public required bool CrawlPoseAvailable { get; init; }

    /// <summary>The pose-dimension era (crouch height and swim/crawl boxes).</summary>
    public required PoseDimensionsEra PoseDimensions { get; init; }

    /// <summary>The position-send cadence era (advisory for the integration layer).</summary>
    public required PositionSendCadence PositionSend { get; init; }

    /// <summary>The player standing box height for this era.</summary>
    public double StandingHeight => PhysicsConstants.PlayerStandingHeight;

    /// <summary>The player crouch box height for this era.</summary>
    public double CrouchHeight => PoseDimensions == PoseDimensionsEra.ModernCrawl
        ? PhysicsConstants.PlayerCrouchHeightModern
        : PhysicsConstants.PlayerCrouchHeightLegacy;

    /// <summary>The player swim/crawl box height for this era.</summary>
    public double SwimHeight => PhysicsConstants.PlayerSwimHeight;

    /// <summary>The player box width for this era.</summary>
    public double Width => PhysicsConstants.PlayerWidth;

    /// <summary>Builds a profile from raw feature-flag values (the shape of <c>features.json</c>). Callers that hold a generated <c>ProtocolFeatures</c> (from <c>Umpk.Data.Java</c>) adapt it to these raw bools rather than this project taking a dependency on the protocol/data layers, so the engine never reads JSON - or <c>ProtocolFeatures</c> - directly. See <c>Umpk.Client.Navigation.PhysicsProfileFactory</c> for the production adapter and its field-by- field mapping (2 of these 8 flags - <paramref name="modernCrouchHeight"/> and <paramref name="dirtyCheckedPositionCadence"/> - have no direct <c>features.json</c> source and are derived there).</summary>
    public static PhysicsProfile FromFeatures(
        bool swimmingUpdate,
        bool sprintAwareWaterTravel,
        bool waterClimbBump,
        bool elytraAvailable,
        bool swimPoseAvailable,
        bool crawlPoseAvailable,
        bool modernCrouchHeight,
        bool dirtyCheckedPositionCadence) => new()
        {
            FluidMovement = swimmingUpdate ? FluidMovementEra.SwimmingUpdate : FluidMovementEra.Legacy,
            WaterTravel = sprintAwareWaterTravel ? WaterTravelEra.SprintAware : WaterTravelEra.Legacy,
            WaterClimbBumpAvailable = waterClimbBump,
            ElytraAvailable = elytraAvailable,
            SwimPoseAvailable = swimPoseAvailable,
            CrawlPoseAvailable = crawlPoseAvailable,
            PoseDimensions = modernCrouchHeight ? PoseDimensionsEra.ModernCrawl : PoseDimensionsEra.PreCrawl,
            PositionSend = dirtyCheckedPositionCadence ? PositionSendCadence.DirtyChecked : PositionSendCadence.Legacy,
        };

    /// <summary>The <see cref="PositionSendCadence"/> for a protocol version, mirroring the <see cref="ForProtocol"/>/<c>WorldBorderState.ContainmentEraForProtocol</c> convention: a plain protocol-number switch, computed once by whoever knows the session's protocol. There is no <c>features.json</c> field for this because it is client send-loop behavior, not a wire or world fact.</summary>
    public static PositionSendCadence PositionSendCadenceForProtocol(int protocol) =>
        protocol <= 47 ? PositionSendCadence.Legacy : PositionSendCadence.DirtyChecked;

    /// <summary>
    /// Curated in-code default profile for a protocol version. Provided for tests and bootstrap only; production consumers get the profile via <c>Umpk.Client.Navigation.PhysicsProfileFactory</c>, built from the generated <c>Umpk.Data.Java</c>/<c>ProtocolFeatures</c> data through <see cref="FromFeatures"/> (see <c>PhysicsProfileConformanceTests</c> in <c>Umpk.Client.Tests</c> for the per-protocol proof). Covers the three reference protocols: 47 (1.8), 770 (1.21.5), 776 (26.2). Any newer protocol resolves to the 1.13+ modern defaults.
    /// <para>KNOWN, deliberate divergence from the verified dataset: protocols 47-392 (1.8-1.12.2) carry <see cref="FluidMovementEra.Legacy"/> here, but the dataset (and therefore production) correctly gives them <see cref="FluidMovementEra.SwimmingUpdate"/> - the depth-strider water blend that flag gates has been present, unchanged, since Depth Strider shipped in 1.8. The dataset value therefore uses the swimming-update feature from protocol 47 onward, as checked by <c>PhysicsProfileConformanceTests.DatasetFluidMovement_MatchesVanilla_ForEveryProtocol</c>. This curated table is intentionally simplified and pinned by <c>PhysicsProfileEraTests.ForProtocol_PinsFeatureEraBreakpoints</c>; it is not a second source of truth for production behavior.</para>
    /// </summary>
    public static PhysicsProfile ForProtocol(int protocolVersion) => protocolVersion switch
    {
        // 1.8 (protocol 47): pre-swimming, no elytra, legacy crouch height 1.65, legacy cadence.
        <= 47 => new PhysicsProfile
        {
            FluidMovement = FluidMovementEra.Legacy,
            WaterTravel = WaterTravelEra.Legacy,
            WaterClimbBumpAvailable = false,
            ElytraAvailable = false,
            SwimPoseAvailable = false,
            CrawlPoseAvailable = false,
            PoseDimensions = PoseDimensionsEra.PreCrawl,
            PositionSend = PositionSendCadence.Legacy,
        },
        // 1.9-1.13 boundary (protocols 48-392): elytra + dirty cadence, still legacy fluid/pose.
        < 393 => new PhysicsProfile
        {
            FluidMovement = FluidMovementEra.Legacy,
            WaterTravel = WaterTravelEra.Legacy,
            WaterClimbBumpAvailable = false,
            ElytraAvailable = true,
            SwimPoseAvailable = false,
            CrawlPoseAvailable = false,
            PoseDimensions = PoseDimensionsEra.PreCrawl,
            PositionSend = PositionSendCadence.DirtyChecked,
        },
        // 1.13 .. 1.14 boundary: swimming update + swim pose, crouch still 1.65 until 1.14.
        < 477 => new PhysicsProfile
        {
            FluidMovement = FluidMovementEra.SwimmingUpdate,
            WaterTravel = WaterTravelEra.SprintAware,
            WaterClimbBumpAvailable = false,
            ElytraAvailable = true,
            SwimPoseAvailable = true,
            CrawlPoseAvailable = false,
            PoseDimensions = PoseDimensionsEra.PreCrawl,
            PositionSend = PositionSendCadence.DirtyChecked,
        },
        // 1.14+ (>= 477), including 770 (1.21.5) and 776 (26.2): full modern feature set.
        _ => Modern,
    };

    /// <summary>The 1.14+ modern default profile (full feature set).</summary>
    public static PhysicsProfile Modern { get; } = new()
    {
        FluidMovement = FluidMovementEra.SwimmingUpdate,
        WaterTravel = WaterTravelEra.SprintAware,
        WaterClimbBumpAvailable = true,
        ElytraAvailable = true,
        SwimPoseAvailable = true,
        CrawlPoseAvailable = true,
        PoseDimensions = PoseDimensionsEra.ModernCrawl,
        PositionSend = PositionSendCadence.DirtyChecked,
    };
}
