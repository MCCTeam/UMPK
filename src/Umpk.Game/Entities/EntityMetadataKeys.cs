using Umpk.Text;

namespace Umpk.Game.Entities;

/// <summary>The canonical tier-2 semantic metadata keys. Each is a shared singleton whose identity the <see cref="IMetadataKeySource"/> maps to a per-version tier-1 index. The set covers the common cross-entity fields; the Java data/codec layer's dataset may register more keys of its own.</summary>
public static class EntityMetadataKeys
{
    /// <summary>The base entity shared-flags bitfield (index 0 on all modern versions): on-fire, crouching, sprinting, swimming, invisible, glowing, fall-flying.</summary>
    public static MetadataKey<sbyte> SharedFlags { get; } = new("shared_flags", v => v.AsByte());

    /// <summary>Air supply (VarInt ticks).</summary>
    public static MetadataKey<int> AirSupply { get; } = new("air_supply", v => v.AsVarInt());

    /// <summary>The custom display name, if any.</summary>
    public static MetadataKey<Component?> CustomName { get; } = new("custom_name", v => v.AsOptionalComponent());

    /// <summary>Whether the custom name is always rendered.</summary>
    public static MetadataKey<bool> CustomNameVisible { get; } = new("custom_name_visible", v => v.AsBoolean());

    /// <summary>Whether the entity is silent.</summary>
    public static MetadataKey<bool> Silent { get; } = new("silent", v => v.AsBoolean());

    /// <summary>Whether gravity is disabled for the entity.</summary>
    public static MetadataKey<bool> NoGravity { get; } = new("no_gravity", v => v.AsBoolean());

    /// <summary>The entity pose.</summary>
    public static MetadataKey<EntityPose> Pose { get; } = new("pose", v => v.AsPose());

    /// <summary>Ticks frozen in powder snow (VarInt).</summary>
    public static MetadataKey<int> TicksFrozen { get; } = new("ticks_frozen", v => v.AsVarInt());

    /// <summary>Living-entity health (float).</summary>
    public static MetadataKey<float> Health { get; } = new("health", v => v.AsFloat());

    /// <summary>The living-entity potion-effect particle color / ambient flags (VarInt).</summary>
    public static MetadataKey<int> EffectParticles { get; } = new("effect_particles", v => v.AsVarInt());

    /// <summary>Whether the living-entity effect is ambient.</summary>
    public static MetadataKey<bool> EffectAmbience { get; } = new("effect_ambience", v => v.AsBoolean());

    /// <summary>The number of stacked arrows in the living entity (VarInt).</summary>
    public static MetadataKey<int> ArrowCount { get; } = new("arrow_count", v => v.AsVarInt());

    /// <summary>The item stack carried by a dropped-item entity.</summary>
    public static MetadataKey<IMetadataSlot?> CarriedItem { get; } = new("carried_item", v => v.AsSlot());
}
