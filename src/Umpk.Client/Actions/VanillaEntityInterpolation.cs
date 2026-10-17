using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Native living interpolation arithmetic profiles established by the client artifact traces.</summary>
internal enum VanillaInterpolationProfile
{
    LegacyDoubleRotation,
    LegacyFloatRotation,
    LinearStep,
    ClampedLinearStep,
    HandlerOffsetBox,
    Handler,
    DeduplicatingHandler,
    Stepped,
}

internal readonly record struct VanillaPositionStep(Vec3d Position, int TickOffset);

/// <summary>Current entity position, rotation and bounds, advanced only by the caller's session tick. Packet-coordinate bases remain separate from this state.</summary>
internal sealed class VanillaEntityInterpolation
{
    private readonly VanillaInterpolationProfile _profile;
    private readonly int _length;
    private readonly List<Step> _steps = [];
    private VanillaEntityBounds _bounds;
    private Pose _target;
    private Pose _previous;
    private Pose _lastStep;
    private bool _hasPrevious;
    private int _remaining;
    private int _head;
    private float _currentStepTicks;
    private float _remainingTicks;
    private float _speed = 1.0F;

    public VanillaEntityInterpolation(VanillaInterpolationProfile profile, Vec3d position, float yaw, float pitch, float width, float height, int length)
    {
        _profile = profile;
        _length = length;
        _bounds = VanillaEntityBounds.Create(position, width, height);
        SetRotation(yaw, pitch);
    }

    public Vec3d Position => _bounds.Position;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public Aabb Bounds => _bounds.Box;
    public bool HasActiveInterpolation => _profile == VanillaInterpolationProfile.Stepped ? _head < _steps.Count : _remaining > 0;
    public Vec3d TargetPosition => HasActiveInterpolation ? _target.Position : Position;
    public float TargetYaw => HasActiveInterpolation ? _target.Yaw : Yaw;
    public float TargetPitch => HasActiveInterpolation ? _target.Pitch : Pitch;

    private Pose Current => new(Position, Yaw, Pitch);
    private bool UsesHandler => _profile >= VanillaInterpolationProfile.HandlerOffsetBox;

    private bool ClampsPitch => _profile >= VanillaInterpolationProfile.ClampedLinearStep;

    public static VanillaEntityInterpolation CreateRemotePlayer(int protocol, Vec3d position, float yaw, float pitch, float width, float height)
    {
        VanillaInterpolationProfile profile = protocol switch
        {
            <= 754 => VanillaInterpolationProfile.LegacyDoubleRotation,
            <= 763 => VanillaInterpolationProfile.LegacyFloatRotation,
            <= 767 => VanillaInterpolationProfile.LinearStep,
            <= 769 => VanillaInterpolationProfile.ClampedLinearStep,
            770 => VanillaInterpolationProfile.HandlerOffsetBox,
            <= 772 => VanillaInterpolationProfile.Handler,
            <= 776 => VanillaInterpolationProfile.DeduplicatingHandler,
            _ => VanillaInterpolationProfile.Stepped,
        };
        var controller = new VanillaEntityInterpolation(profile, position, 0, 0, width, height, protocol >= 777 ? 2 : 3);
        controller.SetCurrentRotation(yaw, pitch);
        return controller;
    }

    public void SetPosition(Vec3d position) => _bounds = _bounds.AtPosition(position);

    public void SetRotation(float yaw, float pitch)
    {
        Yaw = yaw % 360.0F;
        Pitch = ClampsPitch ? Math.Clamp(pitch % 360.0F, -90.0F, 90.0F) : pitch % 360.0F;
    }

    // Native direct setters preserve yaw outside one turn; setXRot starts wrapping and clamping pitch in 1.21.2.
    public void SetCurrentRotation(float yaw, float pitch)
    {
        if (float.IsFinite(yaw))
            Yaw = yaw;
        if (float.IsFinite(pitch))
            Pitch = ClampsPitch ? Math.Clamp(pitch % 360.0F, -90.0F, 90.0F) : pitch;
    }

    public void RefreshDimensions(float width, float height, bool recenter)
        => _bounds = _bounds.Refresh(width, height, recenter);

    public void SetTarget(Vec3d? position, float? yaw, float? pitch)
    {
        var target = new Pose(position ?? TargetPosition, yaw ?? TargetYaw, pitch ?? TargetPitch);
        AcceptTarget(target, []);
    }

    public void SetPath(ReadOnlySpan<VanillaPositionStep> steps, float? yaw, float? pitch)
    {
        if (_profile != VanillaInterpolationProfile.Stepped || steps.IsEmpty)
            throw new InvalidOperationException("A nonempty stepped position path requires the stepped interpolation profile.");
        AcceptTarget(new Pose(steps[^1].Position, yaw ?? TargetYaw, pitch ?? TargetPitch), steps);
    }

    private void AcceptTarget(Pose target, ReadOnlySpan<VanillaPositionStep> path)
    {
        if (!UsesHandler)
        {
            _target = target;
            _remaining = _length;
            return;
        }
        if (_length == 0)
        {
            SetPosition(target.Position);
            // Native snapTo assigns these angles directly; setRot applies the remainder only during interpolation.
            SetCurrentRotation(target.Yaw, target.Pitch);
            Cancel();
            return;
        }
        if (HasActiveInterpolation && IsDuplicate(target))
            return;
        if (_profile == VanillaInterpolationProfile.Stepped)
        {
            if (!HasActiveInterpolation)
            {
                _lastStep = Current;
                _currentStepTicks = 1.0F;
            }
            if (path.IsEmpty || PositionEquals(target.Position, _target.Position))
                AddStep(target, _length);
            else
                AddPath(path, target);
        }
        else
            _remaining = _length;
        _target = target;
        _previous = Current;
        _hasPrevious = true;
    }

    private bool IsDuplicate(Pose target)
    {
        if (!PositionEquals(target.Position, _target.Position))
            return false;
        return _profile switch
        {
            VanillaInterpolationProfile.DeduplicatingHandler => FloatEquals(target.Yaw, _target.Yaw) && FloatEquals(target.Pitch, _target.Pitch),
            VanillaInterpolationProfile.Stepped => target.Yaw == _target.Yaw && target.Pitch == _target.Pitch,
            _ => false,
        };
    }

    public void Cancel()
    {
        _remaining = 0;
        _steps.Clear();
        _head = 0;
        _remainingTicks = 0.0F;
        _speed = 1.0F;
        if (_profile != VanillaInterpolationProfile.Stepped)
            _hasPrevious = false;
    }

    public void ApplyPredictedMovement(Vec3d delta, Func<Aabb, bool> isCollisionFree)
    {
        if (_profile != VanillaInterpolationProfile.Stepped)
            throw new InvalidOperationException("Predicted movement requires the stepped interpolation profile.");
        AdjustPosition(delta, isCollisionFree);
        _previous = _previous with { Position = _previous.Position.Add(delta) };
    }

    public void Tick(Func<Aabb, bool> isCollisionFree, float relativeTickSpeed = 1.0F)
    {
        if (!HasActiveInterpolation)
        {
            if (UsesHandler)
                Cancel();
            return;
        }
        if (!UsesHandler)
        {
            TickLegacy();
            return;
        }
        if (_hasPrevious)
        {
            Vec3d delta = Position.Subtract(_previous.Position);
            if (_profile != VanillaInterpolationProfile.Stepped || delta.LengthSqr() > 1.0E-5F)
                AdjustPosition(delta, isCollisionFree);
            AddRotation(Yaw - _previous.Yaw, Pitch - _previous.Pitch);
        }
        if (_profile == VanillaInterpolationProfile.Stepped)
        {
            Pose next = NextStep();
            SetPosition(next.Position);
            SetRotation(next.Yaw, next.Pitch);
            AdvancePath(relativeTickSpeed);
            CompactSteps();
        }
        else
        {
            double alpha = 1.0 / _remaining;
            SetPosition(Lerp(alpha, Position, _target.Position));
            SetRotation((float)(Yaw + alpha * Wrap((double)_target.Yaw - Yaw)), (float)(Pitch + alpha * ((double)_target.Pitch - Pitch)));
            _remaining--;
        }
        _previous = Current;
        _hasPrevious = true;
    }

    private void TickLegacy()
    {
        Vec3d position;
        float yaw, pitch;
        if (_profile is VanillaInterpolationProfile.LinearStep or VanillaInterpolationProfile.ClampedLinearStep)
        {
            double alpha = 1.0 / _remaining;
            position = Lerp(alpha, Position, _target.Position);
            yaw = (float)(Yaw + alpha * Wrap((double)_target.Yaw - Yaw));
            pitch = (float)(Pitch + alpha * ((double)_target.Pitch - Pitch));
        }
        else
        {
            position = new(Position.X + (_target.Position.X - Position.X) / _remaining, Position.Y + (_target.Position.Y - Position.Y) / _remaining, Position.Z + (_target.Position.Z - Position.Z) / _remaining);
            double yawDelta = Wrap((double)_target.Yaw - Yaw);
            double pitchDelta = (double)_target.Pitch - Pitch;
            if (_profile == VanillaInterpolationProfile.LegacyDoubleRotation)
            {
                yaw = (float)(Yaw + yawDelta / _remaining);
                pitch = (float)(Pitch + pitchDelta / _remaining);
            }
            else
            {
                yaw = Yaw + (float)yawDelta / _remaining;
                pitch = Pitch + (float)pitchDelta / _remaining;
            }
        }
        _remaining--;
        SetPosition(position);
        SetRotation(yaw, pitch);
    }

    private void AdjustPosition(Vec3d delta, Func<Aabb, bool> isCollisionFree)
    {
        Vec3d adjusted = _target.Position.Add(delta);
        Aabb query = _profile == VanillaInterpolationProfile.HandlerOffsetBox
            ? VanillaEntityBounds.Create(Position, _bounds.Width, _bounds.Height).Box.Move(adjusted.X, adjusted.Y, adjusted.Z)
            : VanillaEntityBounds.Create(adjusted, _bounds.Width, _bounds.Height).Box;
        if (!isCollisionFree(query))
            return;
        _target = _target with { Position = adjusted };
        if (_profile != VanillaInterpolationProfile.Stepped)
            return;
        _lastStep = _lastStep with { Position = _lastStep.Position.Add(delta) };
        for (int i = _head; i < _steps.Count; i++)
            _steps[i] = _steps[i] with { Value = _steps[i].Value with { Position = _steps[i].Value.Position.Add(delta) } };
    }

    private void AddRotation(float yaw, float pitch)
    {
        _target = _target with { Yaw = _target.Yaw + yaw, Pitch = _target.Pitch + pitch };
        if (_profile != VanillaInterpolationProfile.Stepped)
            return;
        _lastStep = _lastStep with { Yaw = _lastStep.Yaw + yaw, Pitch = _lastStep.Pitch + pitch };
        for (int i = _head; i < _steps.Count; i++)
            _steps[i] = _steps[i] with { Value = _steps[i].Value with { Yaw = _steps[i].Value.Yaw + yaw, Pitch = _steps[i].Value.Pitch + pitch } };
    }

    private void AddStep(Pose target, int ticks)
    {
        _steps.Add(new(target, ticks));
        _remainingTicks += ticks;
    }

    private void AddPath(ReadOnlySpan<VanillaPositionStep> path, Pose target)
    {
        bool sameRotation = target.Yaw == _target.Yaw && target.Pitch == _target.Pitch;
        int total = 0;
        foreach (VanillaPositionStep step in path)
            total = unchecked(total + step.TickOffset);
        int offset = 0;
        foreach (VanillaPositionStep step in path)
        {
            offset = unchecked(offset + step.TickOffset);
            float alpha = (float)offset / total;
            float yaw = sameRotation ? target.Yaw : _target.Yaw + alpha * Wrap(target.Yaw - _target.Yaw);
            float pitch = sameRotation ? target.Pitch : _target.Pitch + alpha * (target.Pitch - _target.Pitch);
            AddStep(new(step.Position, yaw, pitch), step.TickOffset);
        }
    }

    private Pose NextStep()
    {
        while (_head < _steps.Count)
        {
            Step step = _steps[_head];
            if (_currentStepTicks < step.Ticks)
            {
                float alpha = _currentStepTicks / step.Ticks;
                return new(Lerp(alpha, _lastStep.Position, step.Value.Position), _lastStep.Yaw + alpha * Wrap(step.Value.Yaw - _lastStep.Yaw), _lastStep.Pitch + alpha * (step.Value.Pitch - _lastStep.Pitch));
            }
            _currentStepTicks -= step.Ticks;
            _lastStep = step.Value;
            _head++;
        }
        return _target;
    }

    private void AdvancePath(float ticks)
    {
        float targetSpeed = Math.Max(_remainingTicks / _length, 1.0F);
        _speed += 1.0F / _length * (targetSpeed - _speed);
        if (ticks * _speed < _remainingTicks)
            ticks *= _speed;
        else
        {
            ticks = _remainingTicks;
            _speed = 1.0F;
        }
        _currentStepTicks += ticks;
        _remainingTicks -= ticks;
    }

    private void CompactSteps()
    {
        if (_head == _steps.Count)
        {
            _steps.Clear();
            _head = 0;
        }
        else if (_head >= 64 && _head >= _steps.Count / 2)
        {
            _steps.RemoveRange(0, _head);
            _head = 0;
        }
    }

    private static Vec3d Lerp(double alpha, Vec3d from, Vec3d to)
        => new(from.X + alpha * (to.X - from.X), from.Y + alpha * (to.Y - from.Y), from.Z + alpha * (to.Z - from.Z));

    private static double Wrap(double value)
    {
        value %= 360.0;
        if (value >= 180.0) value -= 360.0;
        if (value < -180.0) value += 360.0;
        return value;
    }

    private static float Wrap(float value)
    {
        value %= 360.0F;
        if (value >= 180.0F) value -= 360.0F;
        if (value < -180.0F) value += 360.0F;
        return value;
    }

    private static bool FloatEquals(float left, float right)
        => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right) || float.IsNaN(left) && float.IsNaN(right);

    private static bool DoubleEquals(double left, double right)
        => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right) || double.IsNaN(left) && double.IsNaN(right);

    private static bool PositionEquals(Vec3d left, Vec3d right)
        => DoubleEquals(left.X, right.X) && DoubleEquals(left.Y, right.Y) && DoubleEquals(left.Z, right.Z);

    private readonly record struct Pose(Vec3d Position, float Yaw, float Pitch);
    private readonly record struct Step(Pose Value, int Ticks);
}
