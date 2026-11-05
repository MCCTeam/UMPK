using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Internal;

/// <summary>Evaluates original support and material predicates with the legacy client's direct block-read bounds.</summary>
internal sealed class LegacyBedExitQuery
{
    private readonly IBlockInteractionSource _source;
    private readonly Func<BlockPos, int> _getState;
    private readonly bool _boundedHorizontally;

    internal LegacyBedExitQuery(int protocol, IBlockInteractionSource source, Func<BlockPos, int> getState)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(getState);
        _boundedHorizontally = protocol switch
        {
            47 or 107 or 108 or 109 => true,
            110 or 210 or 315 or 316 or 335 or 338 or 340 or 393 or 401 or 404 => false,
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), "The legacy bed exit query requires a measured protocol from 1.8 through 1.13.2."),
        };
        _source = source;
        _getState = getState;
    }

    internal bool TryCanStand(BlockPos cell, out bool canStand)
    {
        canStand = false;
        if (!TryRead(new BlockPos(cell.X, cell.Y - 1, cell.Z), out BedExitCellInfo below))
            return false;
        if (!below.SupportsPlayer)
            return true;
        if (!TryRead(cell, out BedExitCellInfo feet))
            return false;
        if (!feet.AllowsBody)
            return true;
        if (!TryRead(new BlockPos(cell.X, cell.Y + 1, cell.Z), out BedExitCellInfo head))
            return false;
        canStand = head.AllowsBody;
        return true;
    }

    private bool TryRead(BlockPos cell, out BedExitCellInfo info)
    {
        // These are direct getBlockState bounds. The separate resize query has a different horizontal-boundary timeline.
        bool outside = cell.Y is < 0 or >= 256 || (_boundedHorizontally && (cell.X is < -30_000_000 or >= 30_000_000 || cell.Z is < -30_000_000 or >= 30_000_000));
        return _source.TryGetBedExitCell(outside ? 0 : _getState(cell), out info);
    }
}
