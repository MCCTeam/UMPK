using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Movement;

/// <summary>Owns vanilla's ordinary local-player publication state. Controller input, entity sprint edges and at most one movement variant are emitted from one eligible logical tick. Protocol 47 sends its legacy status-only form even for an unchanged pose; later eras elide unchanged ticks. Every era still refreshes position after 20 ticks. Explicit action sends and teleport echoes do not mutate this cadence.</summary>
internal sealed class MovementReporter(ClientSessionServices services)
{
    private const int InputFlagsMinProtocol = 768;
    private const int NoShiftPlayerCommandMinProtocol = 771;
    private const int LegacyMovementCadenceMaxProtocol = 47;
    private const int PositionReminderTicks = 20;

    private Vec3d _lastPosition;
    private float _lastYaw;
    private float _lastPitch;
    private bool _lastOnGround = true;
    private bool _lastHorizontalCollision;
    private MovementInput _lastInput;
    private bool _inputPrimed;
    private bool _legacyShift;
    private int _positionReminder;

    internal void Reset()
    {
        _lastPosition = default;
        _lastYaw = 0;
        _lastPitch = 0;
        _lastOnGround = true;
        _lastHorizontalCollision = false;
        _lastInput = MovementInput.None;
        _inputPrimed = true;
        _legacyShift = false;
        _positionReminder = 0;
    }

    /// <summary>Records a successful explicit input send without touching movement cadence.</summary>
    internal void RecordExplicitInput(MovementInput input)
    {
        _lastInput = input;
        _inputPrimed = true;
        if (services.Version.Version.Protocol < NoShiftPlayerCommandMinProtocol)
            _legacyShift = input.Sneak;
    }

    /// <summary>Publishes one eligible physics tick in vanilla order.</summary>
    internal async ValueTask ReportAsync(
        MovementInput requested,
        PhysicsState state,
        bool sendPosition,
        Func<object, string, ValueTask<bool>> send)
    {
        ArgumentNullException.ThrowIfNull(send);

        await ReportInputAsync(requested, send).ConfigureAwait(false);
        await ReportSprintAsync(state.IsSprinting, send).ConfigureAwait(false);

        if (!sendPosition)
            return;

        _positionReminder++;
        bool moved = PositionReportPolicy.HasMoved(_lastPosition, state.Position)
            || _positionReminder >= PositionReminderTicks;
        bool rotated = state.Yaw != _lastYaw || state.Pitch != _lastPitch;
        bool statusChanged = state.OnGround != _lastOnGround
            || state.HorizontalCollision != _lastHorizontalCollision;

        object? packet = (moved, rotated, statusChanged) switch
        {
            (true, true, _) => new ServerboundMovePlayerPosRotPacket(
                state.Position.X, state.Position.Y, state.Position.Z,
                state.Yaw, state.Pitch, state.OnGround, state.HorizontalCollision),
            (true, false, _) => new ServerboundMovePlayerPosPacket(
                state.Position.X, state.Position.Y, state.Position.Z,
                state.OnGround, state.HorizontalCollision),
            (false, true, _) => new ServerboundMovePlayerRotPacket(
                state.Yaw, state.Pitch, state.OnGround, state.HorizontalCollision),
            (false, false, _) when services.Version.Version.Protocol <= LegacyMovementCadenceMaxProtocol =>
                new ServerboundMovePlayerStatusPacket(state.OnGround),
            (false, false, true) when services.Wire.CanSendPlay(EntityPackets.Serverbound.MovePlayerStatusOnly) =>
                new ServerboundMovePlayerStatusOnlyPacket(state.OnGround, state.HorizontalCollision),
            (false, false, true) => new ServerboundMovePlayerStatusPacket(state.OnGround),
            _ => null,
        };

        if (packet is null)
            return;

        if (!await send(packet, "move_player").ConfigureAwait(false))
            return;

        // These are success-dependent records. A failed send must leave the next tick owing the same edge. Position's reminder resets only for a packet that carries position.
        if (moved)
        {
            _lastPosition = state.Position;
            _positionReminder = 0;
        }

        if (rotated)
        {
            _lastYaw = state.Yaw;
            _lastPitch = state.Pitch;
        }

        _lastOnGround = state.OnGround;
        _lastHorizontalCollision = state.HorizontalCollision;
    }

    private async ValueTask ReportInputAsync(MovementInput input, Func<object, string, ValueTask<bool>> send)
    {
        int protocol = services.Version.Version.Protocol;
        if (protocol >= InputFlagsMinProtocol && services.Wire.CanSendPlay(EntityPackets.Serverbound.PlayerInput))
        {
            if (_inputPrimed && input == _lastInput)
                return;

            bool sent = await send(
                    new ServerboundPlayerInputPacket(
                        input.Forward, input.Back, input.Left, input.Right, input.Jump, input.Sneak, input.Sprint),
                    "player_input")
                .ConfigureAwait(false);
            if (!sent)
                return;
            _lastInput = input;
            _inputPrimed = true;
            return;
        }

        // Before the shift key moved to player_input it was itself an entity action. Follow the held key, not the engine's crouching collision pose.
        if (protocol < NoShiftPlayerCommandMinProtocol && input.Sneak != _legacyShift)
        {
            bool sent = await send(
                    new ServerboundPlayerCommandPacket(
                        services.State.Self.EntityId,
                        input.Sneak ? 0 : 1,
                        0),
                    "player_command:shift")
                .ConfigureAwait(false);
            if (!sent)
                return;
            _legacyShift = input.Sneak;
        }
    }

    private async ValueTask ReportSprintAsync(bool actualSprint, Func<object, string, ValueTask<bool>> send)
    {
        SelfState self = services.State.Self;
        if (actualSprint == self.Sprinting)
            return;

        bool modern = services.Version.Version.Protocol >= NoShiftPlayerCommandMinProtocol;
        bool sent = await send(
                new ServerboundPlayerCommandPacket(
                    self.EntityId,
                    modern
                        ? (actualSprint ? 1 : 2)
                        : (actualSprint ? 3 : 4),
                    0),
                "player_command:sprint")
            .ConfigureAwait(false);
        if (!sent)
            return;
        self.Sprinting = actualSprint;
    }
}
