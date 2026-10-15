using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Game.World;

public static partial class Raycast
{
    private static bool TryHitTargeting(BlockPos position, Vec3d start, Vec3d delta, BlockTargetingShape geometry, out BlockHitResult result)
    {
        result = BlockHitResult.Miss;
        if (!ClipTargetingShape(geometry.Outline.Span, geometry.OutlineOffset, geometry.ClipModel, position, start, delta, out Vec3d point, out Direction face, out bool inside))
            return false;
        if (ClipTargetingShape(geometry.Interaction.Span, geometry.InteractionOffset, geometry.ClipModel, position, start, delta, out Vec3d interactionPoint, out Direction interactionFace, out _)
            && interactionPoint.Subtract(start).LengthSqr() < point.Subtract(start).LengthSqr())
            face = interactionFace;
        result = BlockHitResult.FromHit(position, face, point, point.Subtract(start).Length(), inside);
        return true;
    }

    private static bool ClipTargetingShape(ReadOnlySpan<Aabb> boxes, Vec3d offset, BlockRayClipModel model, BlockPos position, Vec3d start, Vec3d delta, out Vec3d point, out Direction face, out bool inside)
    {
        point = Vec3d.Zero;
        face = Direction.Up;
        inside = false;
        if (boxes.IsEmpty)
            return false;
        if (model is BlockRayClipModel.LegacyBox or BlockRayClipModel.LegacyBoxList)
            return ClipLegacyBoxes(boxes, offset, model == BlockRayClipModel.LegacyBoxList, position, start, delta, out point, out face);
        if (delta.LengthSqr() < 1.0E-7)
            return false;
        {
            Vec3d probe = start.Add(delta.Scale(0.001));
            Vec3d local = probe.Subtract(new Vec3d(position.X, position.Y, position.Z));
            foreach (Aabb source in boxes)
            {
                Aabb box = source.Move(offset.X, offset.Y, offset.Z);
                if (local.X >= box.MinX && local.X < box.MaxX && local.Y >= box.MinY && local.Y < box.MaxY && local.Z >= box.MinZ && local.Z < box.MaxZ)
                {
                    point = probe;
                    face = NearestFloatDirection(delta);
                    if (model == BlockRayClipModel.VoxelOppositeInside)
                        face = face.Opposite();
                    inside = true;
                    return true;
                }
            }
        }

        double best = 1.0;
        bool hit = false;
        foreach (Aabb source in boxes)
        {
            Aabb box = source.Move(offset.X, offset.Y, offset.Z).Move(position.X, position.Y, position.Z);
            {
                if (delta.X > 1.0E-7)
                    hit |= ClipPlane(start.X, delta.X, box.MinX, start.Y, delta.Y, box.MinY, box.MaxY, start.Z, delta.Z, box.MinZ, box.MaxZ, Direction.West, ref best, ref face);
                else if (delta.X < -1.0E-7)
                    hit |= ClipPlane(start.X, delta.X, box.MaxX, start.Y, delta.Y, box.MinY, box.MaxY, start.Z, delta.Z, box.MinZ, box.MaxZ, Direction.East, ref best, ref face);
                if (delta.Y > 1.0E-7)
                    hit |= ClipPlane(start.Y, delta.Y, box.MinY, start.X, delta.X, box.MinX, box.MaxX, start.Z, delta.Z, box.MinZ, box.MaxZ, Direction.Down, ref best, ref face);
                else if (delta.Y < -1.0E-7)
                    hit |= ClipPlane(start.Y, delta.Y, box.MaxY, start.X, delta.X, box.MinX, box.MaxX, start.Z, delta.Z, box.MinZ, box.MaxZ, Direction.Up, ref best, ref face);
                if (delta.Z > 1.0E-7)
                    hit |= ClipPlane(start.Z, delta.Z, box.MinZ, start.X, delta.X, box.MinX, box.MaxX, start.Y, delta.Y, box.MinY, box.MaxY, Direction.North, ref best, ref face);
                else if (delta.Z < -1.0E-7)
                    hit |= ClipPlane(start.Z, delta.Z, box.MaxZ, start.X, delta.X, box.MinX, box.MaxX, start.Y, delta.Y, box.MinY, box.MaxY, Direction.South, ref best, ref face);
            }
        }
        if (hit)
            point = start.Add(delta.Scale(best));
        return hit;
    }

    private static bool ClipPlane(double origin, double delta, double plane, double originA, double deltaA, double minA, double maxA, double originB, double deltaB, double minB, double maxB, Direction candidate, ref double best, ref Direction face)
    {
        double fraction = (plane - origin) / delta;
        if (fraction >= best || fraction <= 0)
            return false;
        double a = originA + fraction * deltaA, b = originB + fraction * deltaB;
        const double tolerance = 1.0E-7;
        if (a < minA - tolerance || a > maxA + tolerance || b < minB - tolerance || b > maxB + tolerance)
            return false;
        best = fraction;
        face = candidate;
        return true;
    }

    private static Direction NearestFloatDirection(Vec3d delta)
    {
        float x = (float)delta.X, y = (float)delta.Y, z = (float)delta.Z;
        float highest = float.Epsilon;
        Direction result = Direction.North;
        for (Direction direction = Direction.Down; direction <= Direction.East; direction++)
        {
            float dot = x * direction.StepX() + y * direction.StepY() + z * direction.StepZ();
            if (dot > highest)
            {
                highest = dot;
                result = direction;
            }
        }
        return result;
    }
}
