using Umpk.Game.Blocks;
using Umpk.Geometry;

namespace Umpk.Physics;

/// <summary>The other block whose collision shape depends on the body asking, and the only one where the deciding fact is what that body is WEARING.</summary>
/// <remarks>
/// <para>A fall distance above 2.5 presents a 0.9-tall box. Otherwise, a player wearing leather boots meets a full cube only when its feet are above the cell and it is not descending. All other cases present no shape.</para>
/// <para>The rule is stable across supported protocols. Powder snow simply does not exist before 1.17.</para>
/// <para><b>What that means for a body.</b> Feet strictly above the cell's top face, leather boots on, not sneaking: a full unit cube, and the body walks the snow like stone. Sneaking on top of it, or standing inside it, or barefoot: <b>nothing</b>, and the body sinks. Those two are one feature - sneak is how a player gets DOWN into powder snow, exactly as it is how one gets down a scaffold.</para>
/// <para><b>The fall arm is not the boots arm and comes before it.</b> Any entity whose fall distance exceeds 2.5 blocks meets the 0.9-tall box instead, booted or not. It reads like a landing and it is the opposite: the body rests at 0.9, landing clears the fall distance, and on the next tick the from-above check compares <c>0.9 &gt; 0.99999</c>, gets false, and the body sinks the rest of the way. Falling THROUGH is how powder snow breaks a long fall, and modelling the boots arm without this one would have a booted body land on top of a snow lane it should have dropped into, which the server would then correct.</para>
/// <para><b>Why this is its own file rather than an arm of <see cref="ScaffoldingCollision"/>.</b> That helper only ever SUBTRACTS from the shape table: scaffolding's tabled shape is vanilla's from-above answer, so its first line is <c>if (tabled.Length == 0 ...) return tabled;</c>. Powder snow's tabled shape is empty on every protocol because an entity-free query returns no shape, and <c>BlockAttributeResolver</c> therefore derives neither <c>BlocksMotion</c> nor <c>Solid</c> for it - so this rule has to ADD a cube the table does not contain. Bolting that onto a subtract-only helper would make its own guard a lie.</para>
/// </remarks>
internal static class PowderSnowCollision
{
    /// <summary>The above-shape tolerance, <c>1.0E-5F</c> widened to a double.</summary>
    private const double IsAboveEpsilon = 1.0E-5;

    /// <summary>Fall distance above which the body meets the 0.9-tall falling shape.</summary>
    private const double NumBlocksToFallIntoBlock = 2.5;

    /// <summary>The ordinary unit-cube collision shape.</summary>
    private static readonly Aabb[] FullCube = [new(0.0, 0.0, 0.0, 1.0, 1.0, 1.0)];

    /// <summary>The 0.9-tall shape presented to a body falling more than 2.5 blocks.</summary>
    private static readonly Aabb[] Falling = [new(0.0, 0.0, 0.0, 1.0, 0.9f, 1.0)];

    /// <summary>The collision boxes this cell presents to a body whose feet are at <paramref name="entityBottom"/>. Identical to <paramref name="tabled"/> for every block but powder snow.</summary>
    /// <param name="state">The state at the cell.</param>
    /// <param name="tabled">The shape table's answer, i.e. vanilla's entity-free shape.</param>
    /// <param name="cellY">The cell's Y.</param>
    /// <param name="entityBottom">The bottom of the body's box.</param>
    /// <param name="body">The per-body collision context. <c>scoped</c> because the returned span is always a static field or <paramref name="tabled"/> and never borrows from it, which ref safety cannot infer on its own.</param>
    /// <returns>The boxes to collide against.</returns>
    public static ReadOnlySpan<Aabb> ShapesFor(
        BlockState state,
        ReadOnlySpan<Aabb> tabled,
        int cellY,
        double entityBottom,
        scoped in BodyCollisionContext body)
    {
        // The fall arm comes first and asks nothing about the body's feet: a body past 2.5 blocks meets the 0.9 box even with boots on. Each predicate appears exactly once so tests can isolate its effect.
        if (body.FallDistance > NumBlocksToFallIntoBlock)
            return state.IsPowderSnow ? Falling : tabled;

        // Nothing below can fire for a body that cannot walk on powder snow, and that is every body in almost every session, so this one bool is what keeps the registry-path compare off the per-cell collision path entirely.
        if (!body.PowderSnowWalkable || !state.IsPowderSnow)
            return tabled;

        return entityBottom > cellY + 1.0 - IsAboveEpsilon && !body.Descending ? FullCube : tabled;
    }
}
