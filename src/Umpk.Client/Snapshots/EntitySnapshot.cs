using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Client.Snapshots;

/// <summary>An immutable snapshot of one tracked entity.</summary>
/// <param name="Id">The server-assigned entity id.</param>
/// <param name="Uuid">The entity uuid.</param>
/// <param name="TypeId">The namespaced entity type id, resolved against the session's static registries. Real registry identifiers only; a known type never reads back as <c>minecraft:unknown</c> (the client always wires static registries at connect, so <see cref="Umpk.Game.Registries.RegistryEntry{T}.Id"/> is always the server's own key).</param>
/// <param name="Position">The entity position.</param>
/// <param name="Velocity">The entity velocity.</param>
/// <param name="Yaw">Body yaw in degrees.</param>
/// <param name="Pitch">Pitch in degrees.</param>
/// <param name="HeadYaw">Head yaw in degrees.</param>
/// <param name="OnGround">Whether the entity was on the ground per the last update.</param>
/// <param name="Pose">The entity pose, as the typed enum (never flattened to a string).</param>
/// <param name="CustomName">The custom display name, still a structured <see cref="Component"/> (style intact), or null.</param>
/// <param name="PlayerName">The player profile name for player entities, else null.</param>
/// <param name="Equipment">Worn/held items keyed by equipment slot. An empty slot carries <see cref="ItemStack.Empty"/>, never a missing key: every slot the tracker has ever seen for this entity is present.</param>
/// <param name="Effects">Active status effects on the entity.</param>
/// <param name="PassengerIds">The ids of entities riding this one.</param>
/// <param name="VehicleId">The id of the entity this one rides, or null.</param>
public sealed record EntitySnapshot(
    int Id,
    Guid Uuid,
    Identifier TypeId,
    Vec3d Position,
    Vec3d Velocity,
    float Yaw,
    float Pitch,
    float HeadYaw,
    bool OnGround,
    EntityPose Pose,
    Component? CustomName,
    string? PlayerName,
    IReadOnlyDictionary<EquipmentSlot, ItemStack> Equipment,
    IReadOnlyList<EffectSnapshot> Effects,
    IReadOnlyList<int> PassengerIds,
    int? VehicleId)
{
    /// <summary>Projects an <see cref="EntitySnapshot"/> from a live tracked entity. Pure; no session loop involved.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
    public static EntitySnapshot Project(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var equipment = new Dictionary<EquipmentSlot, ItemStack>(entity.Equipment.Count);
        foreach (KeyValuePair<EquipmentSlot, IMetadataSlot?> slot in entity.Equipment)
            equipment[slot.Key] = slot.Value is ItemStack stack ? stack : ItemStack.Empty;

        var effects = new List<EffectSnapshot>(entity.Effects.Count);
        foreach (KeyValuePair<int, EffectInstance> pair in entity.Effects)
            effects.Add(EffectSnapshot.Project(pair.Value));

        var passengers = new List<int>(entity.Passengers.Count);
        foreach (Entity passenger in entity.Passengers)
            passengers.Add(passenger.Id);

        return new EntitySnapshot(
            entity.Id,
            entity.Uuid,
            entity.Type.Id,
            entity.Position,
            entity.Velocity,
            entity.Yaw,
            entity.Pitch,
            entity.HeadYaw,
            entity.OnGround,
            entity.Pose,
            entity.CustomName,
            entity.PlayerProfile?.Name,
            equipment,
            effects,
            passengers,
            entity.Vehicle?.Id);
    }
}
