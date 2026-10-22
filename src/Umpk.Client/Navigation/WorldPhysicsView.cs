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
        return type.Path switch
        {

            "allay" or "area_effect_cloud" or "armadillo" or "armor_stand" or "arrow" or "axolotl" or "bat" or "bee" or "blaze" or "block_display" or "bogged" or "breeze" or "breeze_wind_charge" or "camel" or "cat" or "cave_spider" or "chest_minecart" or "chicken" or "cod" or "command_block_minecart" or "cow" or "creaking" or "creeper" or "dolphin" or "donkey" or "dragon_fireball" or "drowned" or "egg" or "elder_guardian" or "end_crystal" or "ender_dragon" or "ender_pearl" or "enderman" or "endermite" or "evoker" or "evoker_fangs" or "experience_bottle" or "experience_orb" or "eye_of_ender" or "falling_block" or "fireball" or "firework_rocket" or "fishing_bobber" or "fox" or "frog" or "furnace_minecart" or "ghast" or "giant" or "glow_item_frame" or "glow_squid" or "goat" or "guardian" or "hoglin" or "hopper_minecart" or "horse" or "husk" or "illusioner" or "interaction" or "iron_golem" or "item" or "item_display" or "item_frame" or "leash_knot" or "lightning_bolt" or "lingering_potion" or "llama" or "llama_spit" or "magma_cube" or "marker" or "minecart" or "mooshroom" or "mule" or "ocelot" or "ominous_item_spawner" or "painting" or "panda" or "parrot" or "phantom" or "pig" or "piglin" or "piglin_brute" or "pillager" or "player" or "polar_bear" or "pufferfish" or "rabbit" or "ravager" or "salmon" or "sheep" or "shulker_bullet" or "silverfish" or "skeleton" or "skeleton_horse" or "slime" or "small_fireball" or "sniffer" or "snow_golem" or "snowball" or "spawner_minecart" or "spectral_arrow" or "spider" or "splash_potion" or "squid" or "stray" or "strider" or "tadpole" or "text_display" or "tnt" or "tnt_minecart" or "trader_llama" or "trident" or "tropical_fish" or "turtle" or "vex" or "villager" or "vindicator" or "wandering_trader" or "warden" or "wind_charge" or "witch" or "wither" or "wither_skeleton" or "wither_skull" or "wolf" or "zoglin" or "zombie" or "zombie_horse" or "zombie_villager" or "zombified_piglin" => true,
            "copper_golem" or "mannequin" => protocol >= 773,
            "camel_husk" or "nautilus" or "parched" or "zombie_nautilus" => protocol >= 774,
            "sulfur_cube" => protocol >= 776,
            "cushion" => protocol >= 777,
            _ => false,
        };
    }
}
