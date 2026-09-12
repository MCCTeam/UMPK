namespace Umpk.Game.Registries;

/// <summary>The registry identifiers UMPK surfaces through typed accessors on <see cref="RegistryAccess"/>. They are readonly value constants, not mutable state.</summary>
public static class RegistryIds
{
    /// <summary><c>minecraft:block</c>.</summary>
    public static Identifier Block => Identifier.Minecraft("block");

    /// <summary><c>minecraft:item</c>.</summary>
    public static Identifier Item => Identifier.Minecraft("item");

    /// <summary><c>minecraft:entity_type</c>.</summary>
    public static Identifier EntityType => Identifier.Minecraft("entity_type");

    /// <summary><c>umpk:legacy_object_entity_type</c>: the pre-1.14 SpawnObject id space. Not a vanilla registry, hence the non-minecraft namespace. Versions below 1.14 spawn objects (boats, items, minecarts, projectiles, falling blocks) through an id space that is DISJOINT from the SpawnMob space that <see cref="EntityType"/> holds, and the same number names different types in each, so the two cannot share one table.</summary>
    public static Identifier LegacyObjectType => new("umpk", "legacy_object_entity_type");

    /// <summary><c>minecraft:dimension_type</c>.</summary>
    public static Identifier DimensionType => Identifier.Minecraft("dimension_type");

    /// <summary><c>minecraft:worldgen/biome</c>.</summary>
    public static Identifier Biome => new("minecraft", "worldgen/biome");

    /// <summary><c>minecraft:chat_type</c>: the datapack-driven chat decorations. Sent by the server from 1.19 (protocol 759) and by no earlier version, because before 1.19 the server composed the chat line itself and left the client nothing to decorate.</summary>
    public static Identifier ChatType => Identifier.Minecraft("chat_type");

    /// <summary><c>minecraft:enchantment</c>.</summary>
    public static Identifier Enchantment => Identifier.Minecraft("enchantment");

    /// <summary><c>minecraft:mob_effect</c>.</summary>
    public static Identifier MobEffect => Identifier.Minecraft("mob_effect");

    /// <summary><c>minecraft:attribute</c>.</summary>
    public static Identifier Attribute => Identifier.Minecraft("attribute");

    /// <summary><c>minecraft:menu</c>: the container-window kinds. Real and wire-numbered from 1.14; before that there is no such registry and open_screen names the window with a string instead, which UMPK still surfaces here (keyed by identifier, with a synthetic numeric id) so that resolving an open container looks the same on every supported version.</summary>
    public static Identifier Menu => Identifier.Minecraft("menu");
}
