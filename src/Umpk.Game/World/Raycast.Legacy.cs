using Umpk.Geometry;

namespace Umpk.Game.World;

public static partial class Raycast
{
    private static bool ClipLegacyBoxes(ReadOnlySpan<Aabb> boxes, Vec3d offset, bool orderedList, BlockPos position, Vec3d start, Vec3d delta, out Vec3d point, out Direction face)
    {
        var blockPosition = new Vec3d(position.X, position.Y, position.Z);
        Vec3d localStart = start.Subtract(blockPosition);
        Vec3d end = start.Add(delta);
        Vec3d localDelta = end.Subtract(blockPosition).Subtract(localStart);
        double selected = orderedList ? 0 : double.PositiveInfinity;
        point = Vec3d.Zero;
        face = Direction.Up;
        bool found = false;
        foreach (Aabb source in boxes)
        {
            Aabb box = source.Move(offset.X, offset.Y, offset.Z);
            double closest = double.PositiveInfinity;
            Vec3d boxPoint = Vec3d.Zero;
            Direction boxFace = Direction.Up;
            bool hit = false;
            hit |= LegacyPlane(localStart, localDelta, box, 0, box.MinX, Direction.West, ref closest, ref boxPoint, ref boxFace);
            hit |= LegacyPlane(localStart, localDelta, box, 0, box.MaxX, Direction.East, ref closest, ref boxPoint, ref boxFace);
            hit |= LegacyPlane(localStart, localDelta, box, 1, box.MinY, Direction.Down, ref closest, ref boxPoint, ref boxFace);
            hit |= LegacyPlane(localStart, localDelta, box, 1, box.MaxY, Direction.Up, ref closest, ref boxPoint, ref boxFace);
            hit |= LegacyPlane(localStart, localDelta, box, 2, box.MinZ, Direction.North, ref closest, ref boxPoint, ref boxFace);
            hit |= LegacyPlane(localStart, localDelta, box, 2, box.MaxZ, Direction.South, ref closest, ref boxPoint, ref boxFace);
            if (!hit)
                continue;
            Vec3d candidate = boxPoint.Add(blockPosition);
            double distance = orderedList ? candidate.Subtract(end).LengthSqr() : closest;
            if (orderedList ? distance > selected : distance < selected)
            {
                selected = distance;
                point = candidate;
                face = boxFace;
                found = true;
            }
        }
        return found;
    }

    private static bool LegacyPlane(Vec3d start, Vec3d delta, Aabb box, int axis, double plane, Direction candidate, ref double closest, ref Vec3d point, ref Direction face)
    {
        double origin = axis == 0 ? start.X : axis == 1 ? start.Y : start.Z;
        double component = axis == 0 ? delta.X : axis == 1 ? delta.Y : delta.Z;
        if (component * component < 1.0000000116860974E-7)
            return false;
        double fraction = (plane - origin) / component;
        if (fraction < 0 || fraction > 1)
            return false;
        Vec3d hit = start.Add(delta.Scale(fraction));
        if ((axis != 0 && (hit.X < box.MinX || hit.X > box.MaxX))
            || (axis != 1 && (hit.Y < box.MinY || hit.Y > box.MaxY))
            || (axis != 2 && (hit.Z < box.MinZ || hit.Z > box.MaxZ)))
            return false;
        double distance = hit.Subtract(start).LengthSqr();
        if (distance >= closest)
            return false;
        closest = distance;
        point = new Vec3d(hit.X == 0 ? 0 : hit.X, hit.Y == 0 ? 0 : hit.Y, hit.Z == 0 ? 0 : hit.Z);
        face = candidate;
        return true;
    }
}
