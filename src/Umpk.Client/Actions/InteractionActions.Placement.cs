using Umpk.Client.Internal;
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
        BlockUsePrediction prediction = PredictBlockUse(position, face, cursor, hand);
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

    internal BlockUsePrediction PredictBlockUse(BlockPos position, Direction face, Vec3d cursor, Hand hand)
    {
        bool inventoryKnown = _services.State.Features.Inventory;
        ItemStack main = HeldItem();
        ItemStack off = inventoryKnown && _services.Wire.CanSendPlay(ItemPackets.Serverbound.UseItemOn) ? _services.State.Inventory.GetSlot(45) : ItemStack.Empty;
        ItemStack item = hand == Hand.Main ? main : off;
        // Minecraft.startUseItem rejects disabled nonempty stacks before useItemOn, independently of terrain and spectator mode. Without inventory, only a completely classified and enabled native catalog can establish permission.
        bool? itemEnabled = inventoryKnown
            ? item.IsEmpty ? true : _services.State.EnabledFeatures.IsItemEnabled(item.Item.Id, _services.Version.Version.Protocol)
            : _services.State.EnabledFeatures.IsUnobservedItemEnabled(_services.State.Registries, _services.Version.Version.Protocol);
        if (itemEnabled is false)
            return BlockUsePrediction.Rejected;
        if (!_services.State.HasWorld)
            return BlockUsePrediction.Unknown;
        World world = _services.State.World;
        if (!world.Border.IsWithinBounds(position.X, position.Z, WorldBorderState.ContainmentEraForProtocol(_services.Version.Version.Protocol)))
            return BlockUsePrediction.Rejected;
        if (world.GetColumn(position) is null)
            return BlockUsePrediction.Unknown;
        BlockState clicked = world.GetBlock(position);
        if (itemEnabled is null)
            return BlockUsePrediction.Unknown;
        // Before 1.21.2 the spectator controller returns SUCCESS before item use. Later clients return CONSUME, whose swing source is NONE.
        if (_services.Self.GameMode == GameMode.Spectator)
            return !clicked.IsValid || clicked.IsAir ? BlockUsePrediction.Unknown
                : _blockProfile.SpectatorUseSwings ? BlockUsePrediction.Success(_blockProfile.UseSwingIsLocal) : BlockUsePrediction.Consume;
        IBlockInteractionSource data = JavaGameData.BlockInteractionData(_services.Version.Version.Protocol);
        if (!clicked.IsValid || !data.TryGetBlock(clicked.Block.Id, out BlockInteractionInfo block))
            return BlockUsePrediction.Unknown;
        // 1.14.4 and 26.3 MultiPlayerGameMode.useItemOn/performUseItemOn suppress block activation only when secondary use has a nonempty stack in either hand. Activation runs before item cooldown and placement permissions.
        bool suppressActivation = _services.Self.Sneaking && (!main.IsEmpty || !off.IsEmpty);
        // performUseItemOn fails on a disabled target before activation, but still commits the use packet. Native secondary use suppresses this entire target/activation phase.
        if (_services.Self.Sneaking && !inventoryKnown)
            return BlockUsePrediction.Unknown;
        if (!suppressActivation)
        {
            bool? blockEnabled = _services.State.EnabledFeatures.IsBlockEnabled(clicked.Block.Id, _services.Version.Version.Protocol);
            if (blockEnabled is not true)
                return blockEnabled is false ? BlockUsePrediction.Fail : BlockUsePrediction.Unknown;
        }
        BlockUseActivationProfile activation = BlockUseActivationProfile.ForProtocol(_services.Version.Version.Protocol);
        BlockState lower = activation.RequiresLowerCell(clicked) && position.Y > world.Dimension.MinY ? world.GetBlock(position.Below()) : default;
        bool hasActivation = activation.TryPredict(clicked, out BlockUseResult activationResult, lower);
        // Cached block updates do not establish the native terrain required by supported activation families or their item continuation.
        if (hasActivation && activation.RequiresNativeTerrain(clicked) && !_services.State.EntityMembership.HasColumn(position))
            return BlockUsePrediction.Unknown;
        bool activationPassesOffhand = hasActivation && activation.EmptyHandActivationIsMainHandOnly && hand == Hand.Off;
        if (!suppressActivation && hasActivation && !activationPassesOffhand)
        {
            if (activationResult != BlockUseResult.Pass)
                return activationResult switch
                {
                    BlockUseResult.Success => BlockUsePrediction.Success(_blockProfile.UseSwingIsLocal),
                    BlockUseResult.Consume => BlockUsePrediction.Consume,
                    _ => BlockUsePrediction.Unknown,
                };
        }
        if (!block.PassiveUse && !suppressActivation && !activationPassesOffhand && !(hasActivation && activationResult == BlockUseResult.Pass))
            return BlockUsePrediction.Unknown;
        if (!inventoryKnown)
            return BlockUsePrediction.Unknown;
        // The legacy controller checks ItemBlock placement before sending and before cooldown/Adventure item use. Only default ordinary cube items have their complete precheck represented here; specialized items keep their own result and unknown context.
        if (_blockProfile.PrechecksBlockItemPlacement && !item.IsEmpty && item.Components.HasNoPatch
            && data.TryGetItem(item.Item.Id, out ItemUseOnInfo legacyUse) && legacyUse.Kind == ItemUseOnKind.SimpleCubePlacement)
        {
            BlockUsePrediction precheck = PredictCubePlacement(preSend: true);
            if (precheck.Result != BlockUseResult.Success)
                return precheck;
        }
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
        VanillaAxeUse axe = VanillaAxeUse.ForProtocol(_services.Version.Version.Protocol);
        if (axe.TryPredict(item.Item.Id, clicked, hand, off, _services.Self.Sneaking, out BlockUsePrediction axeUse))
            return axe.SupportsStack(item) && _services.State.EntityMembership.HasColumn(position) ? axeUse : BlockUsePrediction.Unknown;
        VanillaIgnitionUse ignition = VanillaIgnitionUse.ForProtocol(_services.Version.Version.Protocol);
        if (ignition.Supports(item.Item.Id))
            return ignition.SupportsStack(item) && _services.State.EntityMembership.HasColumn(position)
                ? ignition.Predict(clicked,
                    _services.State.RegistryTags.Contains(RegistryIds.Block, Identifier.Minecraft("campfires"), clicked.Block.NetworkId),
                    _services.State.RegistryTags.Contains(RegistryIds.Block, Identifier.Minecraft("candles"), clicked.Block.NetworkId))
                : BlockUsePrediction.Unknown;
        if (!item.Components.HasNoPatch)
            return BlockUsePrediction.Unknown;
        if (!data.TryGetItem(item.Item.Id, out ItemUseOnInfo use))
            return BlockUsePrediction.Unknown;
        if (use.Kind == ItemUseOnKind.Pass)
            return BlockUsePrediction.Pass;
        VanillaSlabPlacement slab = VanillaSlabPlacement.ForProtocol(_services.Version.Version.Protocol);
        if (slab.Supports(item.Item.Id, use))
            return PredictSlabPlacement(slab, item.Item.Id);
        if (use.Kind != ItemUseOnKind.SimpleCubePlacement || !_services.State.Features.Entities)
            return BlockUsePrediction.Unknown;
        return PredictCubePlacement(preSend: false);

        BlockUsePrediction PredictCubePlacement(bool preSend)
        {
            if (!_services.State.Features.Entities || preSend && !_services.State.EntityMembership.HasColumn(position))
                return BlockUsePrediction.Unknown;
            if (!TryReplacement(position, clicked, out BlockReplacementInfo replacement))
                return BlockUsePrediction.Unknown;
            // Legacy ItemBlock.canPlaceBlockOnSide keeps snow layers at the clicked cell and changes only its queried face. ItemBlock.onItemUse separately resolves its original face/destination, so a successful precheck must not substitute this cell for actual placement.
            bool replacesClicked = replacement.ReplacesClickedBlock || preSend && clicked.Block.Id.Path == "snow_layer";
            BlockPos destination = replacesClicked ? position : position.Offset(face);
            // An out-of-height state/write is not a proven false ItemBlock precheck. Its original world/permission/write phases differ; retain packet-only uncertainty rather than turning a later failure into a pre-send rejection.
            if (destination.Y < world.Dimension.MinY || destination.Y >= world.Dimension.MaxY)
                return preSend ? BlockUsePrediction.Unknown : BlockUsePrediction.Fail;
            if (world.GetColumn(destination) is null || preSend && !_services.State.EntityMembership.HasColumn(destination))
                return BlockUsePrediction.Unknown;
            if (!replacesClicked)
            {
                BlockState previous = world.GetBlock(destination);
                if (!previous.IsValid || !TryReplacement(destination, previous, out replacement))
                    return BlockUsePrediction.Unknown;
            }
            if (!replacement.AllowsPlacement)
                return preSend ? BlockUsePrediction.Rejected : BlockUsePrediction.Fail;
            return PredictPlacementObstruction(destination, preSend, [new(0, 0, 0, 1, 1, 1)]);
        }

        BlockUsePrediction PredictSlabPlacement(VanillaSlabPlacement slab, Identifier slabId)
        {
            if (!_services.State.Features.Entities || !_services.State.EntityMembership.HasColumn(position))
                return BlockUsePrediction.Unknown;
            bool replacesClicked;
            if (clicked.Block.Id == slabId)
            {
                if (!VanillaSlabPlacement.TryGetType(clicked, out string clickedType))
                    return BlockUsePrediction.Unknown;
                replacesClicked = VanillaSlabPlacement.ReplacesClicked(clickedType, face, slab.AboveHalf(position, cursor));
            }
            else
            {
                if (!TryReplacement(position, clicked, out BlockReplacementInfo clickedReplacement))
                    return BlockUsePrediction.Unknown;
                replacesClicked = clickedReplacement.ReplacesClickedBlock;
            }
            BlockPos destination = replacesClicked ? position : position.Offset(face);
            if (destination.Y < world.Dimension.MinY || destination.Y >= world.Dimension.MaxY
                || world.GetColumn(destination) is null || !_services.State.EntityMembership.HasColumn(destination))
                return BlockUsePrediction.Unknown;
            BlockState previous = world.GetBlock(destination);
            if (!previous.IsValid)
                return BlockUsePrediction.Unknown;
            string type;
            if (previous.Block.Id == slabId)
            {
                if (!VanillaSlabPlacement.TryGetType(previous, out string previousType))
                    return BlockUsePrediction.Unknown;
                if (previousType == "double")
                    return BlockUsePrediction.Fail;
                type = "double";
            }
            else
            {
                if (!TryReplacement(destination, previous, out BlockReplacementInfo replacement))
                    return BlockUsePrediction.Unknown;
                if (!replacement.AllowsPlacement)
                    return BlockUsePrediction.Fail;
                type = face == Direction.Down || face != Direction.Up && slab.AboveHalf(position, cursor) ? "top" : "bottom";
            }
            if (!VanillaSlabPlacement.TryGetCollisionShape(previous.Source, slabId, type, JavaGameData.BlockShapes(_services.Version.Version.Protocol), out Aabb shape))
                return BlockUsePrediction.Unknown;
            return PredictPlacementObstruction(destination, preSend: false, [shape]);
        }

        BlockUsePrediction PredictPlacementObstruction(BlockPos destination, bool preSend, ReadOnlySpan<Aabb> shapes)
        {
            if (_services.State.EntityMotion.IsLocalMountUnresolved || _services.ReadPhysicsState() is not { } physics || physics.BoundingBox.XSize <= 0 || physics.BoundingBox.YSize <= 0)
                return BlockUsePrediction.Unknown;
            if (VanillaPlacementCollision.Intersects(destination, shapes, physics.BoundingBox, _blockProfile))
                return preSend ? BlockUsePrediction.Rejected : BlockUsePrediction.Fail;
            foreach (var entity in _services.State.Entities.All)
            {
                if (entity.Id == _services.Self.EntityId || data.IsAlwaysNonObstructingEntity(entity.Type.Id)
                    || _services.State.EntityMotion.IsKnownSpectator(entity, _services.State.TabList))
                    continue;
                bool? member = _services.State.EntityMembership.IsWorldQueryMember(entity);
                if (member is false)
                    continue;
                if (member is null)
                    return BlockUsePrediction.Unknown;
                if (!_services.State.EntityMotion.IsRiderGeometrySupported(entity) || !_services.State.EntityGeometry.IsPositionKnown(entity))
                    return BlockUsePrediction.Unknown;
                Aabb bounds;
                if (data is IEntityGeometrySource geometry && _services.State.EntityGeometry.TryGet(entity, geometry, out bounds, out bool blocksBuilding, _services.Version.Version.Protocol))
                {
                    if (!blocksBuilding)
                        continue;
                }
                else if (EntityGeometryState.IsFixedCrystal(entity, _services.Version.Version.Protocol))
                    return BlockUsePrediction.Unknown;
                else if (data.TryGetFixedEntityDimensions(entity.Type.Id, out float width, out float height))
                    bounds = VanillaEntityBounds.Create(entity.Position, width, height).Box;
                else
                    return BlockUsePrediction.Unknown;
                if (Aabb.BlockAt(destination.X, destination.Y, destination.Z).Intersects(bounds)
                    && !_services.State.EntityMotion.IsSpectator(entity, _services.State.TabList)
                    && VanillaPlacementCollision.Intersects(destination, shapes, bounds, _blockProfile))
                    return preSend ? BlockUsePrediction.Rejected : BlockUsePrediction.Fail;
            }
            return BlockUsePrediction.Success(_blockProfile.UseSwingIsLocal);
        }

        bool TryReplacement(BlockPos cell, BlockState state, out BlockReplacementInfo info)
        {
            BlockState lower = world.GetBlock(cell.Below());
            return data.TryGetCubeReplacement(state.StateId, lower.IsValid ? lower.StateId : null, out info);
        }
    }
}
