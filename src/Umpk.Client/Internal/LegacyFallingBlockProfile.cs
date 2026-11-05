using Umpk.Game.Blocks;
using Umpk.Game.Entities;

namespace Umpk.Client.Internal;

/// <summary>The measured 1.8.9, 1.9 and bounded 1.13 falling-block slices. Other eras and unclassified callbacks retain unknown motion.</summary>
internal static class LegacyFallingBlockProfile
{
    public static bool Supports(Entity entity, int protocol) => protocol is 47 or 107 or 393 && entity.Type.Id == Identifier.Minecraft("falling_block");

    public static bool UsesClassicCollisionQuery(int protocol) => protocol == 47;

    public static float SpawnYOffset(int protocol) => protocol is 107 or 393 ? (1.0F - .98F) / 2.0F : 0;

    public static bool UsesVoxelCollisionQuery(int protocol) => protocol == 393;

    public static bool HasSimpleSourceRemoval(int blockId, int protocol)
        => !UsesVoxelCollisionQuery(protocol) || blockId is 1 or 28 or 30 or 531;

    public static bool TryGetCarriedBlock(IBlockDataSource source, int protocol, int data, out int blockId)
    {
        blockId = data & 4095;
        if (UsesVoxelCollisionQuery(protocol))
        {
            if (!source.IsValidState(data) || (source.GetFlags(data) & BlockFlags.Air) != 0)
                return false;
            blockId = source.GetBlockNetworkId(data);
        }
        return blockId != 0 && source.Blocks.TryGet(blockId, out _);
    }

    public static bool TryGetNoGravity(Entity entity, int protocol, out bool noGravity)
    {
        noGravity = false;
        if (!UsesVoxelCollisionQuery(protocol) || !entity.Metadata.TryGet(5, out MetadataValue value))
            return true;
        if (value.Kind != MetadataValueKind.Boolean)
            return false;
        noGravity = value.AsBoolean();
        return true;
    }

    // Original 1.8.9 reflection checks all six collision, bounds-update and movement-callback owners. The separate 1.9 list retains its original ajt bindings; the 1.13 slice admits only its source-bound native stone floor. The query also requires exact unit-cube shapes.
    public static bool HasBaseCollisionAndCallbacks(int blockId, int protocol = 107) => protocol == 393 ? blockId == 1 : protocol == 47 ? blockId is
        1 or 2 or 3 or 4 or 5 or 7 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 35 or 41 or 42 or 45 or 47 or 48 or 49 or 52 or 56 or 57 or 58 or 61 or 62 or 73 or 74 or 79 or 80 or 82 or 84 or 86 or 87 or 89 or 91 or 93 or 94 or 95 or 97 or 98 or 99 or 100 or 103 or 110 or 112 or 116 or 121 or 122 or 123 or 124 or 129 or 130 or 133 or 137 or 138 or 140 or 149 or 150 or 152 or 153 or 155 or 158 or 159 or 161 or 162 or 166 or 168 or 169 or 170 or 172 or 173 or 174 or 179
        : protocol == 107 && blockId is
        1 or 2 or 3 or 4 or 5 or 7 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 35 or 41 or 42 or 45 or 47 or 48 or 49 or 52 or 56 or 57 or 58 or 61 or 62 or 73 or 74 or 79 or 80 or 82 or 84 or 86 or 87 or 89 or 91 or 95 or 97 or 98 or 99 or 100 or 103 or 110 or 112 or 121 or 123 or 124 or 129 or 133 or 137 or 138 or 152 or 153 or 155 or 158 or 159 or 161 or 162 or 166 or 168 or 169 or 170 or 172 or 173 or 174 or 179 or 200 or 201 or 202 or 206 or 210 or 211 or 212 or 255;

    // Original 1.8.9 ahv and 1.9 amo return a null box for every liquid level; both inherit base movement callbacks and collection.
    public static bool HasBaseCallbacksAndEmptyCollision(int blockId, int protocol = 107) => protocol is 47 or 107 && blockId is 8 or 9 or 10 or 11;

    // Complete original class/registry audits prove only boats supply hard target boxes in 1.8.9, with Shulkers added in 1.9. Minecart source queries do not make carts hard targets for falling blocks; unknown types are excluded from both finite lists.
    public static bool HasNoHardPeerBox(Identifier type, int protocol = 107) => type.Namespace == "minecraft" && (protocol == 393 ? type.Path is
        "area_effect_cloud" or "armor_stand" or "arrow" or "bat" or "blaze" or "cave_spider" or "chest_minecart" or "chicken" or "cod" or "command_block_minecart" or "cow" or "creeper" or "dolphin" or "donkey" or "dragon_fireball" or "drowned" or "egg" or "elder_guardian" or "end_crystal" or "ender_dragon" or "ender_pearl" or "enderman" or "endermite" or "evoker" or "evoker_fangs" or "experience_bottle" or "experience_orb" or "eye_of_ender" or "falling_block" or "fireball" or "firework_rocket" or "fishing_bobber" or "furnace_minecart" or "ghast" or "giant" or "guardian" or "hopper_minecart" or "horse" or "husk" or "illusioner" or "iron_golem" or "item" or "item_frame" or "leash_knot" or "lightning_bolt" or "llama" or "llama_spit" or "magma_cube" or "minecart" or "mooshroom" or "mule" or "ocelot" or "painting" or "parrot" or "phantom" or "pig" or "player" or "polar_bear" or "potion" or "pufferfish" or "rabbit" or "salmon" or "sheep" or "shulker_bullet" or "silverfish" or "skeleton" or "skeleton_horse" or "slime" or "small_fireball" or "snow_golem" or "snowball" or "spawner_minecart" or "spectral_arrow" or "spider" or "squid" or "stray" or "tnt" or "tnt_minecart" or "trident" or "tropical_fish" or "turtle" or "vex" or "villager" or "vindicator" or "witch" or "wither" or "wither_skeleton" or "wither_skull" or "wolf" or "zombie" or "zombie_horse" or "zombie_pigman" or "zombie_villager"
        : protocol == 47 ? type.Path is
        "armor_stand" or "arrow" or "bat" or "blaze" or "cat" or "cave_spider" or "chest_minecart" or "chicken" or "command_block_minecart" or "cow" or "creeper" or "egg" or "end_crystal" or "ender_dragon" or "ender_pearl" or "enderman" or "endermite" or "experience_bottle" or "experience_orb" or "eye_of_ender" or "falling_block" or "fireball" or "firework_rocket" or "fishing_bobber" or "furnace_minecart" or "ghast" or "giant" or "guardian" or "hopper_minecart" or "horse" or "iron_golem" or "item" or "item_frame" or "leash_knot" or "lightning_bolt" or "magma_cube" or "minecart" or "mooshroom" or "painting" or "pig" or "player" or "potion" or "rabbit" or "sheep" or "silverfish" or "skeleton" or "slime" or "small_fireball" or "snow_golem" or "snowball" or "spawner_minecart" or "spider" or "squid" or "tnt" or "tnt_minecart" or "villager" or "witch" or "wither" or "wither_skull" or "wolf" or "zombie" or "zombified_piglin"
        : protocol == 107 && type.Path is
        "area_effect_cloud" or "armor_stand" or "arrow" or "bat" or "blaze" or "cave_spider" or "chest_minecart" or "chicken" or "commandblock_minecart" or "cow" or "creeper" or "dragon_fireball" or "egg" or "ender_crystal" or "ender_dragon" or "ender_pearl" or "enderman" or "endermite" or "eye_of_ender_signal" or "falling_block" or "fireball" or "fireworks_rocket" or "fishing_bobber" or "furnace_minecart" or "ghast" or "giant" or "guardian" or "hopper_minecart" or "horse" or "item" or "item_frame" or "leash_knot" or "lightning_bolt" or "magma_cube" or "minecart" or "mooshroom" or "ocelot" or "painting" or "pig" or "player" or "potion" or "rabbit" or "sheep" or "shulker_bullet" or "silverfish" or "skeleton" or "slime" or "small_fireball" or "snowball" or "snowman" or "spawner_minecart" or "spectral_arrow" or "spider" or "squid" or "tnt" or "tnt_minecart" or "villager" or "villager_golem" or "witch" or "wither" or "wither_skull" or "wolf" or "xp_bottle" or "xp_orb" or "zombie" or "zombie_pigman");
}
