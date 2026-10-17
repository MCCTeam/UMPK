using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Remote-player velocity arithmetic advanced only by the caller's session tick. A zero interval applies observations immediately.</summary>
internal sealed class VanillaEntityVelocity
{
    private readonly int _length;
    private Vec3d _target;
    private int _remaining;

    public VanillaEntityVelocity(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _length = length;
    }

    public Vec3d Value { get; private set; }

    public void Observe(Vec3d velocity)
    {
        _target = velocity;
        _remaining = _length;
        if (_length == 0)
            Value = velocity;
    }

    // Native setDeltaMovement changes the current velocity without canceling a pending lerpMotion target.
    public void SetCurrent(Vec3d velocity) => Value = velocity;

    public void Tick()
    {
        if (_remaining == 0)
            return;
        Value = new Vec3d(Value.X + (_target.X - Value.X) / _remaining, Value.Y + (_target.Y - Value.Y) / _remaining, Value.Z + (_target.Z - Value.Z) / _remaining);
        _remaining--;
    }
}
