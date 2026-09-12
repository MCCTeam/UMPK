namespace Umpk.Geometry;

/// <summary>The six block-face directions. Wire codecs rely on the ordinals: Down=0, Up=1, North=2, South=3, West=4, and East=5.</summary>
public enum Direction
{
    Down = 0,
    Up = 1,
    North = 2,
    South = 3,
    West = 4,
    East = 5,
}

/// <summary>The three coordinate axes.</summary>
public enum Axis
{
    X = 0,
    Y = 1,
    Z = 2,
}

/// <summary>Step vectors, opposites, and axis mapping for <see cref="Direction"/>.</summary>
public static class DirectionExtensions
{
    public static int StepX(this Direction direction) => direction switch
    {
        Direction.West => -1,
        Direction.East => 1,
        _ => 0,
    };

    public static int StepY(this Direction direction) => direction switch
    {
        Direction.Down => -1,
        Direction.Up => 1,
        _ => 0,
    };

    public static int StepZ(this Direction direction) => direction switch
    {
        Direction.North => -1,
        Direction.South => 1,
        _ => 0,
    };

    public static Direction Opposite(this Direction direction) => direction switch
    {
        Direction.Down => Direction.Up,
        Direction.Up => Direction.Down,
        Direction.North => Direction.South,
        Direction.South => Direction.North,
        Direction.West => Direction.East,
        Direction.East => Direction.West,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public static Axis GetAxis(this Direction direction) => direction switch
    {
        Direction.Down or Direction.Up => Axis.Y,
        Direction.North or Direction.South => Axis.Z,
        Direction.West or Direction.East => Axis.X,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
