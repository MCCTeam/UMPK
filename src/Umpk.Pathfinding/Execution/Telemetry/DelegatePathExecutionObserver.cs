using System.Globalization;
using Umpk.Geometry;

namespace Umpk.Pathfinding.Execution.Telemetry;

/// <summary>A per-instance <see cref="IPathExecutionObserver"/> that formats each event as an English line and hands it to a caller-supplied sink. The sink is typically wired to the host's <c>ILogger</c> at the integration layer; keeping the observer package free of a logging dependency avoids a new package reference while still routing telemetry off the hot path. No process-global state: the sink is owned by the navigation that created the observer.</summary>
public sealed class DelegatePathExecutionObserver : IPathExecutionObserver
{
    private readonly Action<string> _sink;

    /// <summary>Creates an observer that writes formatted event lines to <paramref name="sink"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> is null.</exception>
    public DelegatePathExecutionObserver(Action<string> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <inheritdoc/>
    public void OnNavigationStarted(IReadOnlyList<PathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        _sink($"navigation started: {segments.Count} segments");
    }

    /// <inheritdoc/>
    public void OnSegmentStarted(int segmentIndex, int totalSegments, PathSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        _sink($"segment {segmentIndex}/{totalSegments} started: {segment.MoveType}");
    }

    /// <inheritdoc/>
    public void OnSegmentCompleted(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
    {
        ArgumentNullException.ThrowIfNull(segment);
        _sink($"segment {segmentIndex}/{totalSegments} complete ({segment.MoveType}) in {elapsedTicks}t at {Format(position)}");
    }

    /// <inheritdoc/>
    public void OnSegmentFailed(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
    {
        ArgumentNullException.ThrowIfNull(segment);
        _sink($"segment {segmentIndex}/{totalSegments} FAILED ({segment.MoveType}) after {elapsedTicks}t at {Format(position)}");
    }

    /// <inheritdoc/>
    public void OnDeviationDetected(int segmentIndex, Vec3d expected, Vec3d actual, double distance)
        => _sink($"deviation on segment {segmentIndex}: expected {Format(expected)} actual {Format(actual)} dist {distance.ToString("F3", CultureInfo.InvariantCulture)}");

    /// <inheritdoc/>
    public void OnNavigationCompleted(int totalTicks) => _sink($"navigation complete in {totalTicks}t");

    private static string Format(Vec3d p) => string.Create(
        CultureInfo.InvariantCulture, $"({p.X:F2},{p.Y:F2},{p.Z:F2})");
}
