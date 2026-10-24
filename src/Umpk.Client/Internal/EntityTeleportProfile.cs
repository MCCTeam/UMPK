using Umpk.Client.Actions;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Native modern teleport arithmetic and current/target transitions. Packet-coordinate bases remain separately owned.</summary>
internal readonly record struct EntityTeleportProfile(bool ClampPitchBeforeRotation, bool WideTrigIndex, bool UsesPendingRotation)
{
    public static EntityTeleportProfile ForProtocol(int protocol) => new(protocol >= 770, protocol >= 774, protocol >= 770);

    public PositionMoveRotation GetSource(VanillaEntityInterpolation position, Vec3d knownMovement)
        => new(position.TargetPosition, knownMovement, UsesPendingRotation ? position.TargetYaw : position.Yaw, UsesPendingRotation ? position.TargetPitch : position.Pitch);

    // The caller supplies native known movement and tick/authority eligibility. Direct setters preserve pending position and velocity controllers.
    public bool Apply(VanillaEntityInterpolation position, VanillaEntityVelocity movement, Vec3d knownMovement, PositionMoveRotation change, int flags, bool interpolate)
    {
        PositionMoveRotation source = GetSource(position, knownMovement);
        PositionMoveRotation destination = Resolve(source, change, flags);
        bool tooFar = source.Position.Subtract(destination.Position).LengthSqr() > 4096.0;
        bool interpolated = interpolate && !tooFar;
        if (interpolated)
            position.SetTarget(destination.Position, destination.YRot, destination.XRot);
        else
        {
            position.SetPosition(destination.Position);
            position.SetCurrentRotation(destination.YRot, destination.XRot);
        }
        movement.SetCurrent(destination.DeltaMovement);
        return interpolated;
    }

    public PositionMoveRotation Resolve(PositionMoveRotation source, PositionMoveRotation change, int flags)
    {
        var position = new Vec3d(
            ((flags & 1) != 0 ? source.Position.X : 0.0) + change.Position.X,
            ((flags & 2) != 0 ? source.Position.Y : 0.0) + change.Position.Y,
            ((flags & 4) != 0 ? source.Position.Z : 0.0) + change.Position.Z);
        float yaw = ((flags & 8) != 0 ? source.YRot : 0.0F) + change.YRot;
        float pitch = ((flags & 16) != 0 ? source.XRot : 0.0F) + change.XRot;
        if (ClampPitchBeforeRotation)
            pitch = Math.Clamp(pitch, -90.0F, 90.0F);
        Vec3d current = source.DeltaMovement;
        if ((flags & 256) != 0)
        {
            float x = (float)((source.XRot - pitch) * (Math.PI / 180.0));
            float y = (float)((source.YRot - yaw) * (Math.PI / 180.0));
            (float cosX, float sinX) = VanillaTrigonometry.SinCos(x, WideTrigIndex);
            current = new Vec3d(current.X, (current.Y * cosX) + (current.Z * sinX), (current.Z * cosX) - (current.Y * sinX));
            (float cosY, float sinY) = VanillaTrigonometry.SinCos(y, WideTrigIndex);
            current = new Vec3d((current.X * cosY) + (current.Z * sinY), current.Y, (current.Z * cosY) - (current.X * sinY));
        }
        var movement = new Vec3d(
            (flags & 32) != 0 ? current.X + change.DeltaMovement.X : change.DeltaMovement.X,
            (flags & 64) != 0 ? current.Y + change.DeltaMovement.Y : change.DeltaMovement.Y,
            (flags & 128) != 0 ? current.Z + change.DeltaMovement.Z : change.DeltaMovement.Z);
        return new PositionMoveRotation(position, movement, yaw, pitch);
    }

}
