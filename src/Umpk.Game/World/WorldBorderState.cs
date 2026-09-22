namespace Umpk.Game.World;

/// <summary>Which world-border containment formula applies. Versions 1.14.4, 1.15.2, 1.17, 1.17.1, 1.19, 1.20.4, and 1.20.6 (protocols 498, 578, 755, 756, 759, 765, 766) all carry the <see cref="Legacy"/> shape; 1.21, 1.21.1, 1.21.2, 1.21.4, 1.21.5, 1.21.6, 1.21.7, 1.21.8, 1.21.9, 1.21.11, 26.1, 26.2, 26.3 (protocols 767-777) all carry the <see cref="Modern"/> shape. The boundary is 1.21/protocol 767. The two formulas agree whenever the border's own bounds (<c>centerX/Z +/- size/2</c>) are integers and disagree ONLY on the min-X/min-Z edge otherwise - see <see cref="WorldBorderState.IsWithinBounds"/>'s remarks for the concrete case.</summary>
public enum WorldBorderContainmentEra
{
    /// <summary>1.14.4 through 1.20.6 (protocol &lt;= 766): <c>(x + 1) &gt; minX &amp;&amp; x &lt; maxX</c> (and the same shape on Z). The strict <c>&gt;</c> with a <c>+1</c> offset on the min edge accepts ONE MORE block column there than <see cref="Modern"/> whenever <c>minX</c>/<c>minZ</c> is not an integer.</summary>
    Legacy = 0,

    /// <summary>1.21 onward (protocol &gt;= 767), confirmed unchanged through 26.2: <c>x &gt;= minX &amp;&amp; x &lt; maxX</c> (and the same shape on Z).</summary>
    Modern = 1,
}

/// <summary>The world border state. It carries the center, the current diameter, an optional interpolation toward a target diameter over a duration, and the warning thresholds. This is an immutable snapshot; the session loop replaces the <see cref="World.Border"/> reference wholesale when a border packet arrives.</summary>
public sealed class WorldBorderState
{
    /// <summary>The default border: centered at origin, vanilla maximum diameter, no lerp, default warnings.</summary>
    public static readonly WorldBorderState Default = new(0.0, 0.0, 59_999_968.0, 59_999_968.0, 0L, 5, 15.0);

    /// <summary><see cref="WorldBorderContainmentEra"/> for a protocol version, mirroring the <c>PhysicsProfile.ForProtocol</c> convention: a plain protocol-number switch, computed once by the caller that knows the session's protocol (there is no dataset table for this - the boundary is a single clean cut, not a per-block or per-version-feature fact).</summary>
    public static WorldBorderContainmentEra ContainmentEraForProtocol(int protocol) =>
        protocol >= 767 ? WorldBorderContainmentEra.Modern : WorldBorderContainmentEra.Legacy;

    /// <summary>Creates a border state.</summary>
    /// <param name="centerX">Border center X (world coordinates).</param>
    /// <param name="centerZ">Border center Z (world coordinates).</param>
    /// <param name="size">Current diameter in blocks.</param>
    /// <param name="targetSize">Diameter the border is lerping toward (equal to <paramref name="size"/> when static).</param>
    /// <param name="lerpTimeMillis">Milliseconds the lerp from <paramref name="size"/> to <paramref name="targetSize"/> takes (0 when static).</param>
    /// <param name="warningBlocks">Distance from the border, in blocks, at which the warning overlay appears.</param>
    /// <param name="warningTimeSeconds">Seconds of approaching border movement at which the warning appears.</param>
    public WorldBorderState(double centerX, double centerZ, double size, double targetSize, long lerpTimeMillis, int warningBlocks, double warningTimeSeconds)
    {
        CenterX = centerX;
        CenterZ = centerZ;
        Size = size;
        TargetSize = targetSize;
        LerpTimeMillis = lerpTimeMillis;
        WarningBlocks = warningBlocks;
        WarningTimeSeconds = warningTimeSeconds;
    }

    /// <summary>Border center X in world coordinates.</summary>
    public double CenterX { get; }

    /// <summary>Border center Z in world coordinates.</summary>
    public double CenterZ { get; }

    /// <summary>Current border diameter in blocks.</summary>
    public double Size { get; }

    /// <summary>Diameter the border is lerping toward; equal to <see cref="Size"/> when static.</summary>
    public double TargetSize { get; }

    /// <summary>Milliseconds the current lerp takes; 0 when the border is static.</summary>
    public long LerpTimeMillis { get; }

    /// <summary>True when the border is animating toward a different <see cref="TargetSize"/>.</summary>
    public bool IsLerping => LerpTimeMillis > 0 && TargetSize != Size;

    /// <summary>Distance from the border, in blocks, at which the warning overlay appears.</summary>
    public int WarningBlocks { get; }

    /// <summary>Seconds of approaching border movement at which the warning appears.</summary>
    public double WarningTimeSeconds { get; }

    /// <summary>Tests containment using the formula selected by <paramref name="era"/>. Both eras evaluate at partial tick 0, which is <see cref="Size"/> itself rather than an interpolated value, so a border mid-lerp is read at its CURRENT diameter and not the one it is animating toward.</summary>
    /// <remarks>
    /// <para>The two formulas (X shown; Z is the same shape): <see cref="WorldBorderContainmentEra.Legacy"/> is <c>(x + 1) &gt; minX &amp;&amp; x &lt; maxX</c>. <see cref="WorldBorderContainmentEra.Modern"/> is <c>x &gt;= minX &amp;&amp; x &lt; maxX</c>. They agree whenever <c>minX</c>/<c>minZ</c> (<c>= centerX/Z - size/2</c>) is an integer. They diverge on the min edge whenever it is NOT: for example <c>size=101, center=0</c> gives <c>minX=-50.5</c>, and <c>x=-51</c> is IN bounds under Legacy (<c>(-51+1) &gt; -50.5</c>) but OUT under Modern (<c>-51 &gt;= -50.5</c> is false). The max edge (<c>x &lt; maxX</c>) is textually identical in both eras, so the divergence never touches it, and it never makes Legacy MORE restrictive than Modern - only less, on the min edge, for a non-integer border.</para>
    /// <para>Vanilla also clamps each edge to <c>+/-absoluteMaxSize</c> (29,999,984 by default), which this state has no field for. That clamp only differs from the box above when a border is configured LARGER than vanilla's own maximum; <see cref="Default"/>'s 59,999,968-block box already sits exactly on the clamp, so every border this type can represent is within it regardless.</para>
    /// </remarks>
    public bool IsWithinBounds(int x, int z, WorldBorderContainmentEra era)
    {
        double half = Size / 2.0;
        double minX = CenterX - half;
        double maxX = CenterX + half;
        double minZ = CenterZ - half;
        double maxZ = CenterZ + half;
        return era == WorldBorderContainmentEra.Legacy
            ? (x + 1) > minX && x < maxX && (z + 1) > minZ && z < maxZ
            : x >= minX && x < maxX && z >= minZ && z < maxZ;
    }
}
