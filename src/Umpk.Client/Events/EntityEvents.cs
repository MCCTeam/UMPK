using Umpk.Game.Entities;

namespace Umpk.Client.Events;

/// <summary>Raised when an entity is spawned/added to the tracker.</summary>
public sealed record EntitySpawned(int EntityId, Entity Entity) : IClientEvent;

/// <summary>Raised when an entity is removed.</summary>
public sealed record EntityRemoved(int EntityId) : IClientEvent;

/// <summary>Raised when an entity's position changes.</summary>
public sealed record EntityMoved(int EntityId) : IClientEvent;

/// <summary>Raised when an entity's metadata changes.</summary>
public sealed record EntityMetadataChanged(int EntityId) : IClientEvent;

/// <summary>Raised when a status effect is applied to an entity.</summary>
public sealed record EntityEffectApplied(int EntityId, int EffectId, int Amplifier, int Duration) : IClientEvent;

/// <summary>Raised when a status effect is removed from an entity.</summary>
public sealed record EntityEffectRemoved(int EntityId, int EffectId) : IClientEvent;

/// <summary>Raised when an item entity is picked up by a collector.</summary>
public sealed record ItemPickedUp(int ItemEntityId, int CollectorEntityId, int Amount) : IClientEvent;

/// <summary>Raised when an entity's passenger/vehicle graph changes.</summary>
public sealed record EntityPassengersChanged(int VehicleId) : IClientEvent;

/// <summary>Raised when an entity plays a status event/animation.</summary>
public sealed record EntityStatusChanged(int EntityId, sbyte EventId) : IClientEvent;

/// <summary>Raised when the server requests the client look at a target (FacePlayer/LookAt).</summary>
public sealed record LookAtRequested(Vec3dTarget Target) : IClientEvent;

/// <summary>The target of a server-requested look.</summary>
public readonly record struct Vec3dTarget(double X, double Y, double Z);

/// <summary>Raised when an entity takes damage from a typed source. Ids are registry ids; -1 absent.</summary>
public sealed record EntityDamaged(
    int EntityId,
    int SourceTypeId,
    int? SourceCauseId,
    int? SourceDirectId,
    Umpk.Geometry.Vec3d? SourcePosition) : IClientEvent;

/// <summary>Raised for an entity hurt animation (direction the damage came from).</summary>
public sealed record EntityHurt(int EntityId, float Yaw) : IClientEvent;

/// <summary>Raised when a tracked entity's equipment changes. The new value is already written into <see cref="Entity.Equipment"/> before this is published, so a subscriber that reads the entity sees this update.</summary>
public sealed record EntityEquipmentChanged(int EntityId, EquipmentSlot Slot) : IClientEvent;
