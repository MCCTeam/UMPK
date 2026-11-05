using Umpk.Geometry;

namespace Umpk.Client.Actions;

/// <summary>Resolved client entity dimensions and their current bounds. Dimension refresh and position reapplication are distinct native operations.</summary>
internal readonly record struct VanillaEntityBounds(Vec3d Position, float Width, float Height, Aabb Box)
{
    public static VanillaEntityBounds Create(Vec3d position, float width, float height)
    {
        float half = width / 2.0F;
        return new(position, width, height, new Aabb(position.X - half, position.Y, position.Z - half, position.X + half, position.Y + height, position.Z + half));
    }

    public VanillaEntityBounds Refresh(float width, float height, bool recenter)
    {
        if (recenter)
            return Create(Position, width, height);
        if (width < Width)
        {
            double half = width / 2.0;
            return new(Position, width, height, new Aabb(Position.X - half, Position.Y, Position.Z - half, Position.X + half, Position.Y + height, Position.Z + half));
        }
        return new(Position, width, height, new Aabb(Box.MinX, Box.MinY, Box.MinZ, Box.MinX + width, Box.MinY + height, Box.MinZ + width));
    }

    public VanillaEntityBounds AtPosition(Vec3d position) => Create(position, Width, Height);
}
