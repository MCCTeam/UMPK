namespace Umpk.Physics;

/// <summary>The per-body facts read by context-dependent collision shapes.</summary>
/// <remarks>
/// <para>Scaffolding reads whether the body is above or descending. Powder snow also reads fall distance and whether the body may walk on it. Placement and fluid-walker facts are outside this engine, so this record carries the three per-body facts and the collision scan keeps passing the entity bottom separately - it comes off the box being tested, which is not always the body's own.</para>
/// <para><see cref="Neutral"/> is the answer for a body that is standing still on the ground with nothing on its feet, and it is what every construction site that does not care gets, so adding facts here can never silently change an existing collision.</para>
/// </remarks>
/// <param name="Descending">Whether the body is descending, which for a player is the sneak key.</param>
/// <param name="FallDistance">The body's accumulated fall distance. Read by powder snow alone, which hands any body past 2.5 blocks a 0.9-tall box instead of the shape it would otherwise get.</param>
/// <param name="PowderSnowWalkable">Whether the body can walk on powder snow, resolved by the host. See <see cref="PhysicsConditions.PowderSnowWalkable"/>.</param>
internal readonly record struct BodyCollisionContext(
    bool Descending,
    double FallDistance,
    bool PowderSnowWalkable)
{
    /// <summary>A grounded, bare-footed body: every context-dependent shape answers its default.</summary>
    public static BodyCollisionContext Neutral => default;
}
