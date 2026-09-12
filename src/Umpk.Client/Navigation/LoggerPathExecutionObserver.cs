using Microsoft.Extensions.Logging;
using Umpk.Geometry;
using Umpk.Pathfinding.Execution;
using Umpk.Pathfinding.Execution.Telemetry;

namespace Umpk.Client.Navigation;

/// <summary>A per-navigation <see cref="IPathExecutionObserver"/> that emits structured <see cref="ILogger"/> events. This routes navigation telemetry through the host logging pipeline every other module uses, instead of the dependency-free <c>DelegatePathExecutionObserver</c> string sink (which stays available for hosts that do not want a logging dependency). No process-global state: the logger is owned by the navigation that created the observer.</summary>
internal sealed class LoggerPathExecutionObserver(ILogger logger) : IPathExecutionObserver
{
    private readonly ILogger _logger = logger;

    public void OnNavigationStarted(IReadOnlyList<PathSegment> segments)
        => _logger.LogDebug("Navigation started over {SegmentCount} segments.", segments.Count);

    public void OnSegmentStarted(int segmentIndex, int totalSegments, PathSegment segment)
        => _logger.LogTrace("Segment {Index}/{Total} started: {MoveType}.", segmentIndex, totalSegments, segment.MoveType);

    public void OnSegmentCompleted(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
        => _logger.LogTrace(
            "Segment {Index}/{Total} completed ({MoveType}) in {ElapsedTicks} ticks at {Position}.",
            segmentIndex, totalSegments, segment.MoveType, elapsedTicks, position);

    public void OnSegmentFailed(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position)
        => _logger.LogWarning(
            "Segment {Index}/{Total} failed ({MoveType}) after {ElapsedTicks} ticks at {Position}.",
            segmentIndex, totalSegments, segment.MoveType, elapsedTicks, position);

    public void OnDeviationDetected(int segmentIndex, Vec3d expected, Vec3d actual, double distance)
        => _logger.LogDebug(
            "Deviation on segment {Index}: expected {Expected} actual {Actual} distance {Distance:F3}.",
            segmentIndex, expected, actual, distance);

    public void OnNavigationCompleted(int totalTicks)
        => _logger.LogDebug("Navigation completed in {TotalTicks} ticks.", totalTicks);
}
