namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Reports how the stepper treated the requested input for this tick.</summary>
internal enum BedrockInputDisposition
{
    /// <summary>The input was applied for this tick.</summary>
    Applied = 0,
    /// <summary>The input requested a mode the scaffold profile leaves absent, so the state is unchanged.</summary>
    UnavailableMode = 1,
    /// <summary>The input was refused because required terrain is unknown, so the state is unchanged.</summary>
    RefusedUnknownTerrain = 2,
}

/// <summary>Reports what the collision query found for this tick.</summary>
internal enum BedrockCollisionOutcome
{
    /// <summary>No known solid was found, or no collision query ran because the input was not applied.</summary>
    KnownFree = 0,
    /// <summary>The evaluated path contacted known solid.</summary>
    CollidedSolid = 1,
    /// <summary>The collision query met unknown terrain and refused movement.</summary>
    RefusedUnknown = 2,
}

/// <summary>The applied portion of the request plus the mode outcome.</summary>
internal readonly record struct BedrockAppliedInputResult(BedrockInputDisposition Disposition, double AppliedWishX, double AppliedWishZ, bool AppliedJump);

/// <summary>Axis flags for solid contact in this tick.</summary>
[Flags]
internal enum BedrockBlockedAxes
{
    /// <summary>No axis contacted solid in this tick.</summary>
    None = 0,
    /// <summary>The X axis contacted solid in this tick.</summary>
    X = 1,
    /// <summary>The Y axis contacted solid in this tick.</summary>
    Y = 2,
    /// <summary>The Z axis contacted solid in this tick.</summary>
    Z = 4,
}

/// <summary>Collision outcome plus blocked axes and a lower-bound count of refused unknown encounters.</summary>
/// <remarks>Callers must check disposition first because the collision value is unevaluated when the input disposition is not Applied.</remarks>
internal readonly record struct BedrockCollisionResult(BedrockCollisionOutcome Outcome, BedrockBlockedAxes BlockedAxes, int UnknownCells);
