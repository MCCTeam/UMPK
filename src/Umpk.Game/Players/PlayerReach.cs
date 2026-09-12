using Umpk.Geometry;

namespace Umpk.Game.Players;

/// <summary>
/// Player interaction-reach rules shared by call sites that test blocks or entities.
///
/// <para>
/// There are three distinct rules in the supported protocol range, not one:
/// <list type="bullet">
/// <item>The modern (1.20.5+) client/server rule measures the EYE to the target's BOUNDING BOX (never its
/// centre) against a per-attribute reach that differs by game mode: <see cref="IsWithinBlockReach(Vec3d, BlockPos, GameMode)"/> and <see cref="IsWithinEntityReach"/>.</item>
/// <item>The pre-1.20.5 server-side verification is a flat 6.0-block, EYE-TO-CENTRE check that never moved
/// with the attribute system and is a separate rule from the client's own pick range: <see cref="IsWithinLegacyServerReach"/>.</item>
/// <item><see cref="ServerVerificationBuffer"/> is the server's own slack on top of the client-reported
/// attribute value (1.20.5+), recorded here as a constant because it belongs to the same rule family even though no method below folds it in: it is the SERVER's tolerance for a client whose measured attribute may have moved since the last sync, not a bound a client-side caller should add to its own check.</item>
/// </list>
/// </para>
/// </summary>
public static class PlayerReach
{
    /// <summary>The default standing eye height added to a feet position.</summary>
    public const double StandingEyeHeight = 1.62;

    /// <summary>Survival/adventure block-interaction reach, in blocks, measured to the target's box.</summary>
    public const double SurvivalBlockReach = 4.5;

    /// <summary>Creative block-interaction reach. Creative mode adds a +0.5 transient modifier, so 4.5 + 0.5 = 5.0.</summary>
    public const double CreativeBlockReach = 5.0;

    /// <summary>Survival/adventure entity-interaction reach.</summary>
    public const double SurvivalEntityReach = 3.0;

    /// <summary>Creative entity-interaction reach. Creative mode adds a +2.0 transient modifier, so 3.0 + 2.0 = 5.0.</summary>
    public const double CreativeEntityReach = 5.0;

    /// <summary>The server's own slack added on top of the attribute value it trusts the client to report (1.20.5+). It is not folded into any method here; see the type-level remarks.</summary>
    public const double ServerVerificationBuffer = 1.0;

    /// <summary>The pre-1.20.5 SERVER-side block-interaction bound: 6 blocks, EYE TO CENTRE, and it never read the attribute system at all. Legacy 1.8 also rejects values past the same squared threshold of 36.0.</summary>
    public const double LegacyServerBlockReach = 6.0;

    /// <summary>The eye position for a player standing with feet at <paramref name="feet"/>.</summary>
    public static Vec3d EyePosition(Vec3d feet) => feet.Add(0, StandingEyeHeight, 0);

    /// <summary>The block-interaction reach for a game mode. Spectator maps to <see cref="CreativeBlockReach"/>. The attribute does not widen for spectator, but block placement is unconditionally denied to a spectator regardless of distance, so the un-widened 4.5 is not a bound anything can actually reach either. This helper is deliberately at least as generous as creative for spectator, rather than mirroring the un-widened attribute value a caller could never legitimately hit the placement gate with anyway.</summary>
    public static double BlockReachFor(GameMode mode) =>
        mode is GameMode.Creative or GameMode.Spectator ? CreativeBlockReach : SurvivalBlockReach;

    /// <summary>The entity-interaction reach for a game mode; see <see cref="BlockReachFor"/> for spectator behavior.</summary>
    public static double EntityReachFor(GameMode mode) =>
        mode is GameMode.Creative or GameMode.Spectator ? CreativeEntityReach : SurvivalEntityReach;

    /// <summary>Whether a block at <paramref name="target"/> is within block-interaction reach of an eye at <paramref name="eye"/>, for the given game mode. Measures to the block's BOX (<see cref="Aabb.DistanceSqrTo"/>), never its centre; see <see cref="IsWithinBlockReach(Vec3d, BlockPos, double)"/> for the exact comparison.</summary>
    public static bool IsWithinBlockReach(Vec3d eye, BlockPos target, GameMode mode) =>
        IsWithinBlockReach(eye, target, BlockReachFor(mode));

    /// <summary>Whether a block at <paramref name="target"/> is within <paramref name="reach"/> blocks of an eye at <paramref name="eye"/>, measured to the block's box using strict less-than, so a point exactly <paramref name="reach"/> blocks from the nearest face is judged out of range, matching vanilla.</summary>
    public static bool IsWithinBlockReach(Vec3d eye, BlockPos target, double reach)
    {
        Aabb box = Aabb.BlockAt(target.X, target.Y, target.Z);
        return box.DistanceSqrTo(eye) < reach * reach;
    }

    /// <summary>Whether an entity's box is within entity-interaction reach of an eye, for the given game mode. Measured to the entity's box, strict less-than, same as <see cref="IsWithinBlockReach(Vec3d, BlockPos, double)"/>.</summary>
    public static bool IsWithinEntityReach(Vec3d eye, Aabb entityBox, GameMode mode)
    {
        double reach = EntityReachFor(mode);
        return entityBox.DistanceSqrTo(eye) < reach * reach;
    }

    /// <summary>The pre-1.20.5 SERVER verification rule: 6 blocks, eye to the block's CENTRE, inclusive of the boundary (vanilla rejects only STRICTLY beyond it: <c>&gt; MAX_INTERACTION_DISTANCE</c>, so exactly 36.0 passes). This is a distinct rule from the client's own pick range and from <see cref="IsWithinBlockReach(Vec3d, BlockPos, GameMode)"/>: a target the modern box rule already refuses can still be accepted by a server still running this older, looser, centre-based one.</summary>
    public static bool IsWithinLegacyServerReach(Vec3d eye, BlockPos target)
    {
        Vec3d center = target.Center;
        double dx = eye.X - center.X;
        double dy = eye.Y - center.Y;
        double dz = eye.Z - center.Z;
        return ((dx * dx) + (dy * dy) + (dz * dz)) <= LegacyServerBlockReach * LegacyServerBlockReach;
    }
}
