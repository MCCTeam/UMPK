using Umpk.Geometry;

namespace Umpk.Game.World;

/// <summary>Which world-border containment formula applies. Versions 1.14.4, 1.15.2, 1.17, 1.17.1, 1.19, 1.20.4, and 1.20.6 (protocols 498, 578, 755, 756, 759, 765, 766) all carry the <see cref="Legacy"/> shape; 1.21, 1.21.1, 1.21.2, 1.21.4, 1.21.5, 1.21.6, 1.21.7, 1.21.8, 1.21.9, 1.21.11, 26.1, 26.2, 26.3 (protocols 767-777) all carry the <see cref="Modern"/> shape. The boundary is 1.21/protocol 767. The two formulas agree whenever the border's own bounds (<c>centerX/Z +/- size/2</c>) are integers and disagree ONLY on the min-X/min-Z edge otherwise - see <see cref="WorldBorderState.IsWithinBounds"/>'s remarks for the concrete case.</summary>
public enum WorldBorderContainmentEra
{
    /// <summary>1.14.4 through 1.20.6 (protocol &lt;= 766): <c>(x + 1) &gt; minX &amp;&amp; x &lt; maxX</c> (and the same shape on Z). The strict <c>&gt;</c> with a <c>+1</c> offset on the min edge accepts ONE MORE block column there than <see cref="Modern"/> whenever <c>minX</c>/<c>minZ</c> is not an integer.</summary>
    Legacy = 0,

    /// <summary>1.21 onward (protocol &gt;= 767), confirmed unchanged through 26.2: <c>x &gt;= minX &amp;&amp; x &lt; maxX</c> (and the same shape on Z).</summary>
    Modern = 1,
}

/// <summary>The clock used by a world-border interpolation.</summary>
public enum WorldBorderInterpolationClock
{
    /// <summary>Elapsed monotonic milliseconds, through 1.21.10.</summary>
    Milliseconds = 0,

    /// <summary>Eligible world ticks, from 1.21.11 onward.</summary>
    WorldTicks = 1,
}

/// <summary>Immutable border settings and interpolation origin. Millisecond borders sample their clock when read; world-tick borders advance only through <see cref="AdvanceTick"/>.</summary>
public sealed class WorldBorderState
{
    private readonly Interpolation _interpolation;

    /// <summary>The default border, centered at the origin with vanilla bounds and warnings.</summary>
    public static readonly WorldBorderState Default = new(0.0, 0.0, 59_999_968.0, 59_999_968.0, 0L, 5, 15.0);

    /// <summary>Selects the native block-position containment formula.</summary>
    public static WorldBorderContainmentEra ContainmentEraForProtocol(int protocol) =>
        protocol >= 767 ? WorldBorderContainmentEra.Modern : WorldBorderContainmentEra.Legacy;

    /// <summary>Selects the native border clock. WorldBorder.lerpSizeBetween changes from milliseconds to world ticks at 1.21.11 (protocol 774).</summary>
    public static WorldBorderInterpolationClock InterpolationClockForProtocol(int protocol) =>
        protocol >= 774 ? WorldBorderInterpolationClock.WorldTicks : WorldBorderInterpolationClock.Milliseconds;

    /// <summary>Creates a border using the system monotonic clock and a duration in milliseconds.</summary>
    public WorldBorderState(double centerX, double centerZ, double size, double targetSize, long lerpTimeMillis, int warningBlocks, double warningTimeSeconds)
        : this(centerX, centerZ, size, targetSize, lerpTimeMillis, warningBlocks, warningTimeSeconds, 29_999_984, WorldBorderInterpolationClock.Milliseconds, TimeProvider.System)
    {
    }

    /// <summary>Creates a border using an explicit clock, native wire-duration unit, and absolute coordinate limit. A size setter uses equal start and target sizes; a lerp setter may carry a nonpositive duration.</summary>
    public WorldBorderState(double centerX, double centerZ, double size, double targetSize, long lerpDuration, int warningBlocks, double warningTimeSeconds, int absoluteMaxSize, WorldBorderInterpolationClock interpolationClock, TimeProvider timeProvider)
        : this(centerX, centerZ, warningBlocks, warningTimeSeconds, absoluteMaxSize, Interpolation.Create(size, targetSize, lerpDuration, interpolationClock, timeProvider))
    {
    }

    private WorldBorderState(double centerX, double centerZ, int warningBlocks, double warningTimeSeconds, int absoluteMaxSize, Interpolation interpolation)
    {
        CenterX = centerX;
        CenterZ = centerZ;
        WarningBlocks = warningBlocks;
        WarningTimeSeconds = warningTimeSeconds;
        AbsoluteMaxSize = absoluteMaxSize;
        _interpolation = interpolation;
    }

    /// <summary>Border center X in world coordinates.</summary>
    public double CenterX { get; }

    /// <summary>Border center Z in world coordinates.</summary>
    public double CenterZ { get; }

    /// <summary>The coordinate limit applied independently to every border edge.</summary>
    public int AbsoluteMaxSize { get; }

    /// <summary>The clock and wire-duration unit used by this interpolation.</summary>
    public WorldBorderInterpolationClock InterpolationClock => _interpolation.Clock;

    /// <summary>Current native diameter. A moving world-tick border uses its previous diameter for bounds at partial tick zero.</summary>
    public double Size => _interpolation.Size;

    /// <summary>Diameter the border is moving toward.</summary>
    public double TargetSize => _interpolation.To;

    /// <summary>The original wire duration. This legacy property name denotes milliseconds through 1.21.10 and world ticks from 1.21.11; inspect <see cref="InterpolationClock"/> for the unit.</summary>
    public long LerpTimeMillis => _interpolation.Duration;

    /// <summary>Remaining duration in the unit selected by <see cref="InterpolationClock"/>. A completed millisecond extent may remain negative until its next eligible world tick.</summary>
    public long LerpRemainingTime => _interpolation.Remaining;

    /// <summary>Whether an unfinished interpolation is active.</summary>
    public bool IsLerping => _interpolation.Moving && LerpRemainingTime > 0;

    /// <summary>Distance from the border at which the warning appears.</summary>
    public int WarningBlocks { get; }

    /// <summary>Seconds of approaching movement at which the warning appears.</summary>
    public double WarningTimeSeconds { get; }

    /// <summary>The clamped minimum X edge at partial tick zero.</summary>
    public double MinX => ClampEdge(CenterX - BoundsSize / 2.0);

    /// <summary>The clamped maximum X edge at partial tick zero.</summary>
    public double MaxX => ClampEdge(CenterX + BoundsSize / 2.0);

    /// <summary>The clamped minimum Z edge at partial tick zero.</summary>
    public double MinZ => ClampEdge(CenterZ - BoundsSize / 2.0);

    /// <summary>The clamped maximum Z edge at partial tick zero.</summary>
    public double MaxZ => ClampEdge(CenterZ + BoundsSize / 2.0);

    private double BoundsSize => _interpolation.Moving && InterpolationClock == WorldBorderInterpolationClock.WorldTicks ? _interpolation.PreviousSize : Size;

    /// <summary>Moves the center without restarting interpolation.</summary>
    public WorldBorderState WithCenter(double centerX, double centerZ) => new(centerX, centerZ, WarningBlocks, WarningTimeSeconds, AbsoluteMaxSize, _interpolation);

    /// <summary>Changes the warnings without restarting interpolation.</summary>
    public WorldBorderState WithWarnings(int warningBlocks, double warningTimeSeconds) => new(CenterX, CenterZ, warningBlocks, warningTimeSeconds, AbsoluteMaxSize, _interpolation);

    /// <summary>Changes the coordinate limit without restarting interpolation.</summary>
    public WorldBorderState WithAbsoluteMaxSize(int absoluteMaxSize) => new(CenterX, CenterZ, WarningBlocks, WarningTimeSeconds, absoluteMaxSize, _interpolation);

    /// <summary>Sets an immediate diameter and stops interpolation.</summary>
    public WorldBorderState WithSize(double size) => WithLerp(size, size, 0);

    /// <summary>Starts a native interpolation using this border's clock and wire-duration unit.</summary>
    public WorldBorderState WithLerp(double oldSize, double newSize, long duration) => new(CenterX, CenterZ, WarningBlocks, WarningTimeSeconds, AbsoluteMaxSize, Interpolation.Create(oldSize, newSize, duration, InterpolationClock, _interpolation.Time));

    /// <summary>Advances one eligible world tick. The session owner must skip this call while the world is frozen without a granted step.</summary>
    public WorldBorderState AdvanceTick()
    {
        if (!_interpolation.Moving)
            return this;

        if (InterpolationClock == WorldBorderInterpolationClock.Milliseconds)
            return LerpRemainingTime <= 0 ? WithSize(TargetSize) : this;

        long remaining = _interpolation.RemainingTicks - 1;
        if (remaining <= 0)
            return WithSize(TargetSize);

        Interpolation next = _interpolation with
        {
            RemainingTicks = remaining,
            PreviousSize = Size,
            CurrentSize = Interpolation.Calculate(_interpolation.From, TargetSize, (double)LerpTimeMillis, remaining),
        };
        return new(CenterX, CenterZ, WarningBlocks, WarningTimeSeconds, AbsoluteMaxSize, next);
    }

    /// <summary>Tests native block-position containment against the clamped edges at partial tick zero.</summary>
    /// <remarks>Legacy containment accepts a block whose positive face is beyond the minimum edge; modern containment compares the block coordinate itself. Both exclude the maximum edge.</remarks>
    public bool IsWithinBounds(int x, int z, WorldBorderContainmentEra era) => era == WorldBorderContainmentEra.Legacy
        ? ((double)x + 1) > MinX && x < MaxX && ((double)z + 1) > MinZ && z < MaxZ
        : x >= MinX && x < MaxX && z >= MinZ && z < MaxZ;

    /// <summary>Tests the border's native voxel collision against a proposed box. A missing source has no border collision; proximity uses the source's current position and the proposed box's horizontal size.</summary>
    public bool CollidesWith(in Aabb box, Vec3d? sourcePosition, WorldBorderContainmentEra era)
    {
        if (sourcePosition is not { } source)
            return false;

        double maximumSize = Math.Max(Math.Max(Math.Abs(box.XSize), Math.Abs(box.ZSize)), 1.0);
        // Preserve the native distance getter's order, including separate clock samples for a moving millisecond border.
        double negativeZ = source.Z - MinZ;
        double positiveZ = MaxZ - source.Z;
        double negativeX = source.X - MinX;
        double positiveX = MaxX - source.X;
        double distance = Math.Min(Math.Min(Math.Min(negativeX, positiveX), negativeZ), positiveZ);
        if (!(distance < maximumSize * 2.0))
            return false;

        // 1.20.6-client-decompiled net/minecraft/world/level/border/WorldBorder.java uses strict minimum edges; 1.21-client-decompiled net/minecraft/world/level/border/WorldBorder.java makes them inclusive.
        bool close = era == WorldBorderContainmentEra.Legacy
            ? source.X > MinX - maximumSize && source.X < MaxX + maximumSize && source.Z > MinZ - maximumSize && source.Z < MaxZ + maximumSize
            : source.X >= MinX - maximumSize && source.X < MaxX + maximumSize && source.Z >= MinZ - maximumSize && source.Z < MaxZ + maximumSize;
        if (!close)
            return false;

        double minX = Math.Floor(MinX);
        double minZ = Math.Floor(MinZ);
        double maxX = Math.Ceiling(MaxX);
        double maxZ = Math.Ceiling(MaxZ);
        if (minX > maxX || minZ > maxZ)
            throw new InvalidOperationException("The border minimum exceeds its maximum collision edge.");

        const double epsilon = 1.0E-7;
        if (box.XSize < epsilon || box.YSize < epsilon || box.ZSize < epsilon)
            return false;

        // Native shapes subtract the rounded interior from infinite space. IndirectMerger subtracts epsilon from the next coordinate; changing this to a difference comparison loses boundary bits.
        return minX == maxX || minZ == maxZ
            || box.MinX < minX - epsilon || maxX < box.MaxX - epsilon
            || box.MinZ < minZ - epsilon || maxZ < box.MaxZ - epsilon;
    }

    private double ClampEdge(double value)
    {
        double limit = AbsoluteMaxSize;
        return value < -limit ? -limit : Math.Min(value, limit);
    }

    private readonly record struct Interpolation(TimeProvider Time, WorldBorderInterpolationClock Clock, double From, double To, long Duration, long BeginMillis, long RemainingTicks, double CurrentSize, double PreviousSize, bool Moving)
    {
        internal double Size
        {
            get
            {
                if (!Moving || Clock == WorldBorderInterpolationClock.WorldTicks)
                    return CurrentSize;

                double fraction = unchecked(Millis(Time) - BeginMillis) / (double)Duration;
                return fraction < 1.0 ? From + fraction * (To - From) : To;
            }
        }

        internal long Remaining => !Moving ? 0 : Clock == WorldBorderInterpolationClock.WorldTicks ? RemainingTicks : unchecked(BeginMillis + Duration - Millis(Time));

        internal static Interpolation Create(double from, double to, long duration, WorldBorderInterpolationClock clock, TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(time);
            bool moving = from != to;
            double size = moving ? Calculate(from, to, duration, duration) : to;
            return new(time, clock, from, to, moving ? duration : 0, Millis(time), duration, size, size, moving);
        }

        internal static double Calculate(double from, double to, double duration, long remaining)
        {
            double fraction = (duration - remaining) / duration;
            return fraction < 1.0 ? from + fraction * (to - from) : to;
        }

        // Util.getMillis truncates each absolute clock reading before subtracting. Truncating only elapsed time loses the millisecond crossed by a submillisecond wait.
        private static long Millis(TimeProvider time) => (long)((Int128)time.GetTimestamp() * 1000 / time.TimestampFrequency);
    }
}
