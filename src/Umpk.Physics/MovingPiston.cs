using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>The entity a <see cref="MovingPiston"/> is allowed to push. Implemented by <see cref="PlayerPhysics"/> for the local player; tests implement it over a bare box.</summary>
/// <remarks>A piston pushes every entity intersecting its movement region. A client only owns the local player's movement, so this seam is deliberately single-entity: a tracked remote entity's position is the server's to send, and simulating a push for it would invent a position the client has no authority over.</remarks>
public interface IPistonPushTarget
{
    /// <summary>The entity's current world-space bounding box.</summary>
    Aabb BoundingBox { get; }

    /// <summary>Applies a piston displacement with collision.</summary>
    void MoveByPiston(Vec3d motion);
}

/// <summary>Models the progress and entity push of one live <c>minecraft:moving_piston</c> block entity.</summary>
/// <remarks>
/// <para>A piston push runs on both sides and sends no separate packet for entity displacement. The client reconstructs moving blocks from the block event and must simulate its own displacement.</para>
/// <para>Source heads and bases are always modeled. Non-source instances represent blocks resolved by the client-side structure resolver. This type carries only collision shapes, not the moved block's identity.</para>
/// <para>No self-collision suppression is needed because static datasets assign no collision boxes to <c>minecraft:moving_piston</c>; its live shape comes from this object.</para>
/// <para>Slime launch velocity, honey-top dragging, and final block replacement are not modeled. The first two depend on moved-block identity or nearby entities, which this single-target collision model does not expose. Ordinary piston displacement remains collision checked.</para>
/// </remarks>
public sealed class MovingPiston
{
    /// <summary>Extra displacement that prevents the target from remaining flush with the moving face.</summary>
    public const double PushOffset = 0.01;

    /// <summary>Per-game-tick, per-axis piston displacement clamp.</summary>
    public const double TickMovement = 0.51;

    /// <summary>Progress added on each tick.</summary>
    private const float ProgressStep = 0.5f;

    private readonly Aabb[] _shape;
    private readonly Aabb[] _shortShape;
    private readonly BlockPos _position;
    private readonly Direction _direction;
    private readonly bool _extending;
    private readonly bool _isSourcePiston;

    private float _progress;
    private float _progressO;

    /// <summary>Creates a moving piston block entity.</summary>
    /// <param name="position">The block the <c>moving_piston</c> state occupies.</param>
    /// <param name="direction">The piston's <c>facing</c>, not the direction of travel.</param>
    /// <param name="extending">True for an extension, false for a retraction.</param>
    /// <param name="isSourcePiston">True when this entity is the piston's own head or base.</param>
    /// <param name="shape">The moved block's collision boxes in local block coordinates.</param>
    /// <param name="shortShape">The same boxes with the short piston-head shape. Only a retracting source piston selects them, and it does so when <c>progress &gt; 0.25F</c>; pass null otherwise.</param>
    /// <exception cref="ArgumentNullException"><paramref name="shape"/> is null.</exception>
    public MovingPiston(
        BlockPos position,
        Direction direction,
        bool extending,
        bool isSourcePiston,
        IReadOnlyList<Aabb> shape,
        IReadOnlyList<Aabb>? shortShape = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        _position = position;
        _direction = direction;
        _extending = extending;
        _isSourcePiston = isSourcePiston;
        _shape = [.. shape];
        _shortShape = shortShape is null ? _shape : [.. shortShape];
    }

    /// <summary>The block the <c>moving_piston</c> state occupies.</summary>
    public BlockPos Position => _position;

    /// <summary>Movement progress: 0, then 0.5, then 1.</summary>
    public float Progress => _progress;

    /// <summary>True once the extension or retraction has completed and the entity has stopped pushing.</summary>
    /// <remarks>A finished moving block can remain available briefly for rendering, but it performs no further pushes. This model removes it at the first tick that observes <c>progressO &gt;= 1</c>.</remarks>
    public bool Removed { get; private set; }

    /// <summary>The direction the moved block travels: the piston facing while extending and its opposite while retracting.</summary>
    public Direction MovementDirection => _extending ? _direction : _direction.Opposite();

    /// <summary>Advances one block-entity tick and pushes <paramref name="target"/> when it stands in the way.</summary>
    public void Tick(IPistonPushTarget? target)
    {
        if (Removed)
            return;

        _progressO = _progress;
        if (_progressO >= 1.0f)
        {
            Removed = true;
            return;
        }

        float next = _progress + ProgressStep;
        MoveCollidedEntities(next, target);
        _progress = next;
        if (_progress >= 1.0f)
            _progress = 1.0f;

    }

    /// <summary>Pushes the single entity whose movement the client owns when it intersects the swept region.</summary>
    private void MoveCollidedEntities(float nextProgress, IPistonPushTarget? target)
    {
        if (target is null)
            return;

        Direction movement = MovementDirection;
        double delta = nextProgress - _progress;
        ReadOnlySpan<Aabb> boxes = CurrentShape;
        if (boxes.Length == 0)
            return;

        // Search the union of the shape's current bounds and the area swept during this tick.
        Aabb shapeBounds = MoveByPositionAndProgress(Bounds(boxes));
        Aabb region = Union(GetMovementArea(shapeBounds, movement, delta), shapeBounds);

        Aabb entityBox = target.BoundingBox;
        if (!entityBox.Intersects(region))
            return;

        double best = 0.0;
        for (int i = 0; i < boxes.Length; i++)
        {
            Aabb area = GetMovementArea(MoveByPositionAndProgress(boxes[i]), movement, delta);
            if (area.Intersects(entityBox))
            {
                best = Math.Max(best, GetMovement(area, movement, entityBox));
                if (best >= delta)
                    break;

            }
        }

        if (best <= 0.0)
            return;

        best = Math.Min(best, delta) + PushOffset;
        MoveEntityByPiston(target, movement, best);
        if (!_extending && _isSourcePiston)
            FixEntityWithinPistonBase(target, movement, delta);

    }

    /// <summary>A retracting source piston collides as a piston head whose short shape becomes active at <c>progress &gt; 0.25F</c>; everything else collides as its own moved state.</summary>
    private ReadOnlySpan<Aabb> CurrentShape =>
        !_extending && _isSourcePiston && _progress > 0.25f ? _shortShape : _shape;

    /// <summary>Maps progress to the signed offset from the block position.</summary>
    private float GetExtendedProgress(float progress) => _extending ? progress - 1.0f : 1.0f - progress;

    /// <summary>Moves a local shape to its position at the current progress.</summary>
    private Aabb MoveByPositionAndProgress(in Aabb box)
    {
        double offset = GetExtendedProgress(_progress);
        return box.Move(
            _position.X + (offset * _direction.StepX()),
            _position.Y + (offset * _direction.StepY()),
            _position.Z + (offset * _direction.StepZ()));
    }

    /// <summary>Shoves an entity that ended up inside a retracting piston's own base back out of it.</summary>
    private void FixEntityWithinPistonBase(IPistonPushTarget target, Direction movement, double delta)
    {
        Aabb entityBox = target.BoundingBox;
        var blockBox = new Aabb(
            _position.X, _position.Y, _position.Z,
            _position.X + 1.0, _position.Y + 1.0, _position.Z + 1.0);
        if (!entityBox.Intersects(blockBox))
            return;

        Direction back = movement.Opposite();
        double whole = GetMovement(blockBox, back, entityBox) + PushOffset;
        double clipped = GetMovement(blockBox, back, Intersect(entityBox, blockBox)) + PushOffset;
        if (Math.Abs(whole - clipped) < PushOffset)
        {
            whole = Math.Min(whole, delta) + PushOffset;
            MoveEntityByPiston(target, back, whole);
        }
    }

    /// <summary>Applies a directional piston displacement.</summary>
    private static void MoveEntityByPiston(IPistonPushTarget target, Direction direction, double amount) =>
        target.MoveByPiston(new Vec3d(
            amount * direction.StepX(),
            amount * direction.StepY(),
            amount * direction.StepZ()));

    /// <summary>Gets the displacement needed to move an entity beyond a swept area.</summary>
    internal static double GetMovement(in Aabb area, Direction direction, in Aabb entityBox) => direction switch
    {
        Direction.East => area.MaxX - entityBox.MinX,
        Direction.West => entityBox.MaxX - area.MinX,
        Direction.Down => entityBox.MaxY - area.MinY,
        Direction.South => area.MaxZ - entityBox.MinZ,
        Direction.North => entityBox.MaxZ - area.MinZ,

        // The compatibility default is the upward axis.
        _ => area.MaxY - entityBox.MinY,
    };

    /// <summary>Returns the slab that the moved face sweeps through during this tick.</summary>
    internal static Aabb GetMovementArea(in Aabb box, Direction direction, double delta)
    {
        double step = delta * AxisStep(direction);
        double lo = Math.Min(step, 0.0);
        double hi = Math.Max(step, 0.0);
        return direction switch
        {
            Direction.West => new Aabb(box.MinX + lo, box.MinY, box.MinZ, box.MinX + hi, box.MaxY, box.MaxZ),
            Direction.East => new Aabb(box.MaxX + lo, box.MinY, box.MinZ, box.MaxX + hi, box.MaxY, box.MaxZ),
            Direction.Down => new Aabb(box.MinX, box.MinY + lo, box.MinZ, box.MaxX, box.MinY + hi, box.MaxZ),
            Direction.North => new Aabb(box.MinX, box.MinY, box.MinZ + lo, box.MaxX, box.MaxY, box.MinZ + hi),
            Direction.South => new Aabb(box.MinX, box.MinY, box.MaxZ + lo, box.MaxX, box.MaxY, box.MaxZ + hi),
            // The compatibility default is the upward axis.
            _ => new Aabb(box.MinX, box.MaxY + lo, box.MinZ, box.MaxX, box.MaxY + hi, box.MaxZ),
        };
    }

    /// <summary>Returns -1 for down, north, or west and +1 otherwise.</summary>
    private static int AxisStep(Direction direction) =>
        direction.StepX() + direction.StepY() + direction.StepZ();

    /// <summary>Returns the bounds of a box list.</summary>
    private static Aabb Bounds(ReadOnlySpan<Aabb> boxes)
    {
        Aabb result = boxes[0];
        for (int i = 1; i < boxes.Length; i++)
            result = Union(result, boxes[i]);

        return result;
    }

    /// <summary>Returns the union of two boxes.</summary>
    private static Aabb Union(in Aabb a, in Aabb b) => new(
        Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Min(a.MinZ, b.MinZ),
        Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY), Math.Max(a.MaxZ, b.MaxZ));

    /// <summary>Returns the intersection of two boxes.</summary>
    private static Aabb Intersect(in Aabb a, in Aabb b) => new(
        Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Max(a.MinZ, b.MinZ),
        Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY), Math.Min(a.MaxZ, b.MaxZ));
}
