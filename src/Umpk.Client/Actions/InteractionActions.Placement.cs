using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Actions;

public sealed partial class InteractionActions
{
    private async ValueTask<int?> SendBlockUseOnLoopAsync(BlockPos position, Direction face, Vec3d cursor, Hand hand, bool insideBlock, CancellationToken ct, CancellationToken session)
    {
        ct.ThrowIfCancellationRequested();
        if (_services.State.HasWorld && !_services.State.World.Border.IsWithinBounds(position.X, position.Z, WorldBorderState.ContainmentEraForProtocol(_services.Version.Version.Protocol)))
            return null;
        bool swing = !_blockProfile.UseSwingIsLocal && PredictsBlockUseSwing(position, face, hand);
        ct.ThrowIfCancellationRequested();
        // Once the input is committed, finish its packet group while this session lives. Caller cancellation can stop the acknowledgment wait, but cannot omit an animation after an already-written use.
        int? sequence = null;
        if (!_services.Wire.CanSendPlay(ItemPackets.Serverbound.UseItemOn))
            await SendLegacyBlockPlaceAsync(nameof(PlaceBlockAsync), position, (int)face, cursor, session).ConfigureAwait(false);
        else
        {
            sequence = _sequences.Next();
            await _sink.SendAsync(new ServerboundUseItemOnPacket((int)hand, position, (int)face, (float)cursor.X, (float)cursor.Y, (float)cursor.Z, insideBlock, WorldBorderHit: false, sequence.Value), session).ConfigureAwait(false);
        }
        if (swing)
            await SwingAsync(hand, session).ConfigureAwait(false);
        return sequence;
    }

    private bool PredictsBlockUseSwing(BlockPos position, Direction face, Hand hand)
    {
        if (!_services.State.HasWorld)
            return false;
        World world = _services.State.World;
        if (world.GetColumn(position) is null)
            return false;
        BlockState clicked = world.GetBlock(position);
        // Before 1.21.2 the spectator controller returns SUCCESS before item use. Later clients return CONSUME, whose swing source is NONE.
        if (_services.Self.GameMode == GameMode.Spectator)
            return _blockProfile.SpectatorUseSwings && clicked.IsValid && !clicked.IsAir;
        if (!_services.State.Features.Inventory || !_services.State.Features.Entities || _services.Self.GameMode == GameMode.Adventure)
            return false;
        ItemStack item = hand == Hand.Main ? HeldItem() : _services.State.Inventory.GetSlot(45);
        if (item.IsEmpty || !item.Components.HasNoPatch || _services.Self.ItemCooldowns.GetValueOrDefault(item.Item.NetworkId) > 0)
            return false;
        IBlockInteractionSource data = JavaGameData.BlockInteractionData(_services.Version.Version.Protocol);
        if (!data.TryGetItem(item.Item.Id, out ItemUseOnInfo use) || use.Kind != ItemUseOnKind.SimpleCubePlacement)
            return false;
        if (!clicked.IsValid || !data.TryGetBlock(clicked.Block.Id, out BlockInteractionInfo block) || !TryReplacement(position, clicked, out BlockReplacementInfo replacement))
            return false;
        if (!block.PassiveUse && !_services.Self.Sneaking)
            return false;
        BlockPos destination = replacement.ReplacesClickedBlock ? position : position.Offset(face);
        if (destination.Y < world.Dimension.MinY || destination.Y >= world.Dimension.MaxY || world.GetColumn(destination) is null)
            return false;
        if (!replacement.ReplacesClickedBlock)
        {
            BlockState previous = world.GetBlock(destination);
            if (!previous.IsValid || !TryReplacement(destination, previous, out replacement))
                return false;
        }
        if (!replacement.AllowsPlacement)
            return false;
        if (_services.ReadPhysicsState() is not { } physics || physics.BoundingBox.XSize <= 0 || physics.BoundingBox.YSize <= 0)
            return false;
        if (VanillaPlacementCollision.Intersects(destination, physics.BoundingBox, _blockProfile))
            return false;
        foreach (var entity in _services.State.Entities.All)
        {
            if (entity.Id == _services.Self.EntityId || data.IsAlwaysNonObstructingEntity(entity.Type.Id))
                continue;
            if (entity.Vehicle is not null)
                return false;
            Aabb bounds;
            if (data is IEntityGeometrySource geometry && _services.State.EntityGeometry.TryGet(entity, geometry, out bounds, out bool blocksBuilding, _services.Version.Version.Protocol))
            {
                if (!blocksBuilding)
                    continue;
            }
            else if (data.TryGetFixedEntityDimensions(entity.Type.Id, out float width, out float height))
                bounds = VanillaEntityBounds.Create(entity.Position, width, height).Box;
            else
                return false;
            if (VanillaPlacementCollision.Intersects(destination, bounds, _blockProfile))
                return false;
        }
        return true;

        bool TryReplacement(BlockPos cell, BlockState state, out BlockReplacementInfo info)
        {
            BlockState lower = world.GetBlock(cell.Below());
            return data.TryGetCubeReplacement(state.StateId, lower.IsValid ? lower.StateId : null, out info);
        }
    }
}
