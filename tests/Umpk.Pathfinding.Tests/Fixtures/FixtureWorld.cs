using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Pathfinding;

namespace Umpk.Pathfinding.Tests.Fixtures;

/// <summary>A hand-built voxel world for planning tests. Wraps a real <see cref="Umpk.Game.World.World"/> (so <see cref="Umpk.Game.World.World.CopyRegion"/> produces a genuine immutable <c>RegionSnapshot</c>) over a permissive block table with air / stone / water / ladder kinds, plus a set of partial-height collision shapes (bottom/top slab, snow layer, a bottom-half stair, a fence post) and a solid hazard block for <see cref="Umpk.Pathfinding.PathfinderOptions.AllowPartialHeightSupport"/> coverage. Captures a <see cref="PlanningWorldView"/> on demand. No generated data is used (documented per the brief).</summary>
public sealed class FixtureWorld
{
    /// <summary>Air state id.</summary>
    public const int Air = 0;

    /// <summary>Solid stone state id.</summary>
    public const int Stone = 1;

    /// <summary>Water fluid state id.</summary>
    public const int Water = 2;

    /// <summary>Ladder (climbable) state id.</summary>
    public const int Ladder = 3;

    /// <summary>A WATERLOGGED ladder: climbable and water at once, empty shape. The control for every rule that keys on "the body is hanging", because in water it is swimming instead.</summary>
    public const int WaterloggedLadder = FixtureBlockData.WaterloggedLadderState;

    /// <summary>Bottom slab state id: collision <c>[[0,0,0,1,0.5,1]]</c>, full-cover support at 0.5.</summary>
    public const int BottomSlab = 4;

    /// <summary>Top slab state id: collision <c>[[0,0.5,0,1,1,1]]</c>, full-cover support at 1.0.</summary>
    public const int TopSlab = 5;

    /// <summary>Snow-layer state id: collision <c>[[0,0,0,1,0.375,1]]</c>, full-cover support at 0.375.</summary>
    public const int SnowLayer = 6;

    /// <summary>Fixture data for <c>StairsBottom</c>.</summary>
    public const int StairsBottom = 7;

    /// <summary>Fence-post state id: collision <c>[[0.375,0,0.375,0.625,1.5,0.625]]</c>, never full-cover.</summary>
    public const int Fence = 8;

    /// <summary>Solid, motion-blocking, curated-hazard (<c>minecraft:magma_block</c>) state id.</summary>
    public const int MagmaBlock = 9;

    /// <summary>Lava (<c>minecraft:lava</c>): a fluid, and a curated hazard. Collision shape empty.</summary>
    public const int Lava = 10;

    /// <summary>Fixture data for <c>PowderSnow</c>.</summary>
    public const int PowderSnow = 11;

    /// <summary>Flowing water (<c>minecraft:flowing_water</c>): a block of its own on every pre-flattening dataset, and a fluid, but not <c>minecraft:water</c>.</summary>
    public const int FlowingWater = 12;

    /// <summary>A waterlogged fence post: <c>BlocksMotion</c> and <c>Waterlogged</c> at once, shape as <see cref="Fence"/>.</summary>
    public const int WaterloggedFence = 13;

    /// <summary>A waterlogged bottom-half stair: <c>BlocksMotion</c> and <c>Waterlogged</c> at once, shape as <see cref="StairsBottom"/>.</summary>
    public const int WaterloggedStairs = 14;

    /// <summary><c>minecraft:ladder[facing=west]</c>: climbable exactly as <see cref="Ladder"/> is, but carrying the 3/16 collision plate vanilla gives a ladder against the wall it hangs on. <see cref="Ladder"/> has no shape at all, so a shaft built out of it is 3/16 wider than the real one and lets a body rest where the real shaft cannot hold it.</summary>
    public const int LadderWest = FixtureBlockData.LadderWestState;

    /// <summary>White carpet: collision <c>[[0,0,0,1,0.0625,1]]</c>, the thinnest floor in the game.</summary>
    public const int Carpet = FixtureBlockData.CarpetState;

    /// <summary>Honey block: collision <c>[[0.0625,0,0.0625,0.9375,0.9375,0.9375]]</c>. Inset 1/16 on each side, so it has NO full-cover plane and a centred 0.6-wide body stands on it at 0.9375.</summary>
    public const int Honey = FixtureBlockData.HoneyState;

    /// <summary>Lily pad: the same inset at <c>0.09375</c> high, and equally standable.</summary>
    public const int LilyPad = FixtureBlockData.LilyPadState;

    /// <summary><c>minecraft:snow[layers=1]</c>: the EMPTY collision shape. A pass-through, not a floor, and the case <see cref="Umpk.Pathfinding.Moves.MoveHelper.CanWalkOn"/>'s lower bound exists for.</summary>
    public const int SnowLayerOne = FixtureBlockData.SnowLayerOneState;

    /// <summary>Campfire: collision <c>[[0,0,0,1,0.4375,1]]</c>, a full-footprint 7/16 slab.</summary>
    public const int Campfire = FixtureBlockData.CampfireState;

    /// <summary>Cauldron: 1.0-high walls around a 0.25 inner floor, the concave standable case.</summary>
    public const int Cauldron = FixtureBlockData.CauldronState;

    /// <summary>Fixture data for <c>ClosedFenceGate</c>.</summary>
    public const int ClosedFenceGate = FixtureBlockData.ClosedFenceGateState;

    /// <summary>Soul sand: collision <c>[[0,0,0,1,0.875,1]]</c>.</summary>
    public const int SoulSand = FixtureBlockData.SoulSandState;

    /// <summary><c>minecraft:pointed_dripstone[vertical_direction=up,thickness=tip]</c>, the stalagmite tip: a 6/16 column topping at 0.6875, which is a floor, and the one state whose landing behavior adds 2.5 to the fall distance and doubles the multiplier.</summary>
    public const int DripstoneTipUp = FixtureBlockData.DripstoneTipUpState;

    /// <summary><c>...[vertical_direction=down,thickness=tip]</c>, a stalactite. Ordinary landing.</summary>
    public const int DripstoneTipDown = FixtureBlockData.DripstoneTipDownState;

    /// <summary><c>...[vertical_direction=up,thickness=base]</c>, a stalagmite shaft. Ordinary landing.</summary>
    public const int DripstoneBaseUp = FixtureBlockData.DripstoneBaseUpState;

    /// <summary>Fixture data for <c>Bamboo</c>.</summary>
    public const int Bamboo = FixtureBlockData.BambooState;

    /// <summary>Dirt path: collision <c>[[0,0,0,1,0.9375,1]]</c>, the shallowest partial support there is.</summary>
    public const int DirtPath = FixtureBlockData.DirtPathState;

    /// <summary>Snow at <c>layers=2</c>: collision <c>[[0,0,0,1,0.125,1]]</c>.</summary>
    public const int SnowLayerTwo = FixtureBlockData.SnowLayerTwoState;

    /// <summary>Snow at <c>layers=5</c>: collision <c>[[0,0,0,1,0.5,1]]</c>, a slab's height in snow.</summary>
    public const int SnowLayerFive = FixtureBlockData.SnowLayerFiveState;

    /// <summary>An upside-down stair: both boxes top at 1.0, so it is a flat floor and a full block from below.</summary>
    public const int StairsTop = FixtureBlockData.StairsTopState;

    /// <summary>An OPEN oak door: the 3/16 panel rotated onto the cell's north (low-Z) face. Still <c>BlocksMotion</c> - opening a door rotates its box rather than removing it - which is exactly why <see cref="Umpk.Pathfinding.Moves.MoveHelper.CanWalkThrough"/> cannot decide this family from the flag.</summary>
    public const int OpenDoorNorth = FixtureBlockData.OpenDoorNorthState;

    /// <summary>A CLOSED oak door: the same panel across the crossing axis.</summary>
    public const int ClosedDoorWest = FixtureBlockData.ClosedDoorWestState;

    /// <summary>An OPEN oak trapdoor: the same vertical panel a door's open state carries.</summary>
    public const int OpenTrapdoorNorth = FixtureBlockData.OpenTrapdoorNorthState;

    /// <summary>An OPEN oak trapdoor with <c>half=bottom</c>, panel on the cell's WEST face: the one barrier state a plan crosses by CLOSING it, because <c>half=bottom</c> folds into a 3/16 floor slab. Course row G5b.</summary>
    public const int OpenTrapdoorBottomWest = FixtureBlockData.OpenTrapdoorBottomWestState;

    /// <summary>A CLOSED bottom oak trapdoor: a 3/16 floor slab, not a barrier.</summary>
    public const int ClosedTrapdoorBottom = FixtureBlockData.ClosedTrapdoorBottomState;

    /// <summary>A CLOSED TOP oak trapdoor: a 3/16 slab at the cell's CEILING, i.e. a lid flush with the floor of the cell above. Course rows G5a and L5 are built on it.</summary>
    public const int ClosedTrapdoorTop = FixtureBlockData.ClosedTrapdoorTopState;

    /// <summary>Fixture data for <c>IronDoorClosed</c>.</summary>
    public const int IronDoorClosed = FixtureBlockData.IronDoorClosedState;

    /// <summary>An OPEN iron trapdoor, panel on the cell's WEST face: course row L5's lid after its button has been pressed.</summary>
    public const int IronTrapdoorOpenWest = FixtureBlockData.IronTrapdoorOpenWestState;

    /// <summary>A CLOSED TOP iron trapdoor: <see cref="ClosedTrapdoorTop"/>'s shape, and no hand opens it.</summary>
    public const int IronTrapdoorClosedTop = FixtureBlockData.IronTrapdoorClosedTopState;

    /// <summary><c>minecraft:stone_button[face=wall,facing=south]</c>: no collision, and a <b>20</b>-tick press. Attached to the block on its NORTH side, which is where its direct signal goes.</summary>
    public const int StoneButtonWallSouth = FixtureBlockData.StoneButtonWallSouthState;

    /// <summary><c>minecraft:oak_button[face=wall,facing=south]</c>: the same, at <b>30</b> ticks.</summary>
    public const int OakButtonWallSouth = FixtureBlockData.OakButtonWallSouthState;

    /// <summary><c>minecraft:lever[face=wall,facing=south]</c>: a latch, so no window at all.</summary>
    public const int LeverWallSouth = FixtureBlockData.LeverWallSouthState;

    /// <summary><c>minecraft:oak_button[face=wall,facing=east]</c>, attached on its WEST side.</summary>
    public const int OakButtonWallEast = FixtureBlockData.OakButtonWallEastState;

    /// <summary><c>minecraft:redstone_wire</c>: no collision, and no signal model behind it.</summary>
    public const int RedstoneWire = FixtureBlockData.RedstoneWireState;

    /// <summary>Fixture data for <c>OakPressurePlate</c>.</summary>
    public const int OakPressurePlate = FixtureBlockData.OakPressurePlateState;

    /// <summary>Fixture data for <c>StonePressurePlate</c>.</summary>
    public const int StonePressurePlate = FixtureBlockData.StonePressurePlateState;

    /// <summary>Fixture data for <c>PolishedBlackstonePressurePlate</c>.</summary>
    public const int PolishedBlackstonePressurePlate =
        FixtureBlockData.PolishedBlackstonePressurePlateState;

    /// <summary>Fixture data for <c>LightWeightedPressurePlate</c>.</summary>
    public const int LightWeightedPressurePlate = FixtureBlockData.LightWeightedPressurePlateState;

    /// <summary><c>minecraft:heavy_weighted_pressure_plate</c>, the iron one: the same 10 ticks.</summary>
    public const int HeavyWeightedPressurePlate = FixtureBlockData.HeavyWeightedPressurePlateState;

    /// <summary>A plate whose <c>powered</c> property no source can resolve: the pre-flattening bands, where the name still says <c>oak_pressure_plate</c> and nothing else can be read.</summary>
    public const int UnreadablePressurePlate = FixtureBlockData.UnreadablePressurePlateState;

    /// <summary>Fixture data for <c>OpenFenceGate</c>.</summary>
    public const int OpenFenceGate = FixtureBlockData.OpenFenceGateState;

    /// <summary>A door whose <c>open</c> property no source can resolve, i.e. the pre-flattening bands.</summary>
    public const int UnreadableDoor = FixtureBlockData.UnreadableDoorState;

    /// <summary>Fixture data for <c>Fire</c>.</summary>
    public const int Fire = FixtureBlockData.FireState;

    /// <summary><c>minecraft:soul_fire</c>: the same empty shape, and also in <c>IS_FIRE</c>.</summary>
    public const int SoulFire = FixtureBlockData.SoulFireState;

    /// <summary><c>minecraft:cactus</c>: a hazard whose damage type is NOT in <c>IS_FIRE</c>, so fire resistance does nothing for it. The polarity control.</summary>
    public const int Cactus = FixtureBlockData.CactusState;

    /// <summary>Fixture data for <c>Scaffolding</c>.</summary>
    public const int Scaffolding = FixtureBlockData.ScaffoldingState;

    /// <summary>Fixture data for <c>Cobweb</c> for course F8.</summary>
    public const int Cobweb = FixtureBlockData.CobwebState;

    /// <summary><c>minecraft:slime_block</c>: a full cube that absorbs a fall entirely (multiplier 0.0) and bounces a body that does not sneak. Course rows N1, C8-slime, C8b.</summary>
    public const int SlimeBlock = FixtureBlockData.SlimeBlockState;

    /// <summary><c>minecraft:hay_block</c>: a full cube that scales a fall by 0.2 and does not bounce. Course rows N2 and C8-hay.</summary>
    public const int HayBlock = FixtureBlockData.HayBlockState;

    /// <summary><c>minecraft:bubble_column[drag=false]</c>: an UPWARD column, the state a soul-sand base makes. Water with no collision shape, plus the lift. Course row E12.</summary>
    public const int BubbleColumnUp = FixtureBlockData.BubbleColumnUpState;

    /// <summary><c>minecraft:bubble_column[drag=true]</c>: a DOWNWARD column, from a magma base. Water, and deliberately no modelled downdraft - course row E13 stays deferred.</summary>
    public const int BubbleColumnDown = FixtureBlockData.BubbleColumnDownState;

    /// <summary>Fixture data for <c>WaterAtLevel</c>.</summary>
    public static int WaterAtLevel(int level) => level switch
    {
        0 => Water,
        >= 1 and <= 8 => FixtureBlockData.FirstLeveledWater + (level - 1),
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };

    private readonly Umpk.Game.World.World _world;
    private readonly FixtureBlockShapes _shapes = new();
    private readonly FixtureBlockData _data = new();

    /// <summary>Creates an all-air world with an overworld dimension.</summary>
    public FixtureWorld()
    {
        var dimType = Registry.FromEntries(
            RegistryIds.DimensionType,
            [0],
            [Identifier.Minecraft("overworld")],
            [new DimensionTypeDefinition(-64, 384, true)]);
        var dimension = new DimensionState(dimType[0], Identifier.Minecraft("overworld"));
        var biomes = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 1)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Build();
        _world = new Umpk.Game.World.World(dimension, _data, biomes);
    }

    /// <summary>Sets a block state id at a position.</summary>
    public FixtureWorld Set(int x, int y, int z, int stateId)
    {
        _world.SetBlockStateId(new BlockPos(x, y, z), stateId);
        return this;
    }

    /// <summary>Fills an inclusive box with a state id.</summary>
    public FixtureWorld Fill(int x1, int y1, int z1, int x2, int y2, int z2, int stateId)
    {
        for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
            for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
                for (int z = Math.Min(z1, z2); z <= Math.Max(z1, z2); z++)
                    _world.SetBlockStateId(new BlockPos(x, y, z), stateId);

        return this;
    }

    /// <summary>Lays a stone floor over an X/Z range at height <paramref name="y"/>.</summary>
    public FixtureWorld Floor(int x1, int x2, int z1, int z2, int y) => Fill(x1, y, z1, x2, y, z2, Stone);

    /// <summary>Lays a flowing run of water: a source at <c>(x, y, z)</c> and then descending fluid heights along <c>(stepX, stepZ)</c>, which is the state pattern vanilla settles into along a channel and therefore the state pattern that produces a steady unit current down the run.</summary>
    /// <param name="x">The source cell's X.</param>
    /// <param name="y">The TOP layer's Y. Extra <paramref name="layers"/> are laid downward from here.</param>
    /// <param name="z">The source cell's Z.</param>
    /// <param name="length">Cells in the run, including the source. Levels stop rising at 7.</param>
    /// <param name="stepX">The run's X step, -1, 0 or 1.</param>
    /// <param name="stepZ">The run's Z step, -1, 0 or 1.</param>
    /// <param name="layers">How many cells deep the run is. One layer is a vanilla-shaped surface sheet; more than one puts the same gradient on every layer, which is the only way a fixture can hold a swimmer's WHOLE body inside a current (vanilla reaches that shape down a sloping bed, not in a flat tank).</param>
    /// <exception cref="ArgumentOutOfRangeException">A step is not -1, 0 or 1, or both are 0.</exception>
    public FixtureWorld FlowingRun(int x, int y, int z, int length, int stepX, int stepZ, int layers = 1)
    {
        if (stepX is < -1 or > 1 || stepZ is < -1 or > 1 || (stepX == 0 && stepZ == 0))
            throw new ArgumentOutOfRangeException(nameof(stepX), "the run needs exactly one cardinal step");

        for (int i = 0; i < length; i++)
        {
            int state = WaterAtLevel(Math.Min(i, 7));
            for (int layer = 0; layer < layers; layer++)
                Set(x + (stepX * i), y - layer, z + (stepZ * i), state);

        }

        return this;
    }

    /// <summary>Fills a vertical column with FALLING water (<c>level=8</c>), the state under a source that has nothing to spread onto. Against a solid face it carries a strong downward flow, so an enclosed shaft of it is a current a swimmer meets over its whole body at every depth.</summary>
    public FixtureWorld FallingColumn(int x, int y, int z, int height)
        => Fill(x, y, z, x, y + height - 1, z, WaterAtLevel(8));

    /// <summary>Captures a planning view over the bounding box of two positions plus a margin.</summary>
    public PlanningWorldView Capture(BlockPos a, BlockPos b, int margin = 8)
        => PlanningWorldView.Capture(_world, _shapes, a, b, margin);

    /// <summary>The same box, materialised section by section on first read.</summary>
    public PlanningWorldView CaptureOnDemand(BlockPos a, BlockPos b, int margin = 8)
        => PlanningWorldView.CaptureOnDemand(_world, _shapes, a, b, margin);

    /// <summary>The block-shape source (a full cube for solid states, empty for air/fluid/climbable).</summary>
    public FixtureBlockShapes Shapes => _shapes;

    /// <summary>The block data source, whose <c>FlagReads</c> counts what the planner actually read.</summary>
    public FixtureBlockData Data => _data;
}
