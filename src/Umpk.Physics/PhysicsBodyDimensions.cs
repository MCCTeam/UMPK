namespace Umpk.Physics;

/// <summary>Host-owned cached body dimensions and the native float eye height. The eye can be above the body, including a legacy death callback.</summary>
/// <param name="Width">The native body width.</param>
/// <param name="Height">The native body height.</param>
/// <param name="EyeHeight">The native eye height, independent of body height.</param>
public readonly record struct PhysicsBodyDimensions(float Width, float Height, float EyeHeight);
