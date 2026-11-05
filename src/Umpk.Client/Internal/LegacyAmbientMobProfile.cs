using Umpk.Client.Actions;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>The source-bound 1.11 bat and squid slice. Adjacent releases retain their existing unknown geometry.</summary>
internal readonly record struct LegacyAmbientMobProfile(bool IsBat)
{
    public float Width => IsBat ? .5F : .8F;
    public float Height => IsBat ? .9F : .8F;
    public float DefaultHealth => IsBat ? 6F : 10F;
    public EntityGeometryDimensions Dimensions => new(Width, Height, true, true);

    public static Vec3d SpawnVelocity(int protocol, int typeId, short x, short y, short z, Vec3d fallback)
        => protocol == 315 && typeId is 65 or 94 ? new(x / 8000.0F, y / 8000.0F, z / 8000.0F) : fallback;

    // The complete original 1.11 registry and packet-identity audit resolves these peer.ag getters to null. Placement obstruction and picking remain separate.
    public static bool HasNoHardPeerBox(Identifier type)
    {
        string path = type.Path;
        if (!type.IsMinecraft || path.Length == 0)
            return false;
        const string names = "area_effect_cloud|armor_stand|arrow|bat|blaze|cave_spider|chest_minecart|chicken|commandblock_minecart|cow|creeper|donkey|dragon_fireball|egg|elder_guardian|ender_crystal|ender_dragon|ender_pearl|enderman|endermite|evocation_fangs|evocation_illager|eye_of_ender_signal|falling_block|fireball|fireworks_rocket|fishing_bobber|furnace_minecart|ghast|giant|guardian|hopper_minecart|horse|husk|item|item_frame|leash_knot|llama|llama_spit|magma_cube|minecart|mooshroom|mule|ocelot|painting|pig|player|polar_bear|potion|rabbit|sheep|shulker_bullet|silverfish|skeleton|skeleton_horse|slime|small_fireball|snowball|snowman|spawner_minecart|spectral_arrow|spider|squid|stray|tnt|tnt_minecart|vex|villager|villager_golem|vindication_illager|witch|wither|wither_skeleton|wither_skull|wolf|xp_bottle|xp_orb|zombie|zombie_horse|zombie_pigman|zombie_villager";
        int start = 0;
        while (true)
        {
            int index = names.IndexOf(path, start, StringComparison.Ordinal);
            if (index < 0)
                return false;
            int end = index + path.Length;
            if ((index == 0 || names[index - 1] == '|') && (end == names.Length || names[end] == '|'))
                return true;
            start = index + 1;
        }
    }

    // Original bed feet/head eligibility measures !Material.a. In 1.11 Material.c differs only for web's material; neither is the collision-shape BlocksMotion flag.
    public static bool TryGetFluidMaterial(BlockState state, out bool materialA, out bool materialC)
    {
        materialA = materialC = false;
        if (!Umpk.Data.Java.JavaGameData.BedInteractionData(315).TryGetBedExitCell(state.StateId, out BedExitCellInfo cell))
            return false;
        materialA = !cell.AllowsBody;
        materialC = materialA && state.Block.NetworkId != 30;
        return true;
    }

    // Native collision collection and final-body/floor callbacks have independent eligibility; these finite bitmaps bind the original registered 1.11 method owners.
    private static ReadOnlySpan<byte> EmptySquidCollisionBlocks =>
    [
        0x41, 0x0F, 0x00, 0xD8, 0xE1, 0x01, 0x8C, 0x88,
        0x74, 0x39, 0x08, 0x04, 0x00, 0x07, 0x08, 0x00,
        0x18, 0xE0, 0x18, 0x20, 0x00, 0x80, 0x03, 0x00,
        0x00, 0x80, 0x02, 0x02, 0x00, 0x00, 0x00, 0x00,
    ];

    public static bool HasEmptySquidCollisionContract(BlockState state) => MatchesContract(EmptySquidCollisionBlocks, state);

    private static ReadOnlySpan<byte> UnitSquidCollisionBlocks =>
    [
        0xBE, 0xF0, 0xFF, 0x03, 0x08, 0xE6, 0x13, 0x67,
        0x00, 0x86, 0xD5, 0x8A, 0x9E, 0x40, 0x01, 0x1A,
        0x22, 0x06, 0x00, 0xCB, 0x66, 0x77, 0x08, 0x00,
        0x00, 0x47, 0xFC, 0x05, 0x00, 0x00, 0x00, 0x80,
    ];

    public static bool HasUnitSquidCollisionContract(BlockState state) => MatchesContract(UnitSquidCollisionBlocks, state);

    private static ReadOnlySpan<byte> NoopSquidInsideBlocks =>
    [
        0xFF, 0xFF, 0xFF, 0xAF, 0xFF, 0xBF, 0xFF, 0xFF,
        0xBF, 0xDE, 0xFD, 0xFA, 0xFF, 0xFF, 0x3F, 0xFF,
        0xEF, 0x7F, 0xE7, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x00, 0x80,
    ];

    public static bool HasNoopSquidInsideContract(BlockState state) => MatchesContract(NoopSquidInsideBlocks, state);

    private static ReadOnlySpan<byte> BaseSquidLandedBlocks =>
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xDF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x00, 0x80,
    ];

    public static bool HasBaseSquidLandedContract(BlockState state) => MatchesContract(BaseSquidLandedBlocks, state);

    private static ReadOnlySpan<byte> BaseSquidFallenUponBlocks =>
    [
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xEF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xDF, 0xFB, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x07, 0x00, 0x80,
    ];

    public static bool HasBaseSquidFallenUponContract(BlockState state) => MatchesContract(BaseSquidFallenUponBlocks, state);

    private static ReadOnlySpan<byte> AirFloorFallbackBlocks =>
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x20, 0x00, 0x00, 0x08, 0x02, 0x00,
        0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x80, 0xFF,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    public static bool UsesAirFloorFallback(BlockState state) => MatchesContract(AirFloorFallbackBlocks, state);

    private static bool MatchesContract(ReadOnlySpan<byte> contract, BlockState state)
    {
        if (!state.IsValid)
            return false;
        int id = state.Block.NetworkId;
        return (uint)id < 256 && (contract[id >> 3] & (1 << (id & 7))) != 0;
    }

    public static bool TryGetFixedSquidCollisionBox(BlockState state, out Aabb box)
    {
        box = default;
        if (!state.IsValid)
            return false;
        // Native arq omits its fixed lily box only for boats; xg Squid always collects it. Native aqr supplies the fixed soul-sand box independently of its inside callback.
        box = state.Block.NetworkId switch
        {
            111 => new(.0625, 0, .0625, .9375, .09375, .9375),
            88 => new(0, 0, 0, 1, .875, 1),
            _ => default,
        };
        return state.Block.NetworkId is 88 or 111;
    }

    public static bool IsWater(BlockState state) => state.Block.NetworkId is 8 or 9;
    public static bool IsIceMaterial(BlockState state) => state.Block.NetworkId is 79 or 212;

    public static bool TryGet(Entity entity, int protocol, out LegacyAmbientMobProfile profile)
    {
        profile = default;
        if (protocol != 315 || !entity.Type.Id.IsMinecraft || entity.Type.Id.Path is not ("bat" or "squid"))
            return false;
        profile = new(entity.Type.Id.Path == "bat");
        return true;
    }
}

/// <summary>A native cached box whose position setter is independent of the actor's later direct position assignments.</summary>
internal sealed class TrackedLegacyAmbientGeometry(Entity entity, LegacyAmbientMobProfile profile)
{
    private VanillaEntityBounds _bounds = VanillaEntityBounds.Create(entity.Position, profile.Width, profile.Height);
    public LegacyAmbientMobProfile Profile { get; } = profile;
    public bool Known { get; set; } = Finite(entity.Position);
    public Aabb Bounds => _bounds.Box;

    public void ObservePosition(Entity entity)
    {
        _bounds = _bounds.AtPosition(entity.Position);
        Known = Finite(entity.Position);
    }

    public void ObserveMovement(Entity entity, Aabb bounds)
    {
        _bounds = new(entity.Position, Profile.Width, Profile.Height, bounds);
    }

    private static bool Finite(Vec3d position)
        => double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Z)
            && position.X is > int.MinValue and < int.MaxValue && position.Y is > int.MinValue and < int.MaxValue && position.Z is > int.MinValue and < int.MaxValue;
}
