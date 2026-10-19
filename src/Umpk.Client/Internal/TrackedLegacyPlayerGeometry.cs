using Umpk.Client.Actions;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Owns the legacy player's cached box and explicit lifecycle callbacks on the session loop.</summary>
internal sealed class TrackedLegacyPlayerGeometry(Entity entity, int protocol)
{
    private readonly VanillaLegacyPlayerGeometry _geometry = new(protocol, entity.Position);
    private readonly LegacyPlayerGeometryProfile _profile = LegacyPlayerGeometryProfile.ForProtocol(protocol);
    private BlockPos? _bed;
    private bool _positionKnown = true;
    private bool _dimensionsKnown = true;
    private bool _metadataKnown = true;
    private bool _lifecycleKnown = true;
    private bool _queryKnown;
    private LegacyPlayerResizeClearance? _clearance;
    private Func<Aabb, bool>? _noCollision;

    public bool IsPositionKnown => _positionKnown;

    public void Tick(LegacyPlayerResizeClearance? clearance)
    {
        if (!_lifecycleKnown || !_metadataKnown)
        {
            _dimensionsKnown = false;
            return;
        }
        _clearance = _positionKnown ? clearance : null;
        _queryKnown = true;
        _dimensionsKnown = _geometry.TryApplySizePhase(_noCollision ??= QueryResize, _dimensionsKnown) && _queryKnown;
    }

    private bool QueryResize(Aabb candidate)
    {
        if (_clearance is not null)
            return _clearance.NoCollision(candidate);
        _queryKnown = false;
        return false;
    }

    public bool TryGet(Entity entity, out Aabb bounds)
    {
        if (_positionKnown && _geometry.Position != entity.Position)
            _geometry.SetPosition(entity.Position);
        bounds = _geometry.Bounds;
        return _positionKnown && _dimensionsKnown && _metadataKnown;
    }

    public void ObserveMetadata(Entity entity, int index)
    {
        if (index != 0 && !(_profile.SwimmingAndSpin && index == 6))
            return;
        bool sharedKnown = !entity.Metadata.TryGet(0, out MetadataValue shared) || shared.Kind == MetadataValueKind.Byte;
        bool spinKnown = !_profile.SwimmingAndSpin || !entity.Metadata.TryGet(6, out MetadataValue spin) || spin.Kind == MetadataValueKind.Byte;
        _metadataKnown = sharedKnown && spinKnown;
        if (!_metadataKnown)
        {
            _dimensionsKnown = _lifecycleKnown = false;
            return;
        }
        byte flags = entity.Metadata.TryGet(0, out shared) ? unchecked((byte)shared.AsByte()) : (byte)0;
        // 1.13.2-client-decompiled afa.java: the synchronized living flags use bit 4 for spin attack.
        bool spinAttack = _profile.SwimmingAndSpin && entity.Metadata.TryGet(6, out spin) && (spin.AsByte() & 4) != 0;
        _geometry.ObserveFlags(flags, spinAttack);
    }

    public void ObservePosition(Entity entity)
    {
        _geometry.SetPosition(entity.Position);
        _positionKnown = true;
    }

    public bool BeginSleep(Entity entity, World? world, IBlockInteractionSource source, BlockPos bed)
    {
        _bed = bed;
        if (world is null)
        {
            _positionKnown = _dimensionsKnown = _lifecycleKnown = false;
            return false;
        }
        if (_profile.SkipsBedRead(bed))
            _geometry.BeginSleepWithoutBedRead(bed);
        else
        {
            if (world.GetColumn(bed) is null || !source.TryGetBedFacing(world.GetBlockStateId(bed), out Direction facing))
            {
                _positionKnown = _dimensionsKnown = _lifecycleKnown = false;
                return false;
            }
            _geometry.BeginSleep(bed, facing);
        }
        entity.Position = _geometry.Position;
        _positionKnown = _dimensionsKnown = _lifecycleKnown = true;
        return true;
    }

    public void Wake(Entity entity, World? world, IBlockInteractionSource source)
    {
        if (_bed is not { } bed || world is null)
        {
            _positionKnown = _dimensionsKnown = _lifecycleKnown = false;
            return;
        }
        if (_profile.SkipsBedRead(bed))
        {
            _geometry.WakeAfterBedRemoval();
            _dimensionsKnown = _lifecycleKnown = true;
            _bed = null;
            return;
        }
        if (world.GetColumn(bed) is null)
        {
            _positionKnown = _dimensionsKnown = _lifecycleKnown = false;
            return;
        }
        int state = world.GetBlockStateId(bed);
        if (source.TryGetBedFacing(state, out Direction facing))
        {
            if (!TryClearOccupied(world.GetBlock(bed), out int unoccupied))
            {
                _positionKnown = _dimensionsKnown = _lifecycleKnown = false;
                return;
            }
            world.SetBlockStateId(bed, unoccupied);
            var query = new LegacyBedExitQuery(protocol, source, cell => world.GetColumn(cell) is null ? -1 : world.GetBlockStateId(cell));
            _positionKnown = _geometry.TryWakeFromIntactBed(bed, facing, query.TryCanStand, out _);
            _dimensionsKnown = _lifecycleKnown = true;
            if (_positionKnown)
                entity.Position = _geometry.Position;
            else
                _geometry.WakeAfterBedRemoval();
        }
        else
        {
            var current = world.GetBlock(bed);
            if (!current.IsValid || current.Block.Id.Path is "bed" || current.Block.Id.Path.EndsWith("_bed", StringComparison.Ordinal))
            {
                _positionKnown = _dimensionsKnown = _lifecycleKnown = false;
                return;
            }
            _geometry.WakeAfterBedRemoval();
            _dimensionsKnown = _lifecycleKnown = true;
        }
        _bed = null;
    }

    private static bool TryClearOccupied(BlockState state, out int result)
    {
        result = 0;
        if (state.LegacyMeta >= 0)
        {
            // Original world writes across all 27 releases preserve facing/part and normalize raw head aliases when clearing occupancy.
            result = state.StateId & ~4;
            return true;
        }
        BlockDefinition block = state.Block.Value;
        for (int candidate = block.MinStateId; candidate <= block.MaxStateId; candidate++)
        {
            if (!block.TryGetPropertyValue(candidate, "occupied", out string occupied) || occupied != "false")
                continue;
            bool matches = true;
            foreach (BlockPropertyDefinition property in block.Properties)
            {
                if (property.Name == "occupied")
                    continue;
                if (!block.TryGetPropertyValue(state.StateId, property.Name, out string before)
                    || !block.TryGetPropertyValue(candidate, property.Name, out string after)
                    || !string.Equals(before, after, StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                result = candidate;
                return true;
            }
        }
        return false;
    }

    public void HandleDeath()
    {
        _geometry.HandleDeath();
        _dimensionsKnown = true;
    }
}
