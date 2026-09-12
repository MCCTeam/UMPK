using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>
/// The player physics tick engine. One instance owns the live simulation state and steps it at 20 TPS through <see cref="Step"/>. The engine is a self-contained core: it reads the world only through <see cref="IPhysicsWorldView"/> and reads game state only from the <see cref="PhysicsConditions"/> the host pushes via <see cref="SetConditions"/>. It never reaches into client state, never logs, and never touches wall-clock or randomness (determinism).
///
/// <para>The engine uses a snapshot-and-step shape. It covers elytra, attribute-driven speed, sprint-swim, jump boost, block jump and speed factors, and entity colliders.</para>
/// </summary>
public sealed class PlayerPhysics : IPistonPushTarget
{
    private readonly IPhysicsWorldView _world;
    private PhysicsProfile _profile;
    private PhysicsConditions _conditions = PhysicsConditions.Default;

    private readonly ColliderBuffer _colliders = new();
    private readonly ColliderBuffer _stepColliders = new();
    private readonly ColliderBuffer _scratch = new();

    // Live working state
    private Vec3d _position;
    private Vec3d _velocity;
    private float _yaw;
    private float _pitch;
    private bool _onGround;
    private bool _horizontalCollision;
    private bool _verticalCollision;
    private bool _verticalCollisionBelow;
    private double _fallDistance;
    private Vec3d _stuckSpeedMultiplier = Vec3d.Zero;

    private EntityPose _pose = EntityPose.Standing;

    // Environment flags refreshed each tick.
    private bool _inWater;
    private bool _isUnderWater;
    private bool _inLava;
    private bool _onClimbable;

    /// <summary>Whether this tick's feet cell is scaffolding. Refreshed by <see cref="UpdateEnvironment"/>, which is the first thing <see cref="Step"/> does, so it is never read stale.</summary>
    private bool _inScaffolding;

    // Mapped input for the current tick.
    private float _xxa;
    private float _zza;
    private float _yya;
    private bool _jumping;
    private bool _sprinting;
    private bool _sneaking;

    private int _noJumpDelay;

    /// <summary>Creates an engine over a world view with a per-version profile.</summary>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public PlayerPhysics(IPhysicsWorldView world, PhysicsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        _world = world;
        _profile = profile;
    }

    /// <summary>The current physics snapshot.</summary>
    public PhysicsState State => Snapshot();

    /// <summary>The profile in effect. Hosts swap this at version/phase boundaries.</summary>
    public PhysicsProfile Profile
    {
        get => _profile;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _profile = value;
        }
    }

    /// <summary>The conditions last pushed by the host.</summary>
    public PhysicsConditions Conditions => _conditions;

    /// <summary>Full reset to a spawn or teleport position. Clears velocity, pose, fall distance, and all collision and environment flags.</summary>
    public void Reset(Vec3d position, float yaw, float pitch)
    {
        _position = position;
        _velocity = Vec3d.Zero;
        _yaw = yaw;
        _pitch = pitch;
        _onGround = false;
        _horizontalCollision = false;
        _verticalCollision = false;
        _verticalCollisionBelow = false;
        _fallDistance = 0;
        _stuckSpeedMultiplier = Vec3d.Zero;
        _pose = EntityPose.Standing;
        _inWater = false;
        _isUnderWater = false;
        _inLava = false;
        _waterHeight = 0;
        _lavaHeight = 0;
        _eyeInWater = false;
        _onClimbable = false;
        _inScaffolding = false;
        _xxa = 0;
        _zza = 0;
        _yya = 0;
        _jumping = false;
        _sprinting = false;
        _sneaking = false;
        _noJumpDelay = 0;
        _wasGliding = false;
        _bouncedThisTick = false;
        _lastLandingFallDistance = 0;
    }

    /// <summary>Pushes host-computed game conditions (abilities, effects, resolved attributes, equipment). Call whenever they change; the engine never pulls them.</summary>
    public void SetConditions(in PhysicsConditions conditions) => _conditions = conditions;

    /// <summary>Installs a full snapshot into the working state (used by <see cref="PhysicsSimulator"/> for forward prediction). Restores every field a <see cref="PhysicsState"/> carries; fields the snapshot does not carry (stuck multiplier, jump delay) reset to their neutral values.</summary>
    internal void LoadState(in PhysicsState state)
    {
        _position = state.Position;
        _velocity = state.Velocity;
        _yaw = state.Yaw;
        _pitch = state.Pitch;
        _onGround = state.OnGround;
        _horizontalCollision = state.HorizontalCollision;
        _verticalCollision = state.VerticalCollision;
        _verticalCollisionBelow = state.VerticalCollision && state.Velocity.Y < 0.0;
        _fallDistance = state.FallDistance;
        _pose = state.Pose;
        _inWater = state.InWater;
        _isUnderWater = state.IsUnderWater;
        _inLava = state.InLava;
        _waterHeight = state.InWater ? 1.0 : 0.0; // refreshed by the next Step's environment pass
        _lavaHeight = state.InLava ? 1.0 : 0.0;
        _eyeInWater = state.IsUnderWater;
        _onClimbable = state.OnClimbable;
        _inScaffolding = false; // refreshed by the next Step's environment pass, like _waterHeight above
        _stuckSpeedMultiplier = Vec3d.Zero;
        _noJumpDelay = 0;
        _bouncedThisTick = false;
        _lastLandingFallDistance = 0;
        _wasGliding = state.IsGliding;
    }

    /// <summary>Overwrites the current facing (host rotation input, independent of movement input).</summary>
    public void SetRotation(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = pitch;
    }

    /// <summary>Overwrites the current velocity. The host calls this after a server teleport that carries momentum the player is to keep. <see cref="Reset(Vec3d, float, float)"/> zeroes the velocity because it models a spawn; a teleport that hands the client momentum re-seeds it here.</summary>
    public void SetVelocity(Vec3d velocity) => _velocity = velocity;

    // Piston push

    /// <summary>Per-tick piston displacement, indexed by axis (0=X, 1=Y, 2=Z).</summary>
    private readonly double[] _pistonDeltas = new double[3];

    /// <summary>Clears the per-game-tick piston accumulator. The engine has no clock, so the host indicates when a tick begins. Call once per tick before ticking any <see cref="MovingPiston"/>.</summary>
    public void BeginPistonTick() => Array.Clear(_pistonDeltas);

    /// <inheritdoc/>
    Aabb IPistonPushTarget.BoundingBox => BoundingBox;

    /// <inheritdoc/>
    void IPistonPushTarget.MoveByPiston(Vec3d motion)
    {
        Vec3d limited = LimitPistonMovement(motion);
        if (limited == Vec3d.Zero)
            return;

        Move(limited, byPiston: true);
    }

    /// <summary>Clamps the total piston displacement an entity may take on one axis in one game tick to <see cref="MovingPiston.TickMovement"/>, and reduces the result to a single axis. Two pistons pushing the same entity in the same tick is the case this exists for.</summary>
    private Vec3d LimitPistonMovement(Vec3d motion)
    {
        if (motion.LengthSqr() <= 1.0E-7)
            return motion;

        if (motion.X != 0.0)
        {
            double x = ApplyPistonMovementRestriction(motion.X, 0);
            return Math.Abs(x) <= 1.0E-7 ? Vec3d.Zero : new Vec3d(x, 0.0, 0.0);
        }

        if (motion.Y != 0.0)
        {
            double y = ApplyPistonMovementRestriction(motion.Y, 1);
            return Math.Abs(y) <= 1.0E-7 ? Vec3d.Zero : new Vec3d(0.0, y, 0.0);
        }

        if (motion.Z != 0.0)
        {
            double z = ApplyPistonMovementRestriction(motion.Z, 2);
            return Math.Abs(z) <= 1.0E-7 ? Vec3d.Zero : new Vec3d(0.0, 0.0, z);
        }

        return Vec3d.Zero;
    }

    /// <summary>Applies the per-axis piston displacement restriction.</summary>
    private double ApplyPistonMovementRestriction(double amount, int axis)
    {
        double clamped = Math.Clamp(
            amount + _pistonDeltas[axis], -MovingPiston.TickMovement, MovingPiston.TickMovement);
        double allowed = clamped - _pistonDeltas[axis];
        _pistonDeltas[axis] = clamped;
        return allowed;
    }

    /// <summary>Runs one 20-TPS physics tick in compatibility order: environment sensing, pose update, tiny velocity zeroing, input mapping, jump handling, travel, and collision.</summary>
    public StepResult Step(in MovementInput input)
    {
        EntityPose poseBefore = _pose;
        bool onGroundBefore = _onGround;
        bool glidingBefore = _wasGliding;
        bool glidingNow = _conditions.ElytraFlying && _profile.ElytraAvailable;

        UpdateEnvironment();
        MapInput(input);
        UpdatePlayerPose();

        ZeroTinyVelocity();

        HandleJumping();

        var travelInput = new Vec3d(_xxa, _yya, _zza);
        Travel(travelInput);

        ApplyEffectsFromBlocks();

        if (_noJumpDelay > 0)
            _noJumpDelay--;

        bool landed = _onGround && !onGroundBefore;
        double landingFall = landed ? _fallDistance : 0;
        // Fall distance is zeroed inside Move when landing, so capture before that is not possible here; recompute the landing distance from the collision below is not needed because Move records it. We surface the pre-zero value tracked by _lastLandingFallDistance.
        if (landed)
            landingFall = _lastLandingFallDistance;

        var events = new StepEvents
        {
            Landed = landed,
            LandingFallDistance = landingFall,
            Bounced = _bouncedThisTick,
            StartedGliding = glidingNow && !glidingBefore,
            StoppedGliding = !glidingNow && glidingBefore,
            PoseChanged = _pose != poseBefore,
            PreviousPose = poseBefore,
        };
        _bouncedThisTick = false;
        _wasGliding = glidingNow;

        return new StepResult(Snapshot(), events);
    }

    private double _lastLandingFallDistance;
    private bool _bouncedThisTick;
    private bool _wasGliding;

    // Environment

    // Fluid-height sensing scans the block cells the deflated bounding box overlaps, compute each fluid cell's surface height, and track the max (fluidTop - boxMinY) per fluid plus whether the eye cell is inside its fluid. In-water state is equivalent to water height greater than zero; underwater state means the eye is in water.
    private double _waterHeight;
    private double _lavaHeight;
    private bool _eyeInWater;

    // Accumulated fluid current for this tick: one running sum and cell count per fluid. It is summed during the same cell scan that measures the heights, then applied by ApplyFluidPushing.
    private Vec3d _waterFlow;
    private int _waterFlowCells;
    private Vec3d _lavaFlow;
    private int _lavaFlowCells;

    private void UpdateEnvironment()
    {
        SenseFluids();
        _inWater = _waterHeight > 0.0;
        _isUnderWater = _eyeInWater && _inWater;
        _inLava = _lavaHeight > 0.0;
        ApplyFluidPushing();

        BlockState feet = _world.GetBlock(BlockPos.Containing(_position.X, _position.Y, _position.Z));
        _onClimbable = !feet.IsDefault && feet.IsClimbable;

        // Compute scaffolding contact here rather than in HandleOnClimbable because this is where the feet cell is already in hand: zero extra world reads, and BlockState.IsScaffolding short-circuits on the climbable flag before it ever compares a name.
        _inScaffolding = _onClimbable && feet.IsScaffolding;
    }

    /// <summary>Adds this tick's fluid current to the velocity, water first and then lava.</summary>
    /// <remarks>This runs from <see cref="UpdateEnvironment"/>, which is the first thing <see cref="Step"/> does, before tiny velocities are zeroed and before travel. The order is load-bearing: the minimum-push floor reads the pre-zeroing velocity, and the resulting 0.0045 impulse is above the 0.003 zeroing threshold, so a stationary player in a current starts moving instead of being zeroed back to rest every tick.</remarks>
    private void ApplyFluidPushing()
    {
        ApplyCurrent(_waterFlow, _waterFlowCells, PhysicsConstants.WaterPushScale);
        ApplyCurrent(
            _lavaFlow,
            _lavaFlowCells,
            _conditions.UltraWarmDimension
                ? PhysicsConstants.LavaPushScaleUltraWarm
                : PhysicsConstants.LavaPushScale);
    }

    private void ApplyCurrent(Vec3d accumulated, int cells, double scale)
    {
        // Vanilla 1.21.8 gates on `flow.length() > 0`; 26.x tightened it to `lengthSqr() >= 1.0E-5`. The two only differ for a flow that is nonzero but under 0.00316 long, which needs the per-cell unit vectors to cancel almost exactly; still water yields an exact zero on both.
        if (cells <= 0 || accumulated.LengthSqr() <= 0.0)
            return;

        // The local player is a Player, so the accumulated flow is AVERAGED over the cells; only non-player entities normalize it instead.
        Vec3d impulse = accumulated.Scale(1.0 / cells).Scale(scale);
        if (Math.Abs(_velocity.X) < PhysicsConstants.AxisVelocityThreshold
            && Math.Abs(_velocity.Z) < PhysicsConstants.AxisVelocityThreshold
            && impulse.Length() < PhysicsConstants.FluidPushMinimum)
            impulse = impulse.Normalize().Scale(PhysicsConstants.FluidPushMinimum);

        _velocity = _velocity.Add(impulse);
    }

    private void SenseFluids()
    {
        _waterHeight = 0.0;
        _lavaHeight = 0.0;
        _eyeInWater = false;
        _waterFlow = Vec3d.Zero;
        _waterFlowCells = 0;
        _lavaFlow = Vec3d.Zero;
        _lavaFlowCells = 0;

        // A flying player is not pushed by a current.
        bool pushedByFluid = !_conditions.CreativeFlying;

        // The fluid interaction box is the bounding box deflated by 0.001.
        Aabb box = BoundingBox.Deflate(
            PhysicsConstants.FluidInteractionBoxDeflate,
            PhysicsConstants.FluidInteractionBoxDeflate,
            PhysicsConstants.FluidInteractionBoxDeflate);

        int x0 = (int)Math.Floor(box.MinX);
        int y0 = (int)Math.Floor(box.MinY);
        int z0 = (int)Math.Floor(box.MinZ);
        int x1 = (int)Math.Ceiling(box.MaxX) - 1;
        int y1 = (int)Math.Ceiling(box.MaxY) - 1;
        int z1 = (int)Math.Ceiling(box.MaxZ) - 1;

        // Vanilla bails when any involved chunk is not loaded (hasFluidAndLoaded), leaving heights 0.
        if (!_world.IsChunkLoaded(new BlockPos(x0, y0, z0)) || !_world.IsChunkLoaded(new BlockPos(x1, y0, z1)))
            return;

        double entityY = _position.Y; // bounding box min Y (heights are measured from the feet)
        int eyeBlockX = (int)Math.Floor(_position.X);
        int eyeBlockZ = (int)Math.Floor(_position.Z);
        double eyeY = _position.Y + EyeHeight;

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    BlockState state = _world.GetBlock(new BlockPos(x, y, z));
                    bool water = IsWater(state);
                    bool lava = !water && IsLava(state);
                    if (!water && !lava)
                        continue;

                    // height behavior: 1.0 with the same fluid above, else getOwnHeight (amount/9).
                    double fluidTop;
                    BlockState above = _world.GetBlock(new BlockPos(x, y + 1, z));
                    if (water ? IsWater(above) : IsLava(above))
                        fluidTop = y + 1.0;

                    else
                        fluidTop = y + FluidOwnHeight(state);

                    if (fluidTop < box.MinY)
                        continue;

                    double height = fluidTop - entityY;
                    if (water)
                    {
                        _waterHeight = Math.Max(_waterHeight, height);
                        if (x == eyeBlockX && z == eyeBlockZ && eyeY >= y && eyeY <= fluidTop)
                            _eyeInWater = true;

                        if (pushedByFluid)
                        {
                            _waterFlow = _waterFlow.Add(ScaledFlow(x, y, z, state, water: true, _waterHeight));
                            _waterFlowCells++;
                        }
                    }
                    else
                    {
                        _lavaHeight = Math.Max(_lavaHeight, height);
                        if (pushedByFluid)
                        {
                            _lavaFlow = _lavaFlow.Add(ScaledFlow(x, y, z, state, water: false, _lavaHeight));
                            _lavaFlowCells++;
                        }
                    }
                }
            }
        }
    }

    // Fluid own-height is amount / 9. The block "level" property maps to fluid amount as follows: level 0 is a source with amount 8; levels 1-7 are flowing (amount 8 - level), levels 8+ = falling (amount 8). Waterlogged states carry a full water source. States without a parsable level default to a source.
    private static double FluidOwnHeight(BlockState state)
    {
        int amount = 8;
        if (state.IsFluid && state.TryGetProperty("level", out string level)
            && int.TryParse(level, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
            amount = parsed switch
            {
                0 => 8,
                >= 1 and <= 7 => 8 - parsed,
                _ => 8,
            };

        return amount * PhysicsConstants.FluidHeightPerAmount;
    }

    private static bool IsWater(BlockState state)
    {
        if (state.IsDefault)
            return false;

        // Water sources, flowing water, waterlogged states, and bubble columns count as water for travel. The dataset marks bubble columns as waterlogged.
        return (state.IsFluid && !IsLava(state)) || state.IsWaterlogged;
    }

    private static bool IsLava(BlockState state)
    {
        if (state.IsDefault || !state.IsFluid)
            return false;

        return state.Block.Id.Path.Contains("lava", StringComparison.Ordinal);
    }

    // Fluid current

    /// <summary>The four horizontal directions in compatibility order.</summary>
    private static readonly (int X, int Z)[] HorizontalSteps = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>One cell's contribution to the accumulated current: the cell's flow vector, scaled by the running fluid depth while that depth is under 0.4.</summary>
    private Vec3d ScaledFlow(int x, int y, int z, BlockState state, bool water, double runningHeight)
    {
        Vec3d flow = GetFlow(_world, x, y, z, state, water);
        return runningHeight < PhysicsConstants.FluidPushDepthScaleThreshold ? flow.Scale(runningHeight) : flow;
    }

    /// <summary>The water depth a body standing with its feet plane at <paramref name="feetPlaneY"/> in column <c>(x, z)</c> would sense for that stance, and zero where the stance touches no water at all.</summary>
    /// <remarks>
    /// <para>Exposed for the same reason <see cref="GetWaterFlow"/> is: the PLANNER has to know whether a takeoff leaves the body a ground jump. A sensed height no deeper than <see cref="PhysicsConstants.FluidJumpThreshold"/> permits a 0.42 ground jump; deeper fluid permits only the 0.04 liquid impulse. A planner that re-derived the depth itself would be a second model of the same thing, which is the exact split that let the cost table and the executor disagree about every other medium.</para>
    /// <para>It is <see cref="SenseFluids"/>'s own scan restricted to ONE column, and that restriction is exact for the stance the planner asks about: the fluid box is deflated by <see cref="PhysicsConstants.FluidInteractionBoxDeflate"/> and a player is 0.6 wide, so a body centred in a cell overlaps no other column. A body pushed off centre can straddle two and would sense the deeper of them; the planner has no off-centre stance to ask about, and erring shallow here can only ever OFFER a jump, never refuse one, which is the direction a takeoff gate must not err in - so callers pass the stance they mean.</para>
    /// <para>A fluid column has height 1.0 when the cell above holds the same fluid. Otherwise its height is <c>amount / 9</c>, with the block <c>level</c> property mapped to that amount. A cell whose surface is below the feet plane contributes nothing.</para>
    /// </remarks>
    /// <param name="world">The world to read; a frozen planning capture is fine.</param>
    /// <param name="x">The column's X.</param>
    /// <param name="feetPlaneY">The body's bounding-box minimum Y, i.e. where its feet rest.</param>
    /// <param name="z">The column's Z.</param>
    /// <returns>The sensed water height in blocks, measured up from the feet plane.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static double WaterHeightForStance(IPhysicsWorldView world, int x, double feetPlaneY, int z)
    {
        ArgumentNullException.ThrowIfNull(world);

        double minY = feetPlaneY + PhysicsConstants.FluidInteractionBoxDeflate;
        double maxY = feetPlaneY + PhysicsConstants.PlayerStandingHeight - PhysicsConstants.FluidInteractionBoxDeflate;
        int y0 = (int)Math.Floor(minY);
        int y1 = (int)Math.Ceiling(maxY) - 1;

        double height = 0.0;
        for (int y = y0; y <= y1; y++)
        {
            BlockState state = world.GetBlock(new BlockPos(x, y, z));
            if (!IsWater(state))
                continue;

            BlockState above = world.GetBlock(new BlockPos(x, y + 1, z));
            double fluidTop = IsWater(above) ? y + 1.0 : y + FluidOwnHeight(state);
            if (fluidTop < minY)
                continue;

            height = Math.Max(height, fluidTop - feetPlaneY);
        }

        return height;
    }

    /// <summary>The direction water in one cell flows: a unit vector, or exactly zero for still water and for a cell that holds no water at all.</summary>
    /// <remarks>
    /// <para>The same flow calculation that pushes a body, exposed because the PLANNER has to charge a swim against the current it will actually meet, and a planner that re-derives the gradient itself would be a second model of the same thing - the exact split that let the executor and the cost table disagree about every other medium. <c>PlanningWorldView</c> is an <see cref="IPhysicsWorldView"/>, so the planner calls this against its own frozen capture.</para>
    /// <para>The push a body actually receives is this vector scaled by <see cref="PhysicsConstants.WaterPushScale"/> and AVERAGED over every water cell the body overlaps (players average, other entities normalize), so a single cell's flow is the direction and the upper bound of the magnitude, not the impulse.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static Vec3d GetWaterFlow(IPhysicsWorldView world, BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(world);
        BlockState state = world.GetBlock(pos);
        return IsWater(state) ? GetFlow(world, pos.X, pos.Y, pos.Z, state, water: true) : Vec3d.Zero;
    }

    /// <summary>Computes the horizontal gradient of this fluid's own height against its four horizontal neighbours, plus a strong downward term for a FALLING fluid running against a solid face, normalized.</summary>
    /// <remarks>UMPK has no separate full-face support-shape model. This is approximated by "a collision box that covers the whole 1x1 cross-section of that face", which is exactly right for full cubes (true) and for slabs, stairs, fences, panes, carpets and ladders (false), and is only consulted for a falling fluid.</remarks>
    private static Vec3d GetFlow(IPhysicsWorldView world, int x, int y, int z, BlockState state, bool water)
    {
        double ownHeight = FluidOwnHeight(state);
        double dx = 0.0;
        double dz = 0.0;

        foreach ((int stepX, int stepZ) in HorizontalSteps)
        {
            var neighbourPos = new BlockPos(x + stepX, y, z + stepZ);
            BlockState neighbour = world.GetBlock(neighbourPos);
            if (!AffectsFlow(neighbour, water))
                continue;

            double neighbourHeight = IsSameFluid(neighbour, water) ? FluidOwnHeight(neighbour) : 0.0;
            double delta = 0.0;
            if (neighbourHeight == 0.0)
            {
                if (!neighbour.BlocksMotion)
                {
                    BlockState below = world.GetBlock(new BlockPos(neighbourPos.X, neighbourPos.Y - 1, neighbourPos.Z));
                    if (AffectsFlow(below, water) && IsSameFluid(below, water))
                    {
                        double belowHeight = FluidOwnHeight(below);
                        if (belowHeight > 0.0)
                            delta = ownHeight - (belowHeight - PhysicsConstants.FluidBelowNeighbourOffset);

                    }
                }
            }
            else
                delta = ownHeight - neighbourHeight;

            if (delta != 0.0)
            {
                dx += stepX * delta;
                dz += stepZ * delta;
            }
        }

        var flow = new Vec3d(dx, 0.0, dz);
        if (IsFallingFluid(state))
            foreach ((int stepX, int stepZ) in HorizontalSteps)
            {
                var side = new BlockPos(x + stepX, y, z + stepZ);
                if (IsSolidFace(world, side, stepX, stepZ, water)
                    || IsSolidFace(world, new BlockPos(side.X, side.Y + 1, side.Z), stepX, stepZ, water))
                {
                    flow = flow.Normalize().Add(0.0, PhysicsConstants.FluidFallingFlowDownward, 0.0);
                    break;
                }
            }

        return flow.Normalize();
    }

    /// <summary>A neighbour affects flow when it holds no fluid at all or the SAME fluid. A water cell beside lava is not part of the water's gradient.</summary>
    private static bool AffectsFlow(BlockState state, bool water)
        => (!IsWater(state) && !IsLava(state)) || IsSameFluid(state, water);

    private static bool IsSameFluid(BlockState state, bool water) => water ? IsWater(state) : IsLava(state);

    /// <summary>The falling fluid-state flag. Block level 0 is a source, levels 1-7 map to flowing amounts 7-1, and levels 8 or more are the falling state. A waterlogged block carries a plain source, so it never falls.</summary>
    private static bool IsFallingFluid(BlockState state)
    {
        if (!state.IsFluid || !state.TryGetProperty("level", out string level))
            return false;

        return int.TryParse(level, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            && parsed >= PhysicsConstants.FluidFallingLevel;
    }

    private static bool IsSolidFace(IPhysicsWorldView world, BlockPos pos, int stepX, int stepZ, bool water)
    {
        BlockState state = world.GetBlock(pos);
        if (IsSameFluid(state, water))
            return false;

        // Ice is explicitly excluded as a solid flow face, so a waterfall does not get pinned against an ice wall.
        if (IsIce(state))
            return false;

        return HasFullFace(world, state, stepX, stepZ);
    }

    private static bool IsIce(BlockState state)
    {
        if (state.IsDefault)
            return false;

        string path = state.Block.Id.Path;
        return path.Equals("ice", StringComparison.Ordinal) || path.EndsWith("_ice", StringComparison.Ordinal);
    }

    private static bool HasFullFace(IPhysicsWorldView world, BlockState state, int stepX, int stepZ)
    {
        if (!state.BlocksMotion)
            return false;

        foreach (Aabb box in world.GetCollisionShapes(state))
        {
            bool spansY = box.MinY <= 0.0 && box.MaxY >= 1.0;
            if (!spansY)
                continue;

            if (stepX != 0)
            {
                bool touches = stepX > 0 ? box.MaxX >= 1.0 : box.MinX <= 0.0;
                if (touches && box.MinZ <= 0.0 && box.MaxZ >= 1.0)
                    return true;

            }
            else
            {
                bool touches = stepZ > 0 ? box.MaxZ >= 1.0 : box.MinZ <= 0.0;
                if (touches && box.MinX <= 0.0 && box.MaxX >= 1.0)
                    return true;

            }
        }

        return false;
    }

    // Pose

    private void UpdatePlayerPose()
    {
        // Only change pose when the swim box fits.
        if (!CanFitWithPose(EntityPose.Swimming))
            return;

        EntityPose desired = GetDesiredPose();
        EntityPose actual;
        if (_conditions.GameMode == GameMode.Spectator || CanFitWithPose(desired))
            actual = desired;

        else if (CanFitWithPose(EntityPose.Crouching))
            actual = EntityPose.Crouching;

        else
            actual = EntityPose.Swimming;

        _pose = actual;
    }

    private EntityPose GetDesiredPose()
    {
        if (_conditions.ElytraFlying && _profile.ElytraAvailable)
            return EntityPose.FallFlying;

        if (IsSwimmingState() && _profile.SwimPoseAvailable)
            return EntityPose.Swimming;

        if (_sneaking && !_conditions.CreativeFlying)
            return EntityPose.Crouching;

        return EntityPose.Standing;
    }

    // A player swims while sprinting underwater and not flying.
    private bool IsSwimmingState() => !_conditions.CreativeFlying && _sprinting && _isUnderWater;

    private bool CanFitWithPose(EntityPose pose)
    {
        double height = HeightForPose(pose);
        Aabb box = Aabb.OfSize(_position.X, _position.Y, _position.Z, _profile.Width, height);
        Aabb deflated = box.Deflate(PhysicsConstants.CollisionEpsilon, PhysicsConstants.CollisionEpsilon, PhysicsConstants.CollisionEpsilon);
        return CollisionResolver.NoCollision(_world, deflated, _position.Y, Body, _scratch);
    }

    /// <summary>The entity half of vanilla's <c>collision-context construction</c>, rebuilt per query because every field of it changes within a tick: the sneak flag is written by the travel input, the fall distance by the previous move's resolution.</summary>
    /// <remarks><c>PowderSnowWalkable</c> comes off the pushed conditions rather than being derived here, because "leather boots in the feet slot" is an inventory read and the engine reads no game state of its own. See <see cref="PhysicsConditions.PowderSnowWalkable"/>.</remarks>
    private BodyCollisionContext Body =>
        new(_sneaking, _fallDistance, _conditions.PowderSnowWalkable);

    private double HeightForPose(EntityPose pose) => pose switch
    {
        EntityPose.Crouching => _profile.CrouchHeight,
        EntityPose.Swimming or EntityPose.FallFlying or EntityPose.SpinAttack => _profile.SwimHeight,
        _ => _profile.StandingHeight,
    };

    // Vanilla Avatar.POSES eye heights per pose (standing 1.62, crouching 1.27, swim/glide 0.4).
    private double EyeHeightForPose(EntityPose pose) => pose switch
    {
        EntityPose.Crouching => PhysicsConstants.PlayerCrouchEyeHeight,
        EntityPose.Swimming or EntityPose.FallFlying or EntityPose.SpinAttack => PhysicsConstants.PlayerSwimEyeHeight,
        _ => PhysicsConstants.PlayerStandingEyeHeight,
    };

    private double Height => HeightForPose(_pose);

    private double EyeHeight => EyeHeightForPose(_pose);

    private Aabb BoundingBox => Aabb.OfSize(_position.X, _position.Y, _position.Z, _profile.Width, Height);

    // Velocity zeroing

    private void ZeroTinyVelocity()
    {
        double dx = _velocity.X;
        double dy = _velocity.Y;
        double dz = _velocity.Z;

        if (dx * dx + dz * dz < PhysicsConstants.PlayerHorizontalVelocityThresholdSqr)
        {
            dx = 0;
            dz = 0;
        }

        if (Math.Abs(dy) < PhysicsConstants.AxisVelocityThreshold)
            dy = 0;

        _velocity = new Vec3d(dx, dy, dz);
    }

    // Input mapping

    private void MapInput(in MovementInput input)
    {
        (float rawXxa, float rawZza) = input.GetMoveVector();

        rawXxa *= PhysicsConstants.InputFriction;
        rawZza *= PhysicsConstants.InputFriction;

        if (input.Sneak)
        {
            // Vanilla's tick behavior scales both impulses by the crouch factor, which is 0.3 for a player with nothing equipped and clamp(0.3 + 0.15*L, 0, 1) with swift sneak L. It is a REPLACEMENT of the 0.3, never a second multiplier. See PhysicsConditions.SneakingSpeedFactor.
            rawXxa *= _conditions.SneakingSpeedFactor;
            rawZza *= _conditions.SneakingSpeedFactor;
        }

        _xxa = rawXxa;
        _zza = rawZza;
        _yya = 0;
        _sneaking = input.Sneak;
        _sprinting = input.Sprint;

        bool wantJump = input.Jump;
        if (input.AutoJump && !wantJump && _onGround && _horizontalCollision && (rawXxa != 0 || rawZza != 0))
            wantJump = true;

        _jumping = wantJump;

        if (_conditions.CreativeFlying)
        {
            if (input.Jump)
                _yya += _conditions.FlyingSpeed * 3.0f;

            if (input.Sneak)
                _yya -= _conditions.FlyingSpeed * 3.0f;

        }
    }

    // Jump

    // The jump gate treats a player on the ground with player on the ground and the fluid no deeper than the jump threshold, a jump is a FULL ground jump (jumpFromGround: 0.42 power, sprint boost, 10-tick delay); only a genuinely deep fluid gets the 0.04 liquid impulse. A creative-flying player skips fluid jump handling entirely.
    private void HandleJumping()
    {
        if (!_jumping || _conditions.CreativeFlying)
        {
            _noJumpDelay = 0;
            return;
        }

        double fluidHeight = _inLava ? _lavaHeight : _waterHeight;
        bool inWaterAndHasFluidHeight = _inWater && fluidHeight > 0.0;
        double fluidJumpThreshold = GetFluidJumpThreshold();

        if (inWaterAndHasFluidHeight && (!_onGround || fluidHeight > fluidJumpThreshold))
            JumpInLiquid();

        else if (_inLava && (!_onGround || _lavaHeight > fluidJumpThreshold))
            JumpInLiquid();

        else if ((_onGround || (inWaterAndHasFluidHeight && fluidHeight <= fluidJumpThreshold)) && _noJumpDelay == 0)
        {
            JumpFromGround();
            _noJumpDelay = PhysicsConstants.JumpDelayTicks;
        }
    }

    // A liquid jump is a flat upward impulse with no delay.
    private void JumpInLiquid() => _velocity = _velocity.Add(0, PhysicsConstants.FluidJumpImpulse, 0);

    // The fluid jump threshold is 0.4 unless the current eye height is under 0.4, then 0.
    private double GetFluidJumpThreshold() =>
        EyeHeight < PhysicsConstants.FluidJumpThresholdMinEyeHeight ? 0.0 : PhysicsConstants.FluidJumpThreshold;

    private void JumpFromGround()
    {
        float jumpPower = GetJumpPower();
        if (jumpPower <= 1.0E-5f)
            return;

        _velocity = new Vec3d(_velocity.X, Math.Max(jumpPower, _velocity.Y), _velocity.Z);

        if (_sprinting)
        {
            float yawRad = _yaw * (MathF.PI / 180.0f);
            _velocity = _velocity.Add(
                -MathF.Sin(yawRad) * PhysicsConstants.SprintJumpHorizontalBoost,
                0,
                MathF.Cos(yawRad) * PhysicsConstants.SprintJumpHorizontalBoost);
        }
    }

    // Jump power is jump strength times block jump factor, plus jump-boost power.
    private float GetJumpPower()
    {
        float blockJumpFactor = GetBlockJumpFactor();
        float power = PhysicsConstants.BaseJumpPower * blockJumpFactor;
        if (_conditions.HasJumpBoost)
            power += PhysicsConstants.JumpBoostPerLevel * (_conditions.JumpBoostAmplifier + 1);

        return power;
    }

    private float GetBlockJumpFactor()
    {
        BlockState here = BlockAtFeet();
        float jumpHere = here.IsDefault ? 1.0f : here.JumpFactor;
        if (jumpHere != 1.0f)
            return jumpHere;

        BlockState below = BlockBelowFeet();
        return below.IsDefault ? 1.0f : below.JumpFactor;
    }

    // Travel dispatch

    // Travel dispatches fluid first, then fall-flying, then air. shouldTravelInFluid = (isInWater || isInLava) && isAffectedByFluids (= !abilities.flying for players). A gliding player who enters water therefore switches to fluid travel immediately, before the host clears the glide flag. The elytra branch keeps its own climbable fallback to air travel.
    private void Travel(Vec3d input)
    {
        if ((_inWater || _inLava) && !_conditions.CreativeFlying)
        {
            if (_inWater)
            {
                ApplySwimSteer();
                TravelInWater(input);
            }
            else
                TravelInLava(input);

            return;
        }

        if (_conditions.ElytraFlying && _profile.ElytraAvailable && !_conditions.CreativeFlying && !_onClimbable)
        {
            TravelFallFlying();
            return;
        }

        TravelInAir(input);
    }

    // Air and ground travel

    private void TravelInAir(Vec3d input)
    {
        // Creative flight records the Y velocity before, runs the parent travel (which moves the position and applies gravity), then replaces the resulting Y velocity with originalY * 0.6.
        double originalY = _velocity.Y;

        float blockFriction = _onGround ? GetBlockFriction() : 1.0f;

        float speed = GetFrictionInfluencedSpeed(blockFriction);
        MoveRelative(speed, input);

        HandleOnClimbable();

        Move(_velocity);

        Vec3d postMove = _velocity;
        double vy = postMove.Y;

        if ((_horizontalCollision || _jumping) && _onClimbable)
            vy = PhysicsConstants.ClimbWallBump;

        if (_conditions.HasLevitation)
            vy += (0.05 * (_conditions.LevitationAmplifier + 1) - vy) * 0.2;

        else if (!_world.IsChunkLoaded(BlockPos.Containing(_position.X, _position.Y - PhysicsConstants.BelowFeetOffset, _position.Z)))
        {
            // Vanilla client-side: drift down slowly through unloaded chunks instead of full gravity.
            vy = -0.1;
        }
        else
            vy -= GetEffectiveGravity();

        float friction = blockFriction * PhysicsConstants.FrictionMultiplier;
        _velocity = new Vec3d(postMove.X * friction, vy * PhysicsConstants.DragY, postMove.Z * friction);

        if (_conditions.CreativeFlying)
        {
            // Creative flight replaces Y velocity with the damped pre-travel Y.
            _velocity = new Vec3d(_velocity.X, originalY * PhysicsConstants.FlyVerticalDamping, _velocity.Z);
        }

        ApplyBlockSpeedFactor();
        ApplySlimeStepOn();
    }

    /// <summary>The movement-speed attribute for this tick: the host's sprint-free value with the sprint modifier folded back in when the tick's input holds Sprint.</summary>
    /// <remarks>
    /// <para>The game's movement-speed attribute is sprint-inclusive. UMPK has no self-side attribute map for the engine to hang a transient modifier on, so the resolution is performed here instead, from the input bit, which is also what keeps <c>PhysicsSimulator</c>'s lookahead and the live engine the same predictor, since both see the same input and the same frozen conditions.</para>
    /// <para>The fold is deliberately <c>(float)(double_value * (1.0 + (double)0.3f))</c> and not <c>value * 1.3f</c>: attribute calculation works in double over an amount that was widened from the <c>0.3F</c> literal, so the product is rounded to float exactly once, at the end. A float fold lands one ulp low for most attribute values.</para>
    /// </remarks>
    private float ResolvedMovementSpeed() => _sprinting
        ? (float)(_conditions.BaseMovementSpeedAttribute * (1.0 + PhysicsConstants.SprintSpeedModifier))
        : _conditions.BaseMovementSpeedAttribute;

    private float GetFrictionInfluencedSpeed(float blockFriction)
    {
        // The modifier is folded in BEFORE the friction branch because both arms must carry it: vanilla's getFrictionInfluencedSpeed returns getSpeed() unscaled when friction <= 0.6, and 0.6 is ordinary ground. Folding it inside the scaled arm alone would leave every stone floor walking.
        float effectiveSpeed = ResolvedMovementSpeed();
        if (_onGround)
        {
            // Vanilla getFrictionInfluencedSpeed: only scale when friction > 0.6, else return speed.
            if (blockFriction > PhysicsConstants.FrictionSpeedThreshold)
                return effectiveSpeed * (PhysicsConstants.GroundAccelerationFactor / (blockFriction * blockFriction * blockFriction));

            return effectiveSpeed;
        }

        // Off-ground: flat air speed (getFlyingSpeed), not attribute-scaled. Creative-fly scales speed.
        if (_conditions.CreativeFlying)
        {
            float flySpeed = _sprinting ? _conditions.FlyingSpeed * 2.0f : _conditions.FlyingSpeed;
            return flySpeed;
        }

        return _sprinting ? PhysicsConstants.SprintingAirSpeed : PhysicsConstants.AirSpeed;
    }

    // Water travel

    private void TravelInWater(Vec3d input)
    {
        bool isFalling = _velocity.Y <= 0.0;
        double oldY = _position.Y;
        double baseGravity = GetEffectiveGravity();

        // The water-travel era adds the sprint-aware slowdown from protocol 393 onward.
        bool sprintAwareWater = _profile.WaterTravel == WaterTravelEra.SprintAware;
        float slowDown = sprintAwareWater && _sprinting
            ? PhysicsConstants.WaterSprintSlowDown
            : PhysicsConstants.WaterSlowDown;
        float speed = PhysicsConstants.WaterBaseSpeed;

        // Water movement efficiency blend (swimming-update era only).
        if (_profile.FluidMovement == FluidMovementEra.SwimmingUpdate)
        {
            float waterWalker = _conditions.WaterMovementEfficiency;
            if (!_onGround)
                waterWalker *= 0.5f;

            if (waterWalker > 0.0f)
            {
                // The blend target is sprint-inclusive. Blending the sprint-free base here leaves a depth-strider sprint-swim 30% slow. The 0.9/0.8 slow-down split above is a separate sprint consumer and stays as it is.
                slowDown += (PhysicsConstants.WaterWalkerSlowDownTarget - slowDown) * waterWalker;
                speed += (ResolvedMovementSpeed() - speed) * waterWalker;
            }
        }

        if (_conditions.HasDolphinsGrace)
            slowDown = PhysicsConstants.DolphinsGraceSlowDown;

        MoveRelative(speed, input);
        Move(_velocity);

        Vec3d vel = _velocity;

        // The in-water climbable bump is available from protocol 477 (1.14).
        if (_profile.WaterClimbBumpAvailable && _horizontalCollision && _onClimbable)
            vel = new Vec3d(vel.X, PhysicsConstants.ClimbWallBump, vel.Z);

        vel = vel.Multiply(slowDown, PhysicsConstants.WaterYDamping, slowDown);

        // Before 1.13 the vertical tail is a flat subtraction. Later protocols use the gravity-derived fluid-falling adjustment. The zero-gravity guard is currently inert.
        if (sprintAwareWater)
            _velocity = GetFluidFallingAdjustedMovement(baseGravity, isFalling, vel);

        else
            _velocity = baseGravity == 0.0
                ? vel
                : new Vec3d(vel.X, vel.Y - PhysicsConstants.LegacyWaterSink, vel.Z);

        JumpOutOfFluid(oldY);
    }

    /// <summary>
    /// A player pressed against a bank whose box, offset by the delta movement and raised by <c>0.6F</c> of the tick's net vertical travel, is free of blocks and liquid gets a <c>0.3F</c> vertical velocity: this is what lets a swimmer climb out onto a shore.
    ///
    /// <para>This clause is present in every supported era and in both fluid arms, so it is not era-gated.</para>
    /// </summary>
    /// <param name="oldY">The entity Y captured before relative and collision-aware movement.</param>
    private void JumpOutOfFluid(double oldY)
    {
        Vec3d movement = _velocity;
        if (_horizontalCollision
            && IsFree(movement.X, movement.Y + PhysicsConstants.FluidLedgeProbeUp - _position.Y + oldY, movement.Z))
            _velocity = new Vec3d(movement.X, PhysicsConstants.FluidLedgeHop, movement.Z);

    }

    /// <summary>The bounding box offset by the given delta must have no collision and contain no liquid. It reuses the same collider collection the engine's own <c>move</c> path uses, so a shape the collision resolver respects is a shape this probe respects.</summary>
    private bool IsFree(double dx, double dy, double dz)
    {
        Aabb box = BoundingBox.Move(dx, dy, dz);
        return CollisionResolver.NoCollision(_world, box, _position.Y, Body, _scratch) && !ContainsAnyLiquid(box);
    }

    /// <summary>Returns true when any block cell overlapped by the box carries a non-empty fluid state.</summary>
    /// <remarks>
    /// <para>Protocol 47 scans an additional cell layer when a maximum bound is integral; this model uses the exclusive-maximum rule from later protocols for every era. A flush horizontal collision commonly produces such an integral maximum, so protocol 47 can reject a ledge hop that this model permits when the extra wall column contains liquid.</para>
    /// <para>The answer includes water-containing blocks as well as fluid blocks. The method is internal so tests can verify the ledge-hop query directly.</para>
    /// </remarks>
    internal bool ContainsAnyLiquid(in Aabb box)
    {
        int x0 = (int)Math.Floor(box.MinX);
        int x1 = (int)Math.Ceiling(box.MaxX);
        int y0 = (int)Math.Floor(box.MinY);
        int y1 = (int)Math.Ceiling(box.MaxY);
        int z0 = (int)Math.Floor(box.MinZ);
        int z1 = (int)Math.Ceiling(box.MaxZ);

        for (int x = x0; x < x1; x++)
            for (int y = y0; y < y1; y++)
                for (int z = z0; z < z1; z++)
                {
                    BlockState state = _world.GetBlock(new BlockPos(x, y, z));
                    if (IsWater(state) || IsLava(state))
                        return true;

                }

        return false;
    }

    // Fluid falling adjustment
    private Vec3d GetFluidFallingAdjustedMovement(double baseGravity, bool isFalling, Vec3d movement)
    {
        if (baseGravity == 0.0 || _sprinting)
            return movement;

        double yd;
        if (isFalling
            && Math.Abs(movement.Y - 0.005) >= PhysicsConstants.AxisVelocityThreshold
            && Math.Abs(movement.Y - baseGravity / 16.0) < PhysicsConstants.AxisVelocityThreshold)
            yd = -0.003;

        else
            yd = movement.Y - baseGravity / 16.0;

        return new Vec3d(movement.X, yd, movement.Z);
    }

    // Swim ascend and descend steering
    private void ApplySwimSteer()
    {
        if (!IsSwimmingState() || !_profile.SwimPoseAvailable)
            return;

        double lookY = LookAngleY();
        double multiplier = lookY < PhysicsConstants.SwimSteerSteepThreshold
            ? PhysicsConstants.SwimSteerMultiplierSteep
            : PhysicsConstants.SwimSteerMultiplier;

        double aboveHeadY = _position.Y + 1.0 - 0.1;
        BlockState aboveHead = _world.GetBlock(BlockPos.Containing(_position.X, aboveHeadY, _position.Z));
        bool fluidAboveHead = IsWater(aboveHead) || IsLava(aboveHead);

        if (lookY <= 0.0 || _jumping || fluidAboveHead)
            _velocity = _velocity.Add(0, (lookY - _velocity.Y) * multiplier, 0);

    }

    private double LookAngleY()
    {
        // Vanilla getLookAngle().y == -sin(pitch).
        float pitchRad = _pitch * (MathF.PI / 180.0f);
        return -MathF.Sin(pitchRad);
    }

    // Lava travel

    // Shallow lava (isInShallowFluid: height <= jump threshold) damps (0.5, 0.8, 0.5) with the fluid-falling adjustment; deep lava scales uniformly by 0.5. Both then add -gravity/4.
    private void TravelInLava(Vec3d input)
    {
        bool isFalling = _velocity.Y <= 0.0;
        double oldY = _position.Y;
        double baseGravity = GetEffectiveGravity();

        MoveRelative(PhysicsConstants.LavaSpeed, input);
        Move(_velocity);

        if (_lavaHeight <= GetFluidJumpThreshold())
        {
            _velocity = _velocity.Multiply(PhysicsConstants.LavaShallowDamping, PhysicsConstants.LavaShallowVerticalDamping, PhysicsConstants.LavaShallowDamping);
            _velocity = GetFluidFallingAdjustedMovement(baseGravity, isFalling, _velocity);
        }
        else
            _velocity = _velocity.Scale(PhysicsConstants.LavaDeepScale);

        if (baseGravity != 0.0)
            _velocity = _velocity.Add(0, -baseGravity / 4.0, 0);

        JumpOutOfFluid(oldY);
    }

    // Elytra and fall-flying travel

    // Fall-flying movement
    private void TravelFallFlying()
    {
        Vec3d movement = _velocity;
        double moveHorLength = Math.Sqrt(movement.HorizontalDistanceSqr());

        Vec3d look = LookAngle();
        float leanAngle = _pitch * (MathF.PI / 180.0f);
        double lookHorLength = Math.Sqrt(look.X * look.X + look.Z * look.Z);
        double gravity = GetEffectiveGravity();
        double liftForce = Math.Cos(leanAngle);
        liftForce *= liftForce;

        movement = movement.Add(0, gravity * (-1.0 + liftForce * 0.75), 0);

        if (movement.Y < 0.0 && lookHorLength > 0.0)
        {
            double convert = movement.Y * -0.1 * liftForce;
            movement = movement.Add(look.X * convert / lookHorLength, convert, look.Z * convert / lookHorLength);
        }

        if (leanAngle < 0.0f && lookHorLength > 0.0)
        {
            double convert = moveHorLength * -MathF.Sin(leanAngle) * 0.04;
            movement = movement.Add(-look.X * convert / lookHorLength, convert * 3.2, -look.Z * convert / lookHorLength);
        }

        if (lookHorLength > 0.0)
            movement = movement.Add(
                (look.X / lookHorLength * moveHorLength - movement.X) * 0.1,
                0.0,
                (look.Z / lookHorLength * moveHorLength - movement.Z) * 0.1);

        movement = movement.Multiply(PhysicsConstants.ElytraHorizontalDrag, PhysicsConstants.ElytraVerticalDrag, PhysicsConstants.ElytraHorizontalDrag);

        _velocity = movement;
        Move(_velocity);
    }

    private Vec3d LookAngle()
    {
        // Look direction from yaw and pitch in degrees.
        float yawRad = _yaw * (MathF.PI / 180.0f);
        float pitchRad = _pitch * (MathF.PI / 180.0f);
        float cosPitch = MathF.Cos(pitchRad);
        double x = -MathF.Sin(yawRad) * cosPitch;
        double y = -MathF.Sin(pitchRad);
        double z = MathF.Cos(yawRad) * cosPitch;
        return new Vec3d(x, y, z);
    }

    // Shared movement helpers

    private void MoveRelative(float speed, Vec3d input)
    {
        Vec3d rotated = GetInputVector(input, speed, _yaw);
        _velocity = _velocity.Add(rotated);
    }

    private static Vec3d GetInputVector(Vec3d input, float speed, float yaw)
    {
        double lenSqr = input.LengthSqr();
        if (lenSqr < 1.0E-7)
            return Vec3d.Zero;

        Vec3d scaled = (lenSqr > 1.0 ? input.Normalize() : input).Scale(speed);
        float sinYaw = MathF.Sin(yaw * (MathF.PI / 180.0f));
        float cosYaw = MathF.Cos(yaw * (MathF.PI / 180.0f));

        return new Vec3d(
            scaled.X * cosYaw - scaled.Z * sinYaw,
            scaled.Y,
            scaled.Z * cosYaw + scaled.X * sinYaw);
    }

    private void Move(Vec3d movement) => Move(movement, byPiston: false);

    /// <summary>Moves the body with collision. Piston movement clears a pending stuck-speed multiplier and zeroes velocity without applying the multiplier, and it skips edge back-off. Per-tick piston displacement limiting is applied by the caller (<see cref="IPistonPushTarget.MoveByPiston"/>) because its accumulator is per game tick and the block entity, not the engine, knows when a tick started.</summary>
    private void Move(Vec3d movement, bool byPiston)
    {
        if (_stuckSpeedMultiplier.LengthSqr() > 1.0E-7)
        {
            if (!byPiston)
                movement = movement.Multiply(_stuckSpeedMultiplier);

            _stuckSpeedMultiplier = Vec3d.Zero;
            _velocity = Vec3d.Zero;
        }

        if (!byPiston && _sneaking && _onGround)
            movement = MaybeBackOffFromEdge(movement);

        Aabb box = BoundingBox;
        Vec3d resolved = CollisionResolver.Collide(
            _world, box, movement, _onGround, Body, PhysicsConstants.StepHeight, _colliders, _stepColliders);

        double resolvedLenSqr = resolved.LengthSqr();
        if (resolvedLenSqr > 1.0E-7 || movement.LengthSqr() - resolvedLenSqr < 1.0E-7)
            _position = _position.Add(resolved);

        bool blockedX = !MthEqual(movement.X, resolved.X);
        bool blockedZ = !MthEqual(movement.Z, resolved.Z);
        _horizontalCollision = blockedX || blockedZ;
        _verticalCollision = movement.Y != resolved.Y;
        _verticalCollisionBelow = _verticalCollision && movement.Y < 0.0;
        _onGround = _verticalCollisionBelow;

        if (_onGround)
        {
            if (_fallDistance > 0)
                _lastLandingFallDistance = _fallDistance;

            _fallDistance = 0;
        }
        else if (resolved.Y < 0)
            _fallDistance -= resolved.Y;

        if (_horizontalCollision)
            _velocity = new Vec3d(
                blockedX ? 0 : _velocity.X,
                _velocity.Y,
                blockedZ ? 0 : _velocity.Z);

        if (_verticalCollision)
            UpdateMovementAfterFallOn();

    }

    private void UpdateMovementAfterFallOn()
    {
        BlockState below = _world.GetBlock(BlockPos.Containing(_position.X, _position.Y - 0.2, _position.Z));
        if (IsSlime(below) && !_sneaking)
        {
            double vy = _velocity.Y;
            if (vy < 0.0)
            {
                _velocity = new Vec3d(_velocity.X, -vy, _velocity.Z);
                _bouncedThisTick = true;
            }
            else
                _velocity = new Vec3d(_velocity.X, 0, _velocity.Z);

        }
        else
            _velocity = new Vec3d(_velocity.X, 0, _velocity.Z);

    }

    // Edge back-off uses a thin slice of height maxUpStep just below the feet. A full block-height probe would never clear the floor block.
    private Vec3d MaybeBackOffFromEdge(Vec3d movement)
    {
        if (movement.Y > 0)
            return movement;

        double step = PhysicsConstants.EdgeBackoffStep;
        double maxDownStep = PhysicsConstants.StepHeight;
        double dx = movement.X;
        double dz = movement.Z;
        double stepX = Math.Sign(dx) * step;
        double stepZ = Math.Sign(dz) * step;

        while (dx != 0.0 && CanFallAtLeast(dx, 0.0, maxDownStep))
        {
            if (Math.Abs(dx) <= step)
            {
                dx = 0.0;
                break;
            }

            dx -= stepX;
        }

        while (dz != 0.0 && CanFallAtLeast(0.0, dz, maxDownStep))
        {
            if (Math.Abs(dz) <= step)
            {
                dz = 0.0;
                break;
            }

            dz -= stepZ;
        }

        while (dx != 0.0 && dz != 0.0 && CanFallAtLeast(dx, dz, maxDownStep))
        {
            if (Math.Abs(dx) <= step)
                dx = 0.0;

            else
                dx -= stepX;

            if (Math.Abs(dz) <= step)
                dz = 0.0;

            else
                dz -= stepZ;

        }

        return new Vec3d(dx, movement.Y, dz);
    }

    // Tests a thin collision-free slice below the feet, offset by (dx,dz).
    private bool CanFallAtLeast(double dx, double dz, double minHeight)
    {
        const double e = PhysicsConstants.CollisionEpsilon;
        Aabb box = BoundingBox;
        var slice = new Aabb(
            box.MinX + e + dx,
            box.MinY - minHeight - e,
            box.MinZ + e + dz,
            box.MaxX - e + dx,
            box.MinY,
            box.MaxZ - e + dz);
        return CollisionResolver.NoCollision(_world, slice, _position.Y, Body, _scratch);
    }

    // Blocks containing the body

    /// <summary>Runs contact effects for every block cell overlapped by the body's box.</summary>
    /// <remarks>
    /// <para>This runs after the tick's movement and before the next tick's, which is what matters: every effect here lands on state the NEXT tick reads, so the two orders are the same simulation.</para>
    /// <para><b>The cell range</b> uses the bounding box inset by 1e-7 on every face, so a body resting flush on a boundary does not claim the cell it is merely touching. Modern versions additionally sample the swept path between the tick's start and end positions, which differs only for a cell the body passes ENTIRELY through inside one tick. Nothing in this file's effect set can be tunnelled at player speeds - the fastest a sprinting body moves is about 0.28 of a block a tick - so the simpler range is the same answer and is stated as such rather than as an approximation.</para>
    /// <para><b>Allocation.</b> The scan is a triple loop over at most 2x3x2 cells with struct reads only, so the zero-allocation tick contract (<c>Umpk.Physics.Tests.AllocationTests</c>) still holds; the identifier compare is the same shape <see cref="IsIce"/> and <c>IsLava</c> already do on this path.</para>
    /// </remarks>
    private void ApplyEffectsFromBlocks()
    {
        const double e = PhysicsConstants.CollisionEpsilon;
        Aabb box = BoundingBox;

        int minX = (int)Math.Floor(box.MinX + e);
        int minY = (int)Math.Floor(box.MinY + e);
        int minZ = (int)Math.Floor(box.MinZ + e);
        int maxX = (int)Math.Floor(box.MaxX - e);
        int maxY = (int)Math.Floor(box.MaxY - e);
        int maxZ = (int)Math.Floor(box.MaxZ - e);

        // Two corner probes implement the loaded-range gate at the granularity this seam offers.
        if (!_world.IsChunkLoaded(new BlockPos(minX, minY, minZ))
            || !_world.IsChunkLoaded(new BlockPos(maxX, maxY, maxZ)))
            return;

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var pos = new BlockPos(x, y, z);
                    EntityInside(_world.GetBlock(pos), pos);
                }

    }

    /// <summary>Applies one overlapping block's movement effect.</summary>
    private void EntityInside(BlockState state, BlockPos pos)
    {
        if (state.IsDefault || state.IsAir)
            return;

        if (IsBubbleColumn(state))
        {
            BubbleColumnInside(state, pos);
            return;
        }

        if (IsCobweb(state))
        {
            // Cobweb contact applies a (0.25, 0.05, 0.25) movement multiplier. The weaving variant (0.5, 0.25, 0.5) needs an effect this engine's conditions do not carry yet and is left out rather than guessed. The multipliers and fall-distance reset are stable across all supported eras, so no era axis is needed.
            MakeStuckInBlock(PhysicsConstants.WebStuckSpeedMultiplier);
        }
    }

    /// <summary>Zeroes fall distance, then arms the multiplier the next <see cref="Move(Vec3d, bool)"/> consumes.</summary>
    private void MakeStuckInBlock(Vec3d multiplier)
    {
        _fallDistance = 0.0;
        _stuckSpeedMultiplier = multiplier;
    }

    /// <summary><c>minecraft:cobweb</c>, and the pre-flattening registries' <c>minecraft:web</c> (block id 30, <c>legacy block registration</c>), which is the same block under the name those datasets carry.</summary>
    private static bool IsCobweb(BlockState state)
    {
        string path = state.Block.Id.Path;
        return path.Equals("cobweb", StringComparison.Ordinal) || path.Equals("web", StringComparison.Ordinal);
    }

    /// <summary><c>minecraft:bubble_column</c>, which exists from 1.13 (protocol 393) onward.</summary>
    private static bool IsBubbleColumn(BlockState state)
        => state.Block.Id.Path.Equals(BubbleColumnPath, StringComparison.Ordinal);

    /// <summary>Applies the <b>upward</b> bubble-column behavior.</summary>
    /// <remarks>
    /// <para>The bubble-column rule reads the cell above and dispatches: an open cell above means the body is at the column's mouth and takes the stronger mouth impulse; anything else takes the inside impulse:</para>
    /// <code>
    /// above,  drag=false: vy = min(1.8, vy + 0.1) inside, drag=false: vy = min(0.7, vy + 0.06), then resetFallDistance() above,  drag=true : vy = max(-0.9, vy - 0.03)     NOT MODELLED inside, drag=true : vy = max(-0.3, vy - 0.03)     NOT MODELLED
    /// </code>
    /// <para>These values are stable across supported eras. The block's <c>drag</c> property is false above soul sand and true above magma. Downward columns are not modeled and take no impulse.</para>
    /// <para><b>The mouth test</b> requires the cell above to have neither collision nor fluid. This is a superset of the older air-only test; the difference is a non-colliding non-fluid block capping a column, which is not a shape any course row builds. One rule is used on every era rather than an axis for a case with no witness.</para>
    /// </remarks>
    private void BubbleColumnInside(BlockState state, BlockPos pos)
    {
        if (!state.TryGetProperty(DragProperty, out string drag) || drag != FalseValue)
            return;

        BlockState above = _world.GetBlock(new BlockPos(pos.X, pos.Y + 1, pos.Z));
        bool atMouth = _world.GetCollisionShapes(above).Length == 0 && !IsWater(above) && !IsLava(above);

        if (atMouth)
        {
            _velocity = new Vec3d(
                _velocity.X, Math.Min(PhysicsConstants.BubbleColumnAboveMaxUp, _velocity.Y + PhysicsConstants.BubbleColumnAboveUp), _velocity.Z);
            return;
        }

        _velocity = new Vec3d(
            _velocity.X, Math.Min(PhysicsConstants.BubbleColumnInsideMaxUp, _velocity.Y + PhysicsConstants.BubbleColumnInsideUp), _velocity.Z);
        _fallDistance = 0.0;
    }

    private const string BubbleColumnPath = "bubble_column";

    private const string DragProperty = "drag";

    private const string FalseValue = "false";

    private void HandleOnClimbable()
    {
        if (!_onClimbable)
            return;

        _fallDistance = 0;
        double vx = Math.Clamp(_velocity.X, -PhysicsConstants.ClimbMaxSpeed, PhysicsConstants.ClimbMaxSpeed);
        double vz = Math.Clamp(_velocity.Z, -PhysicsConstants.ClimbMaxSpeed, PhysicsConstants.ClimbMaxSpeed);
        double vy = Math.Max(_velocity.Y, -PhysicsConstants.ClimbMaxSpeed);

        // The climb clamp cancels downward motion while sneaking on a climbable block, except on scaffolding. That exception is the whole of shift-descending a scaffold. Without it the shape half removes the plates under a sneaking body. Without this exception, the clamp would suspend the body where the plate had been. The rule is stable across all scaffolding eras.
        if (vy < 0.0 && _sneaking && !_inScaffolding)
            vy = 0.0;

        _velocity = new Vec3d(vx, vy, vz);
    }

    private double GetEffectiveGravity()
    {
        double gravity = PhysicsConstants.DefaultGravity;
        if (_conditions.HasSlowFalling && _velocity.Y <= 0.0)
            return Math.Min(gravity, PhysicsConstants.SlowFallingCap);

        return gravity;
    }

    private float GetBlockFriction()
    {
        BlockState below = BlockBelowFeet();
        return below.IsDefault ? 0.6f : below.Friction;
    }

    private void ApplyBlockSpeedFactor()
    {
        BlockState here = BlockAtFeet();
        float factor = here.IsDefault ? 1.0f : here.SpeedFactor;

        BlockState below = BlockBelowFeet();
        if (factor == 1.0f)
            factor = below.IsDefault ? 1.0f : below.SpeedFactor;

        // Interpolation is correct for both eras because the enchantment era is the special case where the efficiency can only be 0 or 1:
        //   Attribute-era calculation:
        //     factor = lerp(movementEfficiency, factor, 1.0F)
        // so lerp(1, factor, 1) == 1 is exactly 1.20.6's `return 1.0F`, and lerp(0, factor, 1) == factor is exactly its `return super`. Writing the lerp rather than `if (efficiency > 0) factor = 1` is not stylistic: a FRACTIONAL efficiency is reachable (any ADD_VALUE modifier on the attribute produces one), and the `if` would round it up into a full bypass.
        //
        // On 767+ SoulSpeedLevel is 0 and on 735-766 MovementEfficiency is 0, so the maximum below is never a blend of two live sources.
        float efficiency = Math.Max(
            _conditions.MovementEfficiency,
            _conditions.SoulSpeedLevel > 0 && IsSoulSpeedBlock(below) ? 1.0f : 0.0f);
        if (efficiency > 0.0f)
            factor += efficiency * (1.0f - factor);

        if (factor != 1.0f)
            _velocity = _velocity.Multiply(factor, 1.0, factor);

    }

    // The soul-speed block tag contains exactly soul sand and soul soil. UMPK has no block-tag model, so this matches by identifier, following the precedent IsSlime sets below. Both names are stable from 1.16, which is also the first version soul speed exists in, so unlike slime there is no pre-flattening spelling to carry.
    //
    // Matching the TAG and not the speed factor is the whole point. 0.4 is the only sub-unit speed factor in the game, so `factor == 0.4` cannot tell soul sand from honey - but it also silently EXCLUDES soul soil, which is in the tag at factor 1.0 and which a soul-speed wearer is genuinely boosted on.
    private static bool IsSoulSpeedBlock(BlockState state)
    {
        if (state.IsDefault)
            return false;

        string path = state.Block.Id.Path;
        return path.Equals("soul_sand", StringComparison.Ordinal)
            || path.Equals("soul_soil", StringComparison.Ordinal);
    }

    // Vanilla step on behavior: reduces horizontal speed walking on slime with small vertical speed.
    private void ApplySlimeStepOn()
    {
        if (!_onGround)
            return;

        BlockState below = BlockBelowFeet();
        if (!IsSlime(below))
            return;

        double absDeltaY = Math.Abs(_velocity.Y);
        if (absDeltaY >= 0.1 || _sneaking)
            return;

        double scale = 0.4 + absDeltaY * 0.2;
        _velocity = _velocity.Multiply(scale, 1.0, scale);
    }

    private BlockState BlockAtFeet() => _world.GetBlock(BlockPos.Containing(_position.X, _position.Y, _position.Z));

    private BlockState BlockBelowFeet() =>
        _world.GetBlock(BlockPos.Containing(_position.X, _position.Y - PhysicsConstants.BelowFeetOffset, _position.Z));

    // The flattening renamed minecraft:slime to minecraft:slime_block at 1.13; 1.8-1.12.2 registries spell it the short way (legacy block registration, id 165), so the modern name alone missed every bounce and every step-on slowdown on eleven protocols. MoveHelper's own slime predicate has carried both names since it was written; this one had not.
    private static bool IsSlime(BlockState state)
    {
        if (state.IsDefault)
            return false;

        string path = state.Block.Id.Path;
        return path.Equals("slime_block", StringComparison.Ordinal) || path.Equals("slime", StringComparison.Ordinal);
    }

    private static bool MthEqual(double a, double b) => Math.Abs(a - b) < PhysicsConstants.BlockedAxisEpsilon;

    // Snapshot

    private PhysicsState Snapshot() => new()
    {
        Position = _position,
        Velocity = _velocity,
        Yaw = _yaw,
        Pitch = _pitch,
        OnGround = _onGround,
        HorizontalCollision = _horizontalCollision,
        VerticalCollision = _verticalCollision,
        FallDistance = _fallDistance,
        Pose = _pose,
        BoundingBox = BoundingBox,
        InWater = _inWater,
        IsUnderWater = _isUnderWater,
        InLava = _inLava,
        OnClimbable = _onClimbable,
        IsSwimming = IsSwimmingState(),
        IsSprinting = _sprinting,
        IsGliding = _conditions.ElytraFlying && _profile.ElytraAvailable,
    };
}
