using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Client.Navigation;

/// <summary>A fallback <see cref="IBlockShapeSource"/> that derives a full unit cube for motion-blocking, non-fluid states and no shape otherwise.</summary>
/// <remarks>This is no longer the session default: a client with no injected shape source now uses the version's generated shape tables (<c>Umpk.Data.Java.JavaGameData.BlockShapes</c>), which fall back to exactly this rule per state the dataset does not cover. It stays as the standalone expression of that rule, used where a test wants coarse cube terrain and nothing else.</remarks>
internal sealed class FlagsBlockShapes : IBlockShapeSource
{
    private static readonly Aabb[] Cube = [new(0, 0, 0, 1, 1, 1)];
    private static readonly Aabb[] Empty = [];

    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state)
        => state.BlocksMotion && !state.IsFluid ? Cube : Empty;

    public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => stateId == 0 ? Empty : Cube;

    public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => GetCollisionShapes(state);

    public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => GetCollisionShapes(stateId);
}
