using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

internal sealed partial class EntityGeometryState
{
    internal void TickShulkerPush(Entity source, ClientState state, IBlockShapeSource shapes, int protocol, List<int> moved, Aabb? localBody)
    {
        World? world = state.WorldOrNull;
        if (world is null || !_shulkers.TryGetValue(source, out TrackedShulkerGeometry? shulker)
            || !state.EntityMembership.HasColumn(BlockPos.Containing(source.Position)) || world.GetColumn(BlockPos.Containing(source.Position)) is null)
            return;
        if (source.Vehicle is not null || !shulker.TryGet(source, out Aabb before) || !ShulkerPushProfile.Finite(before))
        {
            state.EntityMotion.InvalidateUnownedShulkerPush(state);
            return;
        }
        shulker.Tick(source);
        if (shulker.TryOpeningPush(source, out Aabb region, out Vec3d requested))
            state.EntityMotion.ApplyShulkerPush(source, state, shapes, protocol, region, requested, moved, localBody);
    }
}
