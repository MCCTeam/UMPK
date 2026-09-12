using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Game.Tests.World;

/// <summary>A test <see cref="IBlockShapeSource"/> that gives every non-air state a single half-height box (either the upper or lower half of the unit cube). Air states get no shapes. Used to prove the raycast honors sub-cube outline shapes rather than treating every block as a full cube.</summary>
internal sealed class HalfBlockShapeSource : IBlockShapeSource
{
    private readonly IBlockDataSource _data;
    private readonly Aabb[] _box;

    public HalfBlockShapeSource(IBlockDataSource data, bool upperHalf)
    {
        _data = data;
        _box = upperHalf
            ? [new Aabb(0, 0.5, 0, 1, 1, 1)]
            : [new Aabb(0, 0, 0, 1, 0.5, 1)];
    }

    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => GetOutlineShapes(state);

    public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => GetOutlineShapes(stateId);

    public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => GetOutlineShapes(state.StateId);

    public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) =>
        _data.GetState(stateId).IsAir ? ReadOnlySpan<Aabb>.Empty : _box;
}
