using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Actions;

public sealed partial class InteractionActions
{
    private BlockUsePrediction PredictEndCrystalUse(VanillaEndCrystalUse profile, ItemStack item, BlockState clicked, World world, BlockPos position, IBlockInteractionSource data)
    {
        if (!profile.SupportsStack(item))
            return BlockUsePrediction.Unknown;
        if (_services.Self.GameMode == GameMode.Adventure)
            // Native old item checks player-edit at above; 1.13+ ItemStack checks its clicked-cell Adventure predicate before calling the item. Default End Crystals have no permission predicate.
            return item.Components.HasNoPatch ? profile.TwoReplaceableCells ? BlockUsePrediction.Fail : BlockUsePrediction.Pass : BlockUsePrediction.Unknown;
        if (!clicked.Block.Id.IsMinecraft || clicked.Block.Id.Path is not ("obsidian" or "bedrock"))
            return BlockUsePrediction.Fail;
        bool? above = CellAllowsCrystal(position.Above());
        bool? upper = profile.TwoReplaceableCells ? CellAllowsCrystal(position.Above().Above()) : true;
        if (above is false || upper is false)
            return BlockUsePrediction.Fail;
        if (above is null || upper is null)
            return BlockUsePrediction.Unknown;
        var query = new Aabb(position.X, (double)position.Y + 1, position.Z, (double)position.X + 1, (double)position.Y + 3, (double)position.Z + 1);
        return PredictCrystalEntityQuery(profile, query);

        bool? CellAllowsCrystal(BlockPos cell)
        {
            // Native world reads outside build height return air/void air; this item does not add a placement-height rejection.
            if (cell.Y < world.Dimension.MinY || cell.Y >= world.Dimension.MaxY)
                return true;
            if (!_services.State.EntityMembership.HasColumn(cell) || world.GetColumn(cell) is null)
                return null;
            BlockState state = _services.State.JukeboxPrediction.GetBlock(world, cell);
            if (!state.IsValid)
                return null;
            if (state.IsAir)
                return true;
            if (!profile.TwoReplaceableCells)
                return false;
            BlockState below = _services.State.JukeboxPrediction.GetBlock(world, cell.Below());
            // The retained legacy table flag 1 measures this exact Block.isReplaceable call. Flag 2 also contains ordinary-cube survival and is deliberately unused.
            return data.TryGetCubeReplacement(state.StateId, below.IsValid ? below.StateId : null, out BlockReplacementInfo replacement) ? replacement.ReplacesClickedBlock : null;
        }
    }

    private BlockUsePrediction PredictCrystalEntityQuery(VanillaEndCrystalUse profile, in Aabb query)
    {
        if (!_services.State.Features.Entities)
            return BlockUsePrediction.Unknown;
        bool unknown = _services.State.EntityMotion.IsLocalMountUnresolved;
        if (!unknown && _services.ReadPhysicsState() is { } self && VanillaEndCrystalUse.HasFiniteBounds(self.BoundingBox))
        {
            if (query.Intersects(self.BoundingBox))
            {
                if (profile.SectionStorage && VanillaEndCrystalUse.TryGetSection(self.Position, out int x, out int y, out int z))
                {
                    if (profile.IncludesSection(query, x, y, z))
                        return BlockUsePrediction.Fail;
                }
                else
                    // Remote-player native membership does not establish the real legacy local player's chunk-list registration.
                    unknown = true;
            }
        }
        else
            unknown = true;
        foreach (Entity entity in _services.State.Entities.All)
        {
            if (entity.Id == _services.Self.EntityId || _services.State.EntityMotion.IsKnownSpectator(entity, _services.State.TabList))
                continue;
            bool? member = _services.State.EntityMembership.IsWorldQueryMember(entity);
            if (member is false)
                continue;
            if (!_services.State.EntityMotion.IsRiderGeometrySupported(entity) || !_services.State.EntityGeometry.IsPositionKnown(entity)
                || !_services.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(profile.Protocol), out Aabb bounds, out _, profile.Protocol)
                || !VanillaEndCrystalUse.HasFiniteBounds(bounds))
            {
                unknown = true;
                continue;
            }
            if (!query.Intersects(bounds))
                continue;
            if (member is null)
            {
                unknown = true;
                continue;
            }
            bool visited;
            if (profile.SectionStorage)
            {
                if (!VanillaEndCrystalUse.TryGetSection(entity.Position, out int x, out int y, out int z))
                {
                    unknown = true;
                    continue;
                }
                visited = profile.IncludesSection(query, x, y, z);
            }
            else if (_services.State.EntityMembership.TryGetLegacyMembership(entity, out EntityMembershipState.LegacyMembershipSnapshot membership)
                && membership.RegistrationKnown && membership.ActualSection is int section)
                visited = profile.IncludesSection(query, membership.X, section, membership.Z);
            else
            {
                unknown = true;
                continue;
            }
            // Original getEntities(null, box) uses only the default non-spectator predicate after index/box membership. Permanently nonbuilding, noPhysics and shared-vehicle actors are not excluded.
            if (visited && !_services.State.EntityMotion.IsSpectator(entity, _services.State.TabList))
                return BlockUsePrediction.Fail;
        }
        return unknown ? BlockUsePrediction.Unknown : BlockUsePrediction.Success(_blockProfile.UseSwingIsLocal);
    }
}
