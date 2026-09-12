using Umpk.Geometry;

namespace Umpk.Client.Events;

/// <summary>Raised when the local player's health, food, or saturation changes.</summary>
public sealed record HealthChanged(float Health, int Food, float Saturation) : IClientEvent;

/// <summary>Raised when the server corrects the local player's position (teleport / position sync).</summary>
public sealed record PositionCorrected(Vec3d Position, float Yaw, float Pitch) : IClientEvent;

/// <summary>Raised when the local player dies (health reaches zero).</summary>
public sealed record Died : IClientEvent;

/// <summary>Raised after a respawn is applied.</summary>
public sealed record Respawned : IClientEvent;

/// <summary>Raised when the local player's experience changes.</summary>
public sealed record ExperienceChanged(float Progress, int Level, int TotalExperience) : IClientEvent;

/// <summary>Raised when the server updates the local player's abilities.</summary>
public sealed record AbilitiesChanged(bool Invulnerable, bool Flying, bool MayFly, bool InstantBuild, float FlyingSpeed, float WalkingSpeed) : IClientEvent;

/// <summary>Raised when the held hotbar slot changes (server-driven).</summary>
public sealed record HeldSlotChanged(int Slot) : IClientEvent;

/// <summary>Raised when the game mode changes.</summary>
public sealed record GameModeChanged(Umpk.Game.Players.GameMode GameMode) : IClientEvent;
