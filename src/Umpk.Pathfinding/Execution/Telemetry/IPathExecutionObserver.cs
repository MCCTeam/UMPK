using Umpk.Geometry;

namespace Umpk.Pathfinding.Execution.Telemetry;

/// <summary>The per-navigation telemetry seam. A per-instance observer receives segment transitions and replans without sharing diagnostic state between sessions.</summary>
public interface IPathExecutionObserver
{
    /// <summary>The navigation started over the given segment list.</summary>
    void OnNavigationStarted(IReadOnlyList<PathSegment> segments);

    /// <summary>A segment started executing.</summary>
    void OnSegmentStarted(int segmentIndex, int totalSegments, PathSegment segment);

    /// <summary>A segment completed.</summary>
    void OnSegmentCompleted(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position);

    /// <summary>A segment failed (the driver will request a replan or abort).</summary>
    void OnSegmentFailed(int segmentIndex, int totalSegments, PathSegment segment, int elapsedTicks, Vec3d position);

    /// <summary>The executor detected the real state deviating from the expected state past the threshold.</summary>
    void OnDeviationDetected(int segmentIndex, Vec3d expected, Vec3d actual, double distance);

    /// <summary>The navigation completed successfully.</summary>
    void OnNavigationCompleted(int totalTicks);
}
