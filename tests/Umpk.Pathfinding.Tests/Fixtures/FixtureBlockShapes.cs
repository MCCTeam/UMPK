using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;

namespace Umpk.Pathfinding.Tests.Fixtures;

/// <summary>Fixture data for <c>FixtureBlockShapes</c>.</summary>
public sealed class FixtureBlockShapes : IBlockShapeSource
{
    private static readonly Aabb[] Cube = [new(0, 0, 0, 1, 1, 1)];
    private static readonly Aabb[] Empty = [];
    private static readonly Aabb[] BottomSlabShape = [new(0, 0, 0, 1, 0.5, 1)];
    private static readonly Aabb[] TopSlabShape = [new(0, 0.5, 0, 1, 1, 1)];
    private static readonly Aabb[] SnowLayerShape = [new(0, 0, 0, 1, 0.375, 1)];
    private static readonly Aabb[] StairsBottomShape =
    [
        new(0, 0, 0, 1, 0.5, 1),
        new(0, 0.5, 0, 1, 1, 0.5),
    ];

    private static readonly Aabb[] FenceShape = [new(0.375, 0, 0.375, 0.625, 1.5, 0.625)];

    /// <summary>Fixture data for <c>LadderWestShape</c>.</summary>
    private static readonly Aabb[] LadderWestShape = [new(0.8125, 0, 0, 1, 1, 1)];

    /// <summary>Fixture data for <c>CarpetShape</c>.</summary>
    private static readonly Aabb[] CarpetShape = [new(0, 0, 0, 1, 0.0625, 1)];

    /// <summary><c>minecraft:honey_block</c>, shape 135: inset 1/16 on each side, so no full cover.</summary>
    private static readonly Aabb[] HoneyShape = [new(0.0625, 0, 0.0625, 0.9375, 0.9375, 0.9375)];

    /// <summary><c>minecraft:lily_pad</c>, shape 271: the same inset at 3/32 high.</summary>
    private static readonly Aabb[] LilyPadShape = [new(0.0625, 0, 0.0625, 0.9375, 0.09375, 0.9375)];

    /// <summary><c>minecraft:pointed_dripstone[vertical_direction=up,thickness=tip]</c> uses a 6/16 column topping at 11/16 = 0.6875. That top IS a floor a body rests on, which the live probes measured (<c>Segment 3/6 (Descend) ... at (1040.5068, 97.6875, 1.5)</c>).</summary>
    private static readonly Aabb[] DripstoneTipUpShape = [new(0.3125, 0, 0.3125, 0.6875, 0.6875, 0.6875)];

    /// <summary>Fixture data for <c>DripstoneTipDownShape</c>.</summary>
    private static readonly Aabb[] DripstoneTipDownShape = [new(0.3125, 0.3125, 0.3125, 0.6875, 1, 0.6875)];

    /// <summary>Fixture data for <c>DripstoneBaseShape</c>.</summary>
    private static readonly Aabb[] DripstoneBaseShape = [new(0.25, 0, 0.25, 0.75, 1, 0.75)];

    /// <summary>Fixture data for <c>BambooShape</c>.</summary>
    private static readonly Aabb[] BambooShape = [new(0.40625, 0, 0.40625, 0.59375, 1, 0.59375)];

    /// <summary><c>minecraft:campfire</c>, shape 143: a full-footprint 7/16 slab.</summary>
    private static readonly Aabb[] CampfireShape = [new(0, 0, 0, 1, 0.4375, 1)];

    /// <summary><c>minecraft:cauldron</c>, shape 144 verbatim: eight corner posts and two side walls reaching 1.0, over a 3/16-to-1/4 inner floor. The concave case - a centred 0.6-wide footprint misses every wall and rests on the floor at 0.25.</summary>
    private static readonly Aabb[] CauldronShape =
    [
        new(0, 0, 0, 0.125, 1, 0.25),
        new(0, 0, 0.75, 0.125, 1, 1),
        new(0.125, 0, 0, 0.25, 1, 0.125),
        new(0.125, 0, 0.875, 0.25, 1, 1),
        new(0.75, 0, 0, 1, 1, 0.125),
        new(0.75, 0, 0.875, 1, 1, 1),
        new(0.875, 0, 0.125, 1, 1, 0.25),
        new(0.875, 0, 0.75, 1, 1, 0.875),
        new(0, 0.1875, 0.25, 1, 0.25, 0.75),
        new(0.125, 0.1875, 0.125, 0.875, 0.25, 0.25),
        new(0.125, 0.1875, 0.75, 0.875, 0.25, 0.875),
        new(0.25, 0.1875, 0, 0.75, 1, 0.125),
        new(0.25, 0.1875, 0.875, 0.75, 1, 1),
        new(0, 0.25, 0.25, 0.125, 1, 0.75),
        new(0.875, 0.25, 0.25, 1, 1, 0.75),
    ];

    /// <summary><c>minecraft:oak_fence_gate[open=false]</c>, shape 11: a panel across the cell topping at 1.5, so it protrudes into the head cell exactly as a fence post does.</summary>
    private static readonly Aabb[] ClosedFenceGateShape = [new(0, 0, 0.375, 1, 1.5, 0.625)];

    /// <summary><c>minecraft:soul_sand</c>, shape 278.</summary>
    private static readonly Aabb[] SoulSandShape = [new(0, 0, 0, 1, 0.875, 1)];

    /// <summary><c>minecraft:dirt_path</c>, shape 242.</summary>
    private static readonly Aabb[] DirtPathShape = [new(0, 0, 0, 1, 0.9375, 1)];

    /// <summary><c>minecraft:snow[layers=2]</c>, shape 226.</summary>
    private static readonly Aabb[] SnowLayerTwoShape = [new(0, 0, 0, 1, 0.125, 1)];

    /// <summary><c>minecraft:snow[layers=5]</c>, shape 27 (the same box a bottom slab carries).</summary>
    private static readonly Aabb[] SnowLayerFiveShape = [new(0, 0, 0, 1, 0.5, 1)];

    /// <summary><c>minecraft:oak_stairs[half=top,facing=north]</c>, shape 28: both boxes top at 1.0 and together tile the footprint, so an upside-down stair is a flat 1.0 floor and a full block from below.</summary>
    private static readonly Aabb[] StairsTopShape = [new(0, 0, 0, 1, 1, 0.5), new(0, 0.5, 0.5, 1, 1, 1)];

    /// <summary>Fixture data for <c>OpenPanelNorthShape</c>.</summary>
    private static readonly Aabb[] OpenPanelNorthShape = [new(0, 0, 0, 1, 1, 0.1875)];

    /// <summary>Fixture data for <c>ClosedDoorWestShape</c>.</summary>
    private static readonly Aabb[] ClosedDoorWestShape = [new(0, 0, 0, 0.1875, 1, 1)];

    /// <summary>Fixture data for <c>OpenPanelWestShape</c>.</summary>
    private static readonly Aabb[] OpenPanelWestShape = [new(0, 0, 0, 0.1875, 1, 1)];

    /// <summary>A closed bottom trapdoor: a 3/16 slab at the cell's floor, which is a floor, not a barrier.</summary>
    private static readonly Aabb[] ClosedTrapdoorBottomShape = [new(0, 0, 0, 1, 0.1875, 1)];

    /// <summary>Fixture data for <c>ClosedTrapdoorTopShape</c>.</summary>
    private static readonly Aabb[] ClosedTrapdoorTopShape = [new(0, 0.8125, 0, 1, 1, 1)];

    /// <summary><c>minecraft:cactus</c>, inset 1/16 horizontally and 1/16 below the top.</summary>
    private static readonly Aabb[] CactusShape = [new(0.0625, 0, 0.0625, 0.9375, 0.9375, 0.9375)];

    /// <summary>Fixture data for <c>ScaffoldingShape</c>.</summary>
    private static readonly Aabb[] ScaffoldingShape =
    [
        new(0, 0, 0, 0.125, 1, 0.125),
        new(0, 0, 0.875, 0.125, 1, 1),
        new(0.875, 0, 0, 1, 1, 0.125),
        new(0.875, 0, 0.875, 1, 1, 1),
        new(0, 0.875, 0.125, 1, 1, 0.875),
        new(0.125, 0.875, 0, 0.875, 1, 0.125),
        new(0.125, 0.875, 0.875, 0.875, 1, 1),
    ];

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state) => ShapesFor(state.StateId);

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId) => ShapesFor(stateId);

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => ShapesFor(state.StateId);

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => ShapesFor(stateId);

    private static ReadOnlySpan<Aabb> ShapesFor(int stateId) => stateId switch
    {
        FixtureWorld.Stone or FixtureWorld.MagmaBlock or FixtureWorld.SlimeBlock or FixtureWorld.HayBlock => Cube,
        FixtureWorld.BottomSlab => BottomSlabShape,
        FixtureWorld.TopSlab => TopSlabShape,
        FixtureWorld.SnowLayer => SnowLayerShape,
        FixtureWorld.StairsBottom or FixtureWorld.WaterloggedStairs => StairsBottomShape,
        FixtureWorld.Fence or FixtureWorld.WaterloggedFence => FenceShape,
        FixtureWorld.LadderWest => LadderWestShape,
        FixtureWorld.Carpet => CarpetShape,
        FixtureWorld.Honey => HoneyShape,
        FixtureWorld.LilyPad => LilyPadShape,
        FixtureWorld.Campfire => CampfireShape,
        FixtureWorld.Cauldron => CauldronShape,
        FixtureWorld.ClosedFenceGate => ClosedFenceGateShape,
        FixtureWorld.SoulSand => SoulSandShape,
        FixtureWorld.DirtPath => DirtPathShape,
        FixtureWorld.SnowLayerTwo => SnowLayerTwoShape,
        FixtureWorld.SnowLayerFive => SnowLayerFiveShape,
        FixtureWorld.StairsTop => StairsTopShape,
        FixtureWorld.OpenDoorNorth or FixtureWorld.OpenTrapdoorNorth or FixtureWorld.UnreadableDoor
            => OpenPanelNorthShape,
        FixtureWorld.OpenTrapdoorBottomWest or FixtureWorld.IronTrapdoorOpenWest => OpenPanelWestShape,
        FixtureWorld.ClosedDoorWest or FixtureWorld.IronDoorClosed => ClosedDoorWestShape,
        FixtureWorld.ClosedTrapdoorBottom => ClosedTrapdoorBottomShape,
        FixtureWorld.ClosedTrapdoorTop or FixtureWorld.IronTrapdoorClosedTop => ClosedTrapdoorTopShape,
        FixtureWorld.Cactus => CactusShape,
        FixtureWorld.DripstoneTipUp => DripstoneTipUpShape,
        FixtureWorld.DripstoneTipDown => DripstoneTipDownShape,
        FixtureWorld.DripstoneBaseUp => DripstoneBaseShape,
        FixtureWorld.Bamboo => BambooShape,
        FixtureWorld.Scaffolding => ScaffoldingShape,

        // FixtureWorld.Fire and FixtureWorld.SoulFire are deliberately absent: both carry the EMPTY

        // FixtureWorld.SnowLayerOne is deliberately absent: snow[layers=1]'s collision ref is shape 0, the EMPTY shape, and a support of exactly zero is what MoveHelper.CanWalkOn's `> 0.0` bound exists to refuse.
        _ => Empty,
    };
}
