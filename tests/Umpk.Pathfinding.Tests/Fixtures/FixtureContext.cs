using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Pathfinding.Core;

namespace Umpk.Pathfinding.Tests.Fixtures;

/// <summary>Fixture data for <c>FixtureContext</c>.</summary>
internal static class FixtureContext
{
    /// <summary>Captures a context spanning the region around two positions with the given options.</summary>
    internal static CalculationContext Build(
        FixtureWorld world, BlockPos a, BlockPos b, PathfinderOptions? options = null, int margin = 8,
        PathfinderCapabilities? capabilities = null)
    {
        PlanningWorldView view = world.Capture(a, b, margin);
        return new CalculationContext(view, options ?? PathfinderOptions.Default, capabilities: capabilities);
    }

    /// <summary>Captures a context spanning a single position's neighborhood.</summary>
    internal static CalculationContext Around(
        FixtureWorld world, int x, int y, int z, PathfinderOptions? options = null, int margin = 8,
        PathfinderCapabilities? capabilities = null)
        => Build(world, new BlockPos(x, y, z), new BlockPos(x, y, z), options, margin, capabilities);
}
