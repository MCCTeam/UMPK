namespace Umpk.Client.Bedrock.Movement.Simulation;

/// <summary>Synthetic scaffold parameters with every unmeasured Bedrock mode left absent by default.</summary>
/// <remarks>Numeric values are caller policy, not measured Bedrock facts.</remarks>
internal sealed class BedrockMovementProfile
{
    /// <summary>Initializes scaffold parameters with every unmeasured Bedrock mode left absent unless the caller supplies it.</summary>
    /// <remarks>Supplied nullable numbers must be finite and positive, and the collision epsilon must stay in (0, 0.01).</remarks>
    internal BedrockMovementProfile(
        double? walkSpeedBlocksPerSecond = null,
        double? gravityBlocksPerSecondSquared = null,
        double? terminalVelocityBlocksPerSecond = null,
        double? jumpImpulseBlocksPerSecond = null,
        double? playerWidthBlocks = null,
        double? playerHeightBlocks = null,
        double collisionEpsilonBlocks = 0.0001)
    {
        if (walkSpeedBlocksPerSecond.HasValue)
        {
            if (double.IsNaN(walkSpeedBlocksPerSecond.Value) || double.IsInfinity(walkSpeedBlocksPerSecond.Value))
                throw new ArgumentOutOfRangeException(nameof(walkSpeedBlocksPerSecond), "The walk speed must be a finite value.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(walkSpeedBlocksPerSecond.Value);
        }
        if (gravityBlocksPerSecondSquared.HasValue)
        {
            if (double.IsNaN(gravityBlocksPerSecondSquared.Value) || double.IsInfinity(gravityBlocksPerSecondSquared.Value))
                throw new ArgumentOutOfRangeException(nameof(gravityBlocksPerSecondSquared), "Gravity must be a finite value.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gravityBlocksPerSecondSquared.Value);
        }
        if (terminalVelocityBlocksPerSecond.HasValue)
        {
            if (double.IsNaN(terminalVelocityBlocksPerSecond.Value) || double.IsInfinity(terminalVelocityBlocksPerSecond.Value))
                throw new ArgumentOutOfRangeException(nameof(terminalVelocityBlocksPerSecond), "The terminal velocity must be a finite value.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(terminalVelocityBlocksPerSecond.Value);
        }
        if (jumpImpulseBlocksPerSecond.HasValue)
        {
            if (double.IsNaN(jumpImpulseBlocksPerSecond.Value) || double.IsInfinity(jumpImpulseBlocksPerSecond.Value))
                throw new ArgumentOutOfRangeException(nameof(jumpImpulseBlocksPerSecond), "The jump impulse must be a finite value.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(jumpImpulseBlocksPerSecond.Value);
        }
        if (playerWidthBlocks.HasValue)
        {
            if (double.IsNaN(playerWidthBlocks.Value) || double.IsInfinity(playerWidthBlocks.Value))
                throw new ArgumentOutOfRangeException(nameof(playerWidthBlocks), "The player width must be a finite value.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerWidthBlocks.Value);
        }
        if (playerHeightBlocks.HasValue)
        {
            if (double.IsNaN(playerHeightBlocks.Value) || double.IsInfinity(playerHeightBlocks.Value))
                throw new ArgumentOutOfRangeException(nameof(playerHeightBlocks), "The player height must be a finite value.");
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerHeightBlocks.Value);
        }
        if (double.IsNaN(collisionEpsilonBlocks) || double.IsInfinity(collisionEpsilonBlocks) || collisionEpsilonBlocks <= 0.0 || collisionEpsilonBlocks >= 0.01)
            throw new ArgumentOutOfRangeException(nameof(collisionEpsilonBlocks), "The collision epsilon must be a finite value in (0, 0.01).");
        WalkSpeedBlocksPerSecond = walkSpeedBlocksPerSecond;
        GravityBlocksPerSecondSquared = gravityBlocksPerSecondSquared;
        TerminalVelocityBlocksPerSecond = terminalVelocityBlocksPerSecond;
        JumpImpulseBlocksPerSecond = jumpImpulseBlocksPerSecond;
        PlayerWidthBlocks = playerWidthBlocks;
        PlayerHeightBlocks = playerHeightBlocks;
        CollisionEpsilonBlocks = collisionEpsilonBlocks;
    }

    /// <summary>Gets the default profile with every Bedrock mode absent so any mode-dependent input is unavailable.</summary>
    internal static BedrockMovementProfile Default { get; } = new();

    /// <summary>Gets the caller-supplied walk speed in blocks per second, or null when the horizontal mode is absent.</summary>
    internal double? WalkSpeedBlocksPerSecond { get; }

    /// <summary>Gets the caller-supplied gravity in blocks per second squared, or null when the gravity mode is absent.</summary>
    internal double? GravityBlocksPerSecondSquared { get; }

    /// <summary>Gets the caller-supplied terminal fall speed in blocks per second, or null when the gravity mode is absent.</summary>
    internal double? TerminalVelocityBlocksPerSecond { get; }

    /// <summary>Gets the caller-supplied jump impulse in blocks per second, or null when the jump mode is absent.</summary>
    internal double? JumpImpulseBlocksPerSecond { get; }

    /// <summary>Gets the caller-supplied player width in blocks, or null when collision extents are absent.</summary>
    internal double? PlayerWidthBlocks { get; }

    /// <summary>Gets the caller-supplied player height in blocks, or null when collision extents are absent.</summary>
    internal double? PlayerHeightBlocks { get; }

    /// <summary>Gets the scaffold-policy collision epsilon in blocks used to offset full-cube contact.</summary>
    internal double CollisionEpsilonBlocks { get; }

    /// <summary>Gets whether the profile carries the speed and extents needed for scaffold horizontal motion.</summary>
    internal bool SupportsHorizontal => WalkSpeedBlocksPerSecond.HasValue && PlayerWidthBlocks.HasValue && PlayerHeightBlocks.HasValue;

    /// <summary>Gets whether the profile carries gravity, terminal velocity, and the extents needed for scaffold vertical motion.</summary>
    /// <remarks>Gravity and jump each require extents so the scaffold never phases through known solid without a collision shape.</remarks>
    internal bool SupportsGravity => GravityBlocksPerSecondSquared.HasValue && TerminalVelocityBlocksPerSecond.HasValue && PlayerWidthBlocks.HasValue && PlayerHeightBlocks.HasValue;

    /// <summary>Gets whether the profile carries jump impulse and the extents needed for scaffold vertical motion.</summary>
    /// <remarks>Gravity and jump each require extents so the scaffold never phases through known solid without a collision shape.</remarks>
    internal bool SupportsJump => JumpImpulseBlocksPerSecond.HasValue && PlayerWidthBlocks.HasValue && PlayerHeightBlocks.HasValue;
}
