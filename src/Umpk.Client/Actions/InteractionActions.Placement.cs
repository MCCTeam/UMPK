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
        RequireBlockUseHand(hand, nameof(PlaceBlockAsync));
        BlockUsePrediction prediction = PredictBlockUse(position, face, hand);
        if (!prediction.SendPacket)
            return null;
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
        if (prediction.Swing == BlockUseSwing.Packet)
            await SwingAsync(hand, session).ConfigureAwait(false);
        return sequence;
    }

    private void RequireBlockUseHand(Hand hand, string action)
    {
        if (hand is not Hand.Main and not Hand.Off)
            throw new ArgumentOutOfRangeException(nameof(hand));
        // 1.8.9-client-decompiled bda.java:245 sends ja with the selected inventory stack; ja.java:19 has no hand field. The 1.9 use_item_on binding is the first form that can carry an offhand.
        Require(hand == Hand.Main || _services.Wire.CanSendPlay(ItemPackets.Serverbound.UseItemOn), action,
            ItemPackets.Serverbound.UseItemOn.Id, "This version cannot use the offhand; the legacy packet always uses the main hand.");
    }

    private BlockUsePrediction PredictBlockUse(BlockPos position, Direction face, Hand hand)
    {
        if (!_services.State.HasWorld)
            return BlockUsePrediction.Unknown;
        World world = _services.State.World;
        if (!world.Border.IsWithinBounds(position.X, position.Z, WorldBorderState.ContainmentEraForProtocol(_services.Version.Version.Protocol)))
            return BlockUsePrediction.Rejected;
        if (world.GetColumn(position) is null)
            return BlockUsePrediction.Unknown;
        BlockState clicked = world.GetBlock(position);
        // Before 1.21.2 the spectator controller returns SUCCESS before item use. Later clients return CONSUME, whose swing source is NONE.
        if (_services.Self.GameMode == GameMode.Spectator)
            return !clicked.IsValid || clicked.IsAir ? BlockUsePrediction.Unknown
                : _blockProfile.SpectatorUseSwings ? BlockUsePrediction.Success(_blockProfile.UseSwingIsLocal) : BlockUsePrediction.Consume;
        IBlockInteractionSource data = JavaGameData.BlockInteractionData(_services.Version.Version.Protocol);
        if (!clicked.IsValid || !data.TryGetBlock(clicked.Block.Id, out BlockInteractionInfo block))
            return BlockUsePrediction.Unknown;
        bool inventoryKnown = _services.State.Features.Inventory;
        if (_services.Self.Sneaking && !inventoryKnown)
            return BlockUsePrediction.Unknown;
        ItemStack main = HeldItem();
        ItemStack off = inventoryKnown && _services.Wire.CanSendPlay(ItemPackets.Serverbound.UseItemOn) ? _services.State.Inventory.GetSlot(45) : ItemStack.Empty;
        ItemStack item = hand == Hand.Main ? main : off;
        // 1.14.4 and 26.3 MultiPlayerGameMode.useItemOn/performUseItemOn suppress block activation only when secondary use has a nonempty stack in either hand. Activation runs before item cooldown and placement permissions.
        bool suppressActivation = _services.Self.Sneaking && (!main.IsEmpty || !off.IsEmpty);
        BlockUseActivationProfile activation = BlockUseActivationProfile.ForProtocol(_services.Version.Version.Protocol);
        bool hasActivation = activation.TryPredict(clicked, out BlockUseResult activationResult);
        bool activationPassesOffhand = hasActivation && activation.EmptyHandActivationIsMainHandOnly && hand == Hand.Off;
        if (!suppressActivation && hasActivation && !activationPassesOffhand)
            return activationResult switch
            {
                BlockUseResult.Success => BlockUsePrediction.Success(_blockProfile.UseSwingIsLocal),
                BlockUseResult.Consume => BlockUsePrediction.Consume,
                _ => BlockUsePrediction.Unknown,
            };
        if (!block.PassiveUse && !suppressActivation && !activationPassesOffhand)
            return BlockUsePrediction.Unknown;
        if (!inventoryKnown)
            return BlockUsePrediction.Unknown;
        if (item.IsEmpty || _services.Self.IsItemOnCooldown(item, _services.Version.Version.Protocol))
            return BlockUsePrediction.Pass;
        if (_services.Self.GameMode == GameMode.Adventure)
            return BlockUsePrediction.Unknown;
        VanillaHoeUse hoe = VanillaHoeUse.ForProtocol(_services.Version.Version.Protocol);
        if (hoe.TryPredict(item.Item.Id, clicked, world, position, face, out BlockUsePrediction toolUse))
            // A block update can synthesize a storage column before native chunk arrival or after unload. It does not establish the terrain needed by the tool.
            return hoe.SupportsStack(item) && _services.State.EntityMembership.HasColumn(position) ? toolUse : BlockUsePrediction.Unknown;
        VanillaShovelUse shovel = VanillaShovelUse.ForProtocol(_services.Version.Version.Protocol);
        if (shovel.TryPredict(item.Item.Id, clicked, world, position, face, out BlockUsePrediction shovelUse))
            return shovel.SupportsStack(item) && _services.State.EntityMembership.HasColumn(position) ? shovelUse : BlockUsePrediction.Unknown;
        if (!item.Components.HasNoPatch)
            return BlockUsePrediction.Unknown;
        if (!data.TryGetItem(item.Item.Id, out ItemUseOnInfo use))
            return BlockUsePrediction.Unknown;
        if (use.Kind == ItemUseOnKind.Pass)
            return BlockUsePrediction.Pass;
        if (use.Kind != ItemUseOnKind.SimpleCubePlacement || !_services.State.Features.Entities)
            return BlockUsePrediction.Unknown;
        if (!TryReplacement(position, clicked, out BlockReplacementInfo replacement))
            return BlockUsePrediction.Unknown;
        BlockPos destination = replacement.ReplacesClickedBlock ? position : position.Offset(face);
        if (destination.Y < world.Dimension.MinY || destination.Y >= world.Dimension.MaxY)
            return BlockUsePrediction.Fail;
        if (world.GetColumn(destination) is null)
            return BlockUsePrediction.Unknown;
        if (!replacement.ReplacesClickedBlock)
        {
            BlockState previous = world.GetBlock(destination);
            if (!previous.IsValid || !TryReplacement(destination, previous, out replacement))
                return BlockUsePrediction.Unknown;
        }
        if (!replacement.AllowsPlacement)
            return BlockUsePrediction.Fail;
        if (_services.ReadPhysicsState() is not { } physics || physics.BoundingBox.XSize <= 0 || physics.BoundingBox.YSize <= 0)
            return BlockUsePrediction.Unknown;
        if (VanillaPlacementCollision.Intersects(destination, physics.BoundingBox, _blockProfile))
            return BlockUsePrediction.Fail;
        foreach (var entity in _services.State.Entities.All)
        {
            if (entity.Id == _services.Self.EntityId || data.IsAlwaysNonObstructingEntity(entity.Type.Id))
                continue;
            bool? member = _services.State.EntityMembership.IsWorldQueryMember(entity);
            if (member is false)
                continue;
            if (member is null)
                return BlockUsePrediction.Unknown;
            if (entity.Vehicle is not null)
                return BlockUsePrediction.Unknown;
            Aabb bounds;
            if (data is IEntityGeometrySource geometry && _services.State.EntityGeometry.TryGet(entity, geometry, out bounds, out bool blocksBuilding, _services.Version.Version.Protocol))
            {
                if (!blocksBuilding)
                    continue;
            }
            else if (data.TryGetFixedEntityDimensions(entity.Type.Id, out float width, out float height))
                bounds = VanillaEntityBounds.Create(entity.Position, width, height).Box;
            else
                return BlockUsePrediction.Unknown;
            if (Aabb.BlockAt(destination.X, destination.Y, destination.Z).Intersects(bounds)
                && !_services.State.EntityMotion.IsSpectator(entity, _services.State.TabList)
                && VanillaPlacementCollision.Intersects(destination, bounds, _blockProfile))
                return BlockUsePrediction.Fail;
        }
        return BlockUsePrediction.Success(_blockProfile.UseSwingIsLocal);

        bool TryReplacement(BlockPos cell, BlockState state, out BlockReplacementInfo info)
        {
            BlockState lower = world.GetBlock(cell.Below());
            return data.TryGetCubeReplacement(state.StateId, lower.IsValid ? lower.StateId : null, out info);
        }
    }
}
