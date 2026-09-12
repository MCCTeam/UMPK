namespace Umpk.Physics;

/// <summary>Physics scalars that are constant across the whole 1.8-26.2 supported range. Values that vary by era live on <see cref="PhysicsProfile"/> instead.</summary>
public static class PhysicsConstants
{
    // Player dimensions. Crouch height varies by era.

    /// <summary>Player box width (all poses).</summary>
    public const double PlayerWidth = 0.6;

    /// <summary>Standing box height.</summary>
    public const double PlayerStandingHeight = 1.8;

    /// <summary>Standing eye height.</summary>
    public const double PlayerStandingEyeHeight = 1.62;

    /// <summary>Crouch box height, 1.14+.</summary>
    public const double PlayerCrouchHeightModern = 1.5;

    /// <summary>Crouch box height, pre-1.14.</summary>
    public const double PlayerCrouchHeightLegacy = 1.65;

    /// <summary>Crouch eye height.</summary>
    public const double PlayerCrouchEyeHeight = 1.27;

    /// <summary>Swim/crawl/elytra box height.</summary>
    public const double PlayerSwimHeight = 0.6;

    /// <summary>Swim/crawl/elytra eye height.</summary>
    public const double PlayerSwimEyeHeight = 0.4;

    // Gravity

    /// <summary>Default player gravity (Attributes.GRAVITY default).</summary>
    public const double DefaultGravity = 0.08;

    /// <summary>Slow-falling caps the effective downward gravity at this value.</summary>
    public const double SlowFallingCap = 0.01;

    // Step height

    /// <summary>Max auto step-up height (blocks).</summary>
    public const float StepHeight = 0.6f;

    // Friction and drag

    /// <summary>Horizontal air/ground drag base factor (blockFriction * 0.91).</summary>
    public const float FrictionMultiplier = 0.91f;

    /// <summary>Vertical drag applied after gravity for a normal air mover.</summary>
    public const float DragY = 0.98f;

    /// <summary>Input friction applied to raw movement input.</summary>
    public const float InputFriction = 0.98f;

    /// <summary>Ground acceleration numerator: speed * 0.216 / friction^3 (getFrictionInfluencedSpeed).</summary>
    public const float GroundAccelerationFactor = 0.21600002f;

    /// <summary>Friction threshold above which ground acceleration is scaled (getFrictionInfluencedSpeed).</summary>
    public const float FrictionSpeedThreshold = 0.6f;

    /// <summary>Flat air acceleration for a non-flying entity (getFlyingSpeed base).</summary>
    public const float AirSpeed = 0.02f;

    /// <summary>Flat air acceleration for a sprinting non-flying player.</summary>
    public const float SprintingAirSpeed = 0.025999999f;

    /// <summary>The sprint movement-speed modifier's amount: vanilla's <c>0.3F</c> widened to double, which is what attribute calculation folds in.</summary>
    /// <remarks>
    /// <para>The sprint modifier has amount <c>0.3F</c> and operation <c>ADD_MULTIPLIED_TOTAL</c>. That operation multiplies the running total by <c>1 + amount</c>, so the resolved speed is <c>base * 1.3</c>, never <c>base + 0.03</c>.</para>
    /// <para>There is no era split. Earlier protocols use the same amount and multiply-total operation.</para>
    /// <para>The value is spelled as the widened double rather than as <c>0.3</c> because vanilla's fold is a double fold of a float literal, and the two differ: <c>(double)0.3f</c> is 0.30000001192092896, so <c>1 + amount</c> is 1.300000011920929 rather than 1.3. Folding in float instead (<c>speed * 1.3f</c>) lands one ulp low for most attribute values.</para>
    /// </remarks>
    public const double SprintSpeedModifier = 0.30000001192092896;

    // Water

    /// <summary>Base water slow-down when not sprinting (getWaterSlowDown).</summary>
    public const float WaterSlowDown = 0.8f;

    /// <summary>Water slow-down when sprinting.</summary>
    public const float WaterSprintSlowDown = 0.9f;

    /// <summary>Water-walker (efficiency) blend target for slow-down.</summary>
    public const float WaterWalkerSlowDownTarget = 0.54600006f;

    /// <summary>Dolphins-grace slow-down override.</summary>
    public const float DolphinsGraceSlowDown = 0.96f;

    /// <summary>Base water travel speed.</summary>
    public const float WaterBaseSpeed = 0.02f;

    /// <summary>Vertical water damping.</summary>
    public const float WaterYDamping = 0.8f;

    /// <summary>The pre-1.13 flat vertical water sink (<c>motionY -= 0.02</c>), applied instead of the modern fluid-falling adjustment on <see cref="WaterTravelEra.Legacy"/>.</summary>
    public const double LegacyWaterSink = 0.02;

    /// <summary>Upward impulse when jumping in a fluid.</summary>
    public const double FluidJumpImpulse = 0.04;

    /// <summary>Downward impulse when descending in water (goDownInWater).</summary>
    public const double WaterDescendImpulse = 0.04;

    /// <summary>The fluid depth at or below which a jump is a full ground jump instead of the liquid impulse (0.4 when the eye height is at least 0.4, otherwise 0).</summary>
    public const double FluidJumpThreshold = 0.4;

    /// <summary>The eye height below which the fluid jump threshold collapses to 0 (getFluidJumpThreshold).</summary>
    public const double FluidJumpThresholdMinEyeHeight = 0.4;

    /// <summary>The margin by which the fluid interaction box is deflated.</summary>
    public const double FluidInteractionBoxDeflate = 0.001;

    /// <summary>The vertical probe offset of the fluid ledge hop. The collision box is raised by 0.6 above where the next tick's movement would leave it. Kept as the widened <c>float</c> literal used by the game, not a plain 0.6.</summary>
    public const double FluidLedgeProbeUp = 0.6f;

    /// <summary>The vertical velocity a player gets when hopping out of a fluid over a ledge. Present in every supported era and in both the water and lava arms, so it is not era-gated.</summary>
    public const double FluidLedgeHop = 0.3f;

    /// <summary>The per-level fraction of a fluid amount step: amount divided by 9.</summary>
    public const double FluidHeightPerAmount = 1.0 / 9.0;

    // Fluid current pushing

    /// <summary>The per-tick scale applied to the averaged water flow vector for water current pushing.</summary>
    public const double WaterPushScale = 0.014;

    /// <summary>The per-tick scale applied to the averaged lava flow vector outside an ultra-warm dimension for fluid current pushing.</summary>
    public const double LavaPushScale = 0.0023333333333333335;

    /// <summary>The same scale inside an ultra-warm dimension (the Nether).</summary>
    public const double LavaPushScaleUltraWarm = 0.007;

    /// <summary>Below this fluid depth, multiply the per-cell flow contribution by the depth itself.</summary>
    public const double FluidPushDepthScaleThreshold = 0.4;

    /// <summary>The magnitude the scaled push is raised to when the entity is essentially stationary and the push would otherwise be smaller (vanilla literal 0.0045000000000000005).</summary>
    public const double FluidPushMinimum = 0.0045000000000000005;

    /// <summary>The vertical component added to a FALLING fluid's flow when it runs against a solid face before the final normalization.</summary>
    public const double FluidFallingFlowDownward = -6.0;

    /// <summary>The own-height offset a fluid one level below an open neighbour contributes to the flow (<c>0.8888889F</c>, the float spelling of 8/9).</summary>
    public const float FluidBelowNeighbourOffset = 0.8888889f;

    /// <summary>The lowest water or lava level value that means a falling fluid state.</summary>
    public const int FluidFallingLevel = 8;

    // Lava

    /// <summary>Lava travel speed.</summary>
    public const float LavaSpeed = 0.02f;

    /// <summary>Lava horizontal/vertical damping in shallow lava.</summary>
    public const double LavaShallowDamping = 0.5;

    /// <summary>Lava vertical damping in shallow lava.</summary>
    public const double LavaShallowVerticalDamping = 0.8;

    /// <summary>Lava scale in deep lava.</summary>
    public const double LavaDeepScale = 0.5;

    // Jump

    /// <summary>Default jump strength attribute value (Attributes.JUMP_STRENGTH default).</summary>
    public const float BaseJumpPower = 0.42f;

    /// <summary>Horizontal sprint-jump boost magnitude.</summary>
    public const double SprintJumpHorizontalBoost = 0.2;

    /// <summary>Jump-boost power per amplifier level.</summary>
    public const float JumpBoostPerLevel = 0.1f;

    /// <summary>Anti-spam jump delay in ticks.</summary>
    public const int JumpDelayTicks = 10;

    // Climb

    /// <summary>Max horizontal/vertical climb speed magnitude.</summary>
    public const float ClimbMaxSpeed = 0.15f;

    /// <summary>Upward bump when pressing into a climbable while colliding/jumping.</summary>
    public const double ClimbWallBump = 0.2;

    // Velocity zeroing thresholds

    /// <summary>Player horizontal velocity zeroing threshold, squared (&lt; 0.003 length).</summary>
    public const double PlayerHorizontalVelocityThresholdSqr = 9.0E-6;

    /// <summary>Non-player per-axis velocity zeroing threshold.</summary>
    public const double AxisVelocityThreshold = 0.003;

    // Collision epsilon

    /// <summary>Collision search/inflation epsilon.</summary>
    public const double CollisionEpsilon = 1.0E-7;

    /// <summary>Blocked-axis comparison epsilon.</summary>
    public const double BlockedAxisEpsilon = 1.0E-5;

    // Blocks containing the body

    /// <summary>The per-axis multiplier a cobweb arms on the body it holds: <c>new Vec3(0.25, 0.05F, 0.25)</c>. The Y term is a float widened to <c>0.05000000074505806</c>; keeping that exact double makes the engine bit-comparable with the server.</summary>
    public static readonly Geometry.Vec3d WebStuckSpeedMultiplier = new(0.25, 0.05000000074505806, 0.25);

    /// <summary>The per-tick lift an UPWARD bubble column gives a body inside it, and the speed that lift is capped at: <c>vy = min(0.7, vy + 0.06)</c>.</summary>
    public const double BubbleColumnInsideUp = 0.06;

    /// <inheritdoc cref="BubbleColumnInsideUp"/>
    public const double BubbleColumnInsideMaxUp = 0.7;

    /// <summary>The same for a body at the column's MOUTH, i.e. with an open cell above it: <c>vy = min(1.8, vy + 0.1)</c>. This arm does NOT reset the fall distance; only the inside arm does.</summary>
    public const double BubbleColumnAboveUp = 0.1;

    /// <inheritdoc cref="BubbleColumnAboveUp"/>
    public const double BubbleColumnAboveMaxUp = 1.8;

    // Below-feet sampling offset

    /// <summary>Vertical offset used to sample the block affecting movement below the feet.</summary>
    public const double BelowFeetOffset = 0.500001;

    // Elytra

    /// <summary>Elytra horizontal drag per tick.</summary>
    public const double ElytraHorizontalDrag = 0.99;

    /// <summary>Elytra vertical drag per tick.</summary>
    public const double ElytraVerticalDrag = 0.98;

    // Sneak edge back-off

    /// <summary>Edge back-off probing step.</summary>
    public const double EdgeBackoffStep = 0.05;

    // Sprint-swim ascend and descend

    /// <summary>Swim vertical steer multiplier when looking down hard.</summary>
    public const double SwimSteerMultiplierSteep = 0.085;

    /// <summary>Swim vertical steer multiplier (normal).</summary>
    public const double SwimSteerMultiplier = 0.06;

    /// <summary>Look-angle Y threshold below which the steep swim multiplier applies.</summary>
    public const double SwimSteerSteepThreshold = -0.2;

    // Creative and spectator flight

    /// <summary>Vertical damping applied to creative-fly Y after the parent travel.</summary>
    public const double FlyVerticalDamping = 0.6;

    /// <summary>Default fly speed.</summary>
    public const float DefaultFlySpeed = 0.05f;

    /// <summary>The crouch factor for a player with no swift sneak is <c>0.3F</c>, which from 1.21 is also the <c>minecraft:sneaking_speed</c> attribute's registry default with a default of 0.3 and bounds from 0.0 to 1.0. The two eras agree, so this is one constant rather than an era table.</summary>
    /// <remarks>This is the SEED, not the value the engine uses. What the engine uses is <see cref="PhysicsConditions.SneakingSpeedFactor"/>, which a host resolves from the enchantment or the attribute and pushes. Anything predicting the engine's crouch behaviour must read that, never this.</remarks>
    public const float DefaultSneakingSpeedFactor = 0.3f;
}
