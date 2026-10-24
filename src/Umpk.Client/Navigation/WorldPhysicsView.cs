using Umpk.Client.Internal;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Client.Navigation;

/// <summary>Adapts the tracked <see cref="Umpk.Game.World.World"/> to <see cref="IPhysicsWorldView"/> for the local-player physics engine. Collision shapes come from an <see cref="IBlockShapeSource"/>: the version's generated shape tables by default, or whatever the host injected. Entity colliders are collected only when an owned source actor supplies the native query context.</summary>
internal sealed class WorldPhysicsView(Umpk.Game.World.World world, IBlockShapeSource shapes, WorldBorderContainmentEra borderEra, BlockCollisionQueryEra collisionEra = BlockCollisionQueryEra.BoundingBoxes, EntityCollisionContext? entityContext = null) : IPhysicsWorldView
{
    private readonly Umpk.Game.World.World _world = world;
    private readonly IBlockShapeSource _shapes = shapes;
    private readonly WorldBorderContainmentEra _borderEra = borderEra;

    public BlockCollisionQueryEra CollisionQueryEra { get; } = collisionEra;

    public BlockCollisionGrid? GetCollisionGrid(BlockState state) => (_shapes as IBlockCollisionGridSource)?.GetCollisionGrid(state);

    public BlockState GetBlock(BlockPos pos)
    {
        if (entityContext is { } context)
        {
            // Native collision chunk lookup skips unloaded columns. Storage synthesized by a block update does not make that chunk available.
            if (!context.Membership.HasColumn(pos))
                return new BlockState(_world.BlockData, 0);
            if (ModernHardCollisionProfile.Supports(context.Protocol) && _world.GetColumn(pos) is null)
                throw new UnknownEntityCollisionContextException();
        }
        BlockState state = _world.GetBlock(pos);
        if (entityContext is { } modern && ModernHardCollisionProfile.Supports(modern.Protocol) && !state.IsValid)
            throw new UnknownEntityCollisionContextException();
        return state;
    }

    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => _shapes.GetCollisionShapes(state);

    public bool IsChunkLoaded(BlockPos pos) => entityContext is { } context ? context.Membership.HasColumn(pos) : _world.GetColumn(pos) is not null;

    public bool CollidesWithBorder(in Aabb box, Vec3d sourcePosition) =>
        _world.Border.CollidesWith(box, sourcePosition, _borderEra);

    public void CollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
        if (!TryCollectEntityColliders(region, into))
            throw new UnknownEntityCollisionContextException();
    }

    /// <summary>Retains unowned hard boxes as unknown. A proven placement or picking box is not automatically a native movement collider.</summary>
    internal bool TryCollectEntityColliders(in Aabb region, ICollection<Aabb> into)
    {
        if (entityContext is not { } context)
            return true;
        if (ModernHardCollisionProfile.Supports(context.Protocol))
            return TryCollectModern(context, region, into);
        if (!LegacyFallingBlockProfile.Supports(context.Source, context.Protocol))
            return false;
        foreach (Entity peer in context.Entities.All)
        {
            if (ReferenceEquals(peer, context.Source) || context.Motion.IsTrackedPlayer(peer) || LegacyFallingBlockProfile.HasNoHardPeerBox(peer.Type.Id, context.Protocol))
                continue;
            bool? member = context.Membership.IsWorldQueryMember(peer);
            if (member == false)
                continue;
            if (member is null)
                return false;
            if (SharedRoot(context.Source, peer))
                continue;
            // The legacy boat and 1.9 Shulker peer boxes need their own owned moving geometry. Their missing bounds cannot prove that a distant-looking packet position is irrelevant.
            return false;
        }
        return true;
    }

    private static bool TryCollectModern(EntityCollisionContext context, in Aabb region, ICollection<Aabb> into)
    {
        if ((!context.Motion.IsTrackedPlayer(context.Source) && !context.Motion.IsTrackedCowSource(context.Source)) || context.Geometry is null || context.GeometrySource is null)
            return false;
        // EntityGetter checks the mean size before resolving any candidates, then inflates its candidate query by exactly one epsilon.
        if ((region.XSize + region.YSize + region.ZSize) / 3.0 < 1.0E-7)
            return true;
        Aabb candidates = region.Inflate(1.0E-7);
        foreach (Entity peer in context.Entities.All)
        {
            if (ReferenceEquals(peer, context.Source) || ModernHardCollisionProfile.HasNoHardPeerBox(peer.Type.Id, context.Protocol) || SharedRoot(context.Source, peer))
                continue;
            bool? member = context.Membership.IsWorldQueryMember(peer);
            if (member == false)
                continue;
            if (member is null || peer.Type.Id != Identifier.Minecraft("shulker"))
                return false;
            // Native Shulker.canBeCollidedWith uses isAlive, independent of pushability, noPhysics and placement obstruction. Its constructor health is positive; malformed received health is unknown.
            if (peer.Metadata.TryGet(9, out MetadataValue rawHealth))
            {
                if (rawHealth.Kind != MetadataValueKind.Float)
                    return false;
                if (!(rawHealth.AsFloat() > 0))
                    continue;
            }
            if (peer.Vehicle is not null || !context.Geometry.TryGet(peer, context.GeometrySource, out Aabb box, out _, context.Protocol))
                return false;
            if (candidates.Intersects(box))
                into.Add(box);
        }
        return true;
    }

    private static bool SharedRoot(Entity first, Entity second)
    {
        int leftDepth = 0;
        for (Entity? left = first; left is not null && leftDepth++ < 64; left = left.Vehicle)
        {
            int rightDepth = 0;
            for (Entity? right = second; right is not null && rightDepth++ < 64; right = right.Vehicle)
                if (ReferenceEquals(left, right))
                    return true;
        }
        return false;
    }
}

/// <summary>The source actor and tracked native membership needed for a movement collision query.</summary>
internal sealed record EntityCollisionContext(Entity Source, EntityStore Entities, EntityMembershipState Membership, EntityMotionState Motion, int Protocol, EntityGeometryState? Geometry = null, IEntityGeometrySource? GeometrySource = null);

/// <summary>Signals an unowned collision fact to the serialized motion caller without substituting a clear or blocked answer.</summary>
internal sealed class UnknownEntityCollisionContextException() : InvalidOperationException("The source entity's collision context is unknown.");

/// <summary>Complete original-class inheritance audit for ordinary source actors across the measured modern datasets.</summary>
internal static class ModernHardCollisionProfile
{
    public static bool Supports(int protocol) => protocol is >= 770 and <= 777;

    public static bool HasNoHardPeerBox(Identifier type, int protocol)
    {
        if (type.Namespace != "minecraft")
            return false;
        if (IsCommonNonHardTarget(type.Path))
            return true;
        return type.Path switch
        {

            "copper_golem" or "mannequin" => protocol >= 773,
            "camel_husk" or "nautilus" or "parched" or "zombie_nautilus" => protocol >= 774,
            "sulfur_cube" => protocol >= 776,
            "cushion" => protocol >= 777,
            _ => false,
        };
    }

    private static bool IsCommonNonHardTarget(string path)
    {
        if (path.Length == 0)
            return false;
        const string names = "allay|area_effect_cloud|armadillo|armor_stand|arrow|axolotl|bat|bee|blaze|block_display|bogged|breeze|breeze_wind_charge|camel|cat|cave_spider|chest_minecart|chicken|cod|command_block_minecart|cow|creaking|creeper|dolphin|donkey|dragon_fireball|drowned|egg|elder_guardian|end_crystal|ender_dragon|ender_pearl|enderman|endermite|evoker|evoker_fangs|experience_bottle|experience_orb|eye_of_ender|falling_block|fireball|firework_rocket|fishing_bobber|fox|frog|furnace_minecart|ghast|giant|glow_item_frame|glow_squid|goat|guardian|hoglin|hopper_minecart|horse|husk|illusioner|interaction|iron_golem|item|item_display|item_frame|leash_knot|lightning_bolt|lingering_potion|llama|llama_spit|magma_cube|marker|minecart|mooshroom|mule|ocelot|ominous_item_spawner|painting|panda|parrot|phantom|pig|piglin|piglin_brute|pillager|player|polar_bear|pufferfish|rabbit|ravager|salmon|sheep|shulker_bullet|silverfish|skeleton|skeleton_horse|slime|small_fireball|sniffer|snow_golem|snowball|spawner_minecart|spectral_arrow|spider|splash_potion|squid|stray|strider|tadpole|text_display|tnt|tnt_minecart|trader_llama|trident|tropical_fish|turtle|vex|villager|vindicator|wandering_trader|warden|wind_charge|witch|wither|wither_skeleton|wither_skull|wolf|zoglin|zombie|zombie_horse|zombie_villager|zombified_piglin";
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
}
