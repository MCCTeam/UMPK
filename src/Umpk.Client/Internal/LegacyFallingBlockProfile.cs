using Umpk.Game.Entities;

namespace Umpk.Client.Internal;

/// <summary>The measured 1.9 falling-block slice. Other eras and unclassified callbacks retain unknown motion.</summary>
internal static class LegacyFallingBlockProfile
{
    public static bool Supports(Entity entity, int protocol) => protocol == 107 && entity.Type.Id == Identifier.Minecraft("falling_block");

    // Original 1.9 registry reflection proves that these blocks inherit the fixed cube, inside callback and landing callback from Block (ajt).
    public static bool HasBaseCollisionAndCallbacks(int blockId) => blockId is
        1 or 2 or 3 or 4 or 5 or 7 or 12 or 13 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 35 or 41 or 42 or 45 or 47 or 48 or 49 or 52 or 56 or 57 or 58 or 61 or 62 or 73 or 74 or 79 or 80 or 82 or 84 or 86 or 87 or 89 or 91 or 95 or 97 or 98 or 99 or 100 or 103 or 110 or 112 or 121 or 123 or 124 or 129 or 133 or 137 or 138 or 152 or 153 or 155 or 158 or 159 or 161 or 162 or 166 or 168 or 169 or 170 or 172 or 173 or 174 or 179 or 200 or 201 or 202 or 206 or 210 or 211 or 212 or 255;

    // Complete original 1.9 entity-class inheritance audit: boat and Shulker are the only registered peers with a non-null hard box. Minecart's source-specific peer query does not make it a hard target for a falling block.
    public static bool HasNoHardPeerBox(Identifier type) => type.Namespace == "minecraft" && type.Path is
        "area_effect_cloud" or "armor_stand" or "arrow" or "bat" or "blaze" or "cave_spider" or "chest_minecart" or "chicken" or "commandblock_minecart" or "cow" or "creeper" or "dragon_fireball" or "egg" or "ender_crystal" or "ender_dragon" or "ender_pearl" or "enderman" or "endermite" or "eye_of_ender_signal" or "falling_block" or "fireball" or "fireworks_rocket" or "fishing_bobber" or "furnace_minecart" or "ghast" or "giant" or "guardian" or "hopper_minecart" or "horse" or "item" or "item_frame" or "leash_knot" or "lightning_bolt" or "magma_cube" or "minecart" or "mooshroom" or "ocelot" or "painting" or "pig" or "player" or "potion" or "rabbit" or "sheep" or "shulker_bullet" or "silverfish" or "skeleton" or "slime" or "small_fireball" or "snowball" or "snowman" or "spawner_minecart" or "spectral_arrow" or "spider" or "squid" or "tnt" or "tnt_minecart" or "villager" or "villager_golem" or "witch" or "wither" or "wither_skull" or "wolf" or "xp_bottle" or "xp_orb" or "zombie" or "zombie_pigman";
}
