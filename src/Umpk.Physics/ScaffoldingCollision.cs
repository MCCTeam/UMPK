using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>The one block in the game whose collision shape depends on the body asking for it.</summary>
/// <remarks>
/// <para>A body above the cell and not descending meets the stable shape. Otherwise, an unstable bottom cell can present its 2/16 bottom slab when the body is above the cell floor. The below-block shape is a unit cube moved down one, so its maximum Y is 0 and the second from-above check asks only whether the feet are at or above the cell's own floor.</para>
/// <para><b>What that means for a body.</b> Feet at or above the cell's top face, not sneaking: the body meets <c>SHAPE_STABLE</c> and stands on its 14/16-to-16/16 top plate. Feet anywhere below that, or sneaking on top of it: the body meets <b>nothing</b>. That single rule is how a player climbs a scaffold tower, how shift-descending through one works, and why a scaffold is walkable terrain from above and open corridor from inside.</para>
/// <para>A block-shape table has no entity context, so it records the from-above answer (<c>SHAPE_STABLE</c>, shape 309 on protocol 774) for every one of scaffolding's 32 states. Applying it to a body inside the column would incorrectly retain the top plate. At 0.875 high, that plate requires a full-block step and prevents lateral entry even though the corner posts do not intersect the centered body.</para>
/// <para><b>Named, not generalised.</b> The rule is keyed on the registry identifier. It cannot be inferred from "climbable with a collision shape": a ladder is that too, and its 3/16 wall panel is real from every direction - a shaft built without it is 3/16 wider than the real one.</para>
/// </remarks>
internal static class ScaffoldingCollision
{
    /// <summary>The from-above tolerance, <c>1.0E-5F</c> widened to a double.</summary>
    private const double IsAboveEpsilon = 1.0E-5;

    /// <summary>The unstable-bottom shape: a full-width slab from 0 to 2/16.</summary>
    private static readonly Aabb[] UnstableBottom = [new(0.0, 0.0, 0.0, 1.0, 0.125, 1.0)];

    private static readonly Aabb[] Empty = [];

    private const string BottomProperty = "bottom";

    private const string DistanceProperty = "distance";

    private const string TrueValue = "true";

    /// <summary>The collision boxes this cell presents to a body whose feet are at <paramref name="entityBottom"/>. Identical to <paramref name="tabled"/> for every block but scaffolding.</summary>
    /// <param name="state">The state at the cell.</param>
    /// <param name="tabled">The shape table's from-above answer.</param>
    /// <param name="cellY">The cell's Y.</param>
    /// <param name="entityBottom">The bottom of the body's box.</param>
    /// <param name="descending">Whether the body is descending, which for a player is the sneak key.</param>
    /// <returns>The boxes to collide against.</returns>
    public static ReadOnlySpan<Aabb> ShapesFor(
        BlockState state, ReadOnlySpan<Aabb> tabled, int cellY, double entityBottom, bool descending)
    {
        // BlockState.IsScaffolding is flag-first and default-safe: the climbable bit is a cheap flag read that rejects every non-scaffolding block before the string compare, and scaffolding is climbable on every era that has it (it is in vanilla's #minecraft:climbable from 1.14 onward). Sharing it with PlayerPhysics.HandleOnClimbable is the point: the shape rule and the climb-clamp exemption are two halves of one vanilla feature and must not be able to disagree about which block they are talking about.
        if (tabled.Length == 0 || !state.IsScaffolding)
            return tabled;

        if (entityBottom > cellY + 1.0 - IsAboveEpsilon && !descending)
            return tabled;

        // The unstable-bottom plate: the underside of a scaffold that hangs off its neighbours rather than standing on something. A body at or above the cell's own floor rests on it, which is what stops a climber dropping out of the bottom of a hanging tower. The plate's geometry is spelled out here because the shape table cannot carry it: it records one shape for all 32 states. Pre-flattening sources answer false for every property, but no era below 1.14 has scaffolding at all, so no reachable state loses the plate to an unreadable property.
        if (entityBottom > cellY - IsAboveEpsilon
            && state.TryGetProperty(BottomProperty, out string bottom)
            && bottom == TrueValue
            && state.TryGetProperty(DistanceProperty, out string distance)
            && distance != "0")
            return UnstableBottom;

        return Empty;
    }
}
