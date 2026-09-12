using Umpk.Client.Internal;
using Umpk.Client.Movement;
using Umpk.Client.State;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Actions;

/// <summary>Low-level movement actions. Terrain-level <see cref="MoveToAsync"/> and pathfinding <see cref="NavigateAsync"/> are provided by the navigator when the corresponding feature is enabled; this surface also exposes rotation, jump, sneak/sprint toggles, and arm swing.</summary>
public sealed class MovementActions
{
    private readonly IPacketSink _sink;
    private readonly ClientSessionServices _services;
    private readonly Func<Navigation.Navigator?> _navigator;
    private readonly MovementReporter _reporter;

    internal MovementActions(
        IPacketSink sink,
        ClientSessionServices services,
        Func<Navigation.Navigator?> navigator,
        MovementReporter? reporter = null)
    {
        _sink = sink;
        _services = services;
        _navigator = navigator;
        _reporter = reporter ?? new MovementReporter(services);
    }

    /// <summary>The first protocol whose <c>player_command</c> action enum has NO shift entries. 1.21.6 deleted <c>PRESS_SHIFT_KEY</c> and <c>RELEASE_SHIFT_KEY</c> from <c>ServerboundPlayerCommandPacket.Action</c> declared PRESS_SHIFT_KEY, RELEASE_SHIFT_KEY, STOP_SLEEPING, START_SPRINTING, STOP_SPRINTING, START_RIDING_JUMP, STOP_RIDING_JUMP, OPEN_INVENTORY, START_FALL_FLYING; 1.21.6 declared the same list without the first two (unchanged through 26.2). So every surviving ordinal moved DOWN BY TWO here and the shift pair has no ordinal at all.</summary>
    private const int NoShiftPlayerCommandMin = 771;
    private const int PlayerInputFlagsMin = 768;

    // player_command action ordinals. Two sets, because 1.21.6 removed the shift pair from the enum.
    private const int LegacyPressShiftKeyAction = 0;
    private const int LegacyReleaseShiftKeyAction = 1;
    private const int LegacyStopSleepingAction = 2;
    private const int LegacyStartSprintingAction = 3;
    private const int LegacyStopSprintingAction = 4;
    private const int ModernStopSleepingAction = 0;
    private const int ModernStartSprintingAction = 1;
    private const int ModernStopSprintingAction = 2;

    private SelfState Self => _services.State.Self;

    /// <summary>Whether this version carries the shift key on <c>player_input</c> rather than on <c>player_command</c>. From 1.21.6, the input packet is the only route to the server's shift flag. Below 1.21.6, <c>PRESS_SHIFT_KEY</c> on the player-command packet is the route.</summary>
    private bool ShiftTravelsOnPlayerInput => _services.Version.Version.Protocol >= NoShiftPlayerCommandMin;

    private bool InputFlagsAvailable => _services.Version.Version.Protocol >= PlayerInputFlagsMin
        && _services.Wire.CanSendPlay(EntityPackets.Serverbound.PlayerInput);

    /// <summary>Rotates the player to face a world position and sends the rotation.</summary>
    public async Task LookAtAsync(Vec3d target, CancellationToken ct = default)
    {
        Vec3d eye = Self.Position.Add(0, 1.62, 0);
        Vec3d delta = target.Subtract(eye);
        double horizontal = Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        float yaw = (float)(Math.Atan2(-delta.X, delta.Z) * 180.0 / Math.PI);
        float pitch = (float)(-Math.Atan2(delta.Y, horizontal) * 180.0 / Math.PI);
        await SetRotationAsync(yaw, pitch, ct).ConfigureAwait(false);
    }

    /// <summary>The pitch limits vanilla clamps a turn to; see <see cref="SetRotation"/>.</summary>
    private const float MinPitch = -90f;
    private const float MaxPitch = 90f;

    /// <summary>
    /// Turns the player WITHOUT putting anything on the wire, which is what moving a mouse actually does. Mouse input changes local angles, which reach the server through the next movement report. Called from the session loop (a plugin's <c>OnTick</c>, which runs before this client's own per-tick movement send), this reproduces that exactly: one turn, one packet, the tick's own.
    /// <para><see cref="SetRotationAsync"/> is the other shape and stays what it was, an immediate rotation packet. It is the right call for a one-off aim off the loop; it is the wrong call for a view that moves every tick, because the tick already reports position and rotation, so a per-tick <c>SetRotationAsync</c> puts TWO movement frames on the wire for one turn.</para>
    /// <para>Pitch is clamped to +/-90. Yaw is deliberately not wrapped: a client's yaw accumulates past 180 and 360 while the server wraps it on arrival, so wrapping here would emit a full-circle discontinuity that turning cannot produce.</para>
    /// </summary>
    /// <remarks>THREADING. This writes tracked state with no synchronisation, like the rest of <see cref="SelfState"/>. Call it from the session loop. Off the loop it races the tick's own read of the same two fields.</remarks>
    public void SetRotation(float yaw, float pitch)
    {
        Self.Yaw = yaw;
        Self.Pitch = Math.Clamp(pitch, MinPitch, MaxPitch);
    }

    /// <summary>Sets the player's yaw/pitch and sends a rotation update.</summary>
    public async Task SetRotationAsync(float yaw, float pitch, CancellationToken ct = default)
    {
        SetRotation(yaw, pitch);
        await _sink.SendAsync(
                new ServerboundMovePlayerRotPacket(Self.Yaw, Self.Pitch, Self.OnGround, HorizontalCollision: false), ct)
            .ConfigureAwait(false);
    }

    /// <summary>Sends the current position (and rotation) to the server.</summary>
    public Task SendPositionAsync(CancellationToken ct = default)
        => _sink.SendAsync(
            new ServerboundMovePlayerPosRotPacket(
                Self.Position.X, Self.Position.Y, Self.Position.Z, Self.Yaw, Self.Pitch, Self.OnGround,
                HorizontalCollision: false),
            ct).AsTask();

    /// <summary>Toggles the sneak input state and notifies the server, on the channel the negotiated version actually reads the shift key from.</summary>
    /// <remarks>This is also how a rider leaves a vehicle because there is no separate dismount packet; the server dismounts when the shift input is active. Sending the wrong channel therefore costs the sneak state and the dismount together. On 1.21.6+ the old ordinals instead mean STOP_SLEEPING and START_SPRINTING, so using the wrong channel can trigger an unrelated action.</remarks>
    public async Task SetSneakingAsync(bool sneaking, CancellationToken ct = default)
    {
        Self.Sneaking = sneaking;
        if (ShiftTravelsOnPlayerInput)
        {
            await SendPlayerInputAsync(ct).ConfigureAwait(false);
            _reporter.RecordExplicitInput(ManualInput());
            return;
        }

        await _sink.SendAsync(
            new ServerboundPlayerCommandPacket(
                Self.EntityId, sneaking ? LegacyPressShiftKeyAction : LegacyReleaseShiftKeyAction, 0),
            ct).ConfigureAwait(false);
        _reporter.RecordExplicitInput(ManualInput());
    }

    /// <summary>Toggles the held sprint key and immediately publishes it where the era has input flags.</summary>
    /// <remarks>This explicit low-level API preserves its immediate START/STOP command contract. Ordinary navigation uses the tick reporter, which emits entity-state edges only after hunger, direction and collision gates resolve the requested key. Protocol 768+ additionally carries the raw held key immediately on <c>player_input</c>.</remarks>
    public async Task SetSprintingAsync(bool sprinting, CancellationToken ct = default)
    {
        Self.SprintRequested = sprinting;
        int action = ShiftTravelsOnPlayerInput
            ? (sprinting ? ModernStartSprintingAction : ModernStopSprintingAction)
            : (sprinting ? LegacyStartSprintingAction : LegacyStopSprintingAction);
        await _sink.SendAsync(new ServerboundPlayerCommandPacket(Self.EntityId, action, 0), ct)
            .ConfigureAwait(false);
        Self.Sprinting = sprinting;

        if (InputFlagsAvailable)
        {
            await SendPlayerInputAsync(ct).ConfigureAwait(false);
            _reporter.RecordExplicitInput(ManualInput());
        }
    }

    /// <summary>Leaves the bed the player is sleeping in by sending the player-command STOP_SLEEPING action. The packet is bound on every protocol from 1.8 to 26.2, but the ACTION ORDINAL is not stable: 1.8 through 1.21.5 declare two shift actions ahead of STOP_SLEEPING and 1.21.6 removed them, so the ordinal is 2 below protocol 771 and 0 from 771 up. The server ignores the action when the player is not sleeping.</summary>
    public Task LeaveBedAsync(CancellationToken ct = default)
        => _sink.SendAsync(
            new ServerboundPlayerCommandPacket(
                Self.EntityId,
                ShiftTravelsOnPlayerInput ? ModernStopSleepingAction : LegacyStopSleepingAction,
                0),
            ct).AsTask();

    /// <summary>Sends the 1.21.2+ <c>player_input</c> flags record for the client's current input intent. Only the two states this surface owns are ever set; the directional and jump bits are the walk intent, which the physics engine drives frame by frame and which is not a toggle here. Bit layout is <c>Input.STREAM_CODEC</c>.</summary>
    private ValueTask SendPlayerInputAsync(CancellationToken ct)
        => _sink.SendAsync(
            new ServerboundPlayerInputPacket(
                Forward: false, Backward: false, Left: false, Right: false, Jump: false,
                Shift: Self.Sneaking, Sprint: Self.SprintRequested),
            ct);

    private MovementInput ManualInput() => new()
    {
        Sneak = Self.Sneaking,
        Sprint = Self.SprintRequested,
    };

    /// <summary>Swings the main arm.</summary>
    public Task SwingArmAsync(CancellationToken ct = default)
        => _sink.SendAsync(new ServerboundSwingPacket(0, HasHand: true), ct).AsTask();

    /// <summary>Straight-line/step movement to a target under the Physics feature (no planner).</summary>
    public Task MoveToAsync(Vec3d target, CancellationToken ct = default)
    {
        Navigation.Navigator nav = _navigator()
            ?? throw new FeatureDisabledException("Physics");
        return nav.MoveToAsync(target, ct);
    }

    /// <summary>Pathfinding navigation to a goal; requires the Pathfinding feature.</summary>
    public Task NavigateAsync(Umpk.Pathfinding.Goals.IGoal goal, CancellationToken ct = default)
    {
        Navigation.Navigator nav = _navigator()
            ?? throw new FeatureDisabledException("Pathfinding");
        return nav.NavigateAsync(goal, ct);
    }

    /// <summary><see cref="MoveToAsync"/>, verified against the player's own position afterwards; see <see cref="Navigation.Navigator.MoveToVerifiedAsync(Umpk.Geometry.Vec3d, System.Threading.CancellationToken)"/>.</summary>
    public Task<Navigation.MoveResult> MoveToVerifiedAsync(Vec3d target, CancellationToken ct)
        => MoveToVerifiedAsync(target, Umpk.Pathfinding.PathfinderOptions.Default, ct);

    /// <summary><see cref="MoveToVerifiedAsync(Vec3d, CancellationToken)"/> planned under caller-supplied options, for a caller that needs limits the defaults refuse (see <see cref="Umpk.Pathfinding.PathfinderOptions.UnsafeFalls"/>).</summary>
    public Task<Navigation.MoveResult> MoveToVerifiedAsync(
        Vec3d target, Umpk.Pathfinding.PathfinderOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Navigation.Navigator nav = _navigator()
            ?? throw new FeatureDisabledException("Physics");
        return nav.MoveToVerifiedAsync(target, options, ct);
    }

    /// <summary><see cref="NavigateAsync"/>, verified against an explicit <paramref name="target"/> point; see <see cref="Navigation.Navigator.NavigateVerifiedAsync(Umpk.Pathfinding.Goals.IGoal, Umpk.Geometry.Vec3d, System.Threading.CancellationToken)"/> for why the target cannot be derived from the goal alone.</summary>
    public Task<Navigation.MoveResult> NavigateVerifiedAsync(
        Umpk.Pathfinding.Goals.IGoal goal, Vec3d target, CancellationToken ct = default)
    {
        Navigation.Navigator nav = _navigator()
            ?? throw new FeatureDisabledException("Pathfinding");
        return nav.NavigateVerifiedAsync(goal, target, ct);
    }
}
