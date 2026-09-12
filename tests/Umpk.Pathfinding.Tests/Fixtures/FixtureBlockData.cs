using Umpk.Game.Blocks;
using Umpk.Game.Registries;

namespace Umpk.Pathfinding.Tests.Fixtures;

/// <summary>Fixture data for <c>FixtureBlockData</c>.</summary>
public sealed class FixtureBlockData : IBlockDataSource
{
    private const int Count = 72;
    private const int BlockCount = 49;

    /// <summary>Fixture data for <c>StoneButtonWallSouthState</c>.</summary>
    internal const int StoneButtonWallSouthState = 48;

    /// <summary>Fixture data for <c>OakButtonWallSouthState</c>.</summary>
    internal const int OakButtonWallSouthState = 49;

    /// <summary>Fixture data for <c>LeverWallSouthState</c>.</summary>
    internal const int LeverWallSouthState = 50;

    /// <summary><c>minecraft:oak_button[face=wall,facing=east,powered=false]</c>: the same button attached to the wall on its WEST side. Course row L8 puts one round a corner.</summary>
    internal const int OakButtonWallEastState = 51;

    /// <summary>Fixture data for <c>RedstoneWireState</c> for course L4.</summary>
    internal const int RedstoneWireState = 52;

    /// <summary>Fixture data for <c>ScaffoldingState</c>.</summary>
    internal const int ScaffoldingState = 53;

    /// <summary>Fixture data for <c>CobwebState</c> for course F8.</summary>
    internal const int CobwebState = 54;

    /// <summary><c>minecraft:bubble_column[drag=false]</c>: the state a SOUL SAND base produces. Waterlogged (its fluid state is always a water source), no collision shape, and the upward upward lift applied by the engine. Course row E12 is built on it.</summary>
    internal const int BubbleColumnUpState = 55;

    /// <summary>Fixture data for <c>BubbleColumnDownState</c> for course E13.</summary>
    internal const int BubbleColumnDownState = 56;

    /// <summary><c>minecraft:slime_block</c>: a full solid cube that suppresses fall damage and bounces a non-sneaking body. Course rows N1, C8-slime and C8b.</summary>
    internal const int SlimeBlockState = 57;

    /// <summary><c>minecraft:hay_block</c>: a full solid cube whose landing behavior uses a 0.2 and which does not bounce. Course rows N2 and C8-hay.</summary>
    internal const int HayBlockState = 58;

    /// <summary>Fixture data for <c>IronDoorClosedState</c>.</summary>
    internal const int IronDoorClosedState = 45;

    /// <summary><c>minecraft:oak_trapdoor[half=top,open=false]</c>: the 3/16 panel at the TOP of its cell, i.e. a lid flush with the floor of the cell above. Course rows G5a and L5 are built on exactly this state, and it is the case where the barrier is not in front of the body but under its feet.</summary>
    internal const int ClosedTrapdoorTopState = 46;

    /// <summary>Fixture data for <c>IronTrapdoorClosedTopState</c>.</summary>
    internal const int IronTrapdoorClosedTopState = 47;

    /// <summary><c>minecraft:fire</c>: the EMPTY collision shape, so it blocks motion no more than a flower does and only the curated hazard set keeps a body out of it. That makes it the cleanest test of the hazard arm - remove the hazard and the cell is simply passable.</summary>
    internal const int FireState = 42;

    /// <summary><c>minecraft:soul_fire</c>: the same empty shape, and the same story.</summary>
    internal const int SoulFireState = 43;

    /// <summary>Fixture data for <c>CactusState</c>.</summary>
    internal const int CactusState = 44;

    /// <summary>Fixture data for <c>OpenDoorNorthState</c>.</summary>
    internal const int OpenDoorNorthState = 36;

    /// <summary>Fixture data for <c>ClosedDoorWestState</c>.</summary>
    internal const int ClosedDoorWestState = 37;

    /// <summary>Fixture data for <c>OpenTrapdoorNorthState</c>.</summary>
    internal const int OpenTrapdoorNorthState = 38;

    /// <summary>Fixture data for <c>OpenTrapdoorBottomWestState</c> for course G5B.</summary>
    internal const int OpenTrapdoorBottomWestState = 59;

    /// <summary><c>minecraft:iron_trapdoor[facing=east,half=top,open=true]</c>: what course row L5's lid looks like AFTER its button has been pressed - the 3/16 panel on the cell's WEST face, and the shaft under it open. A hand still cannot move it, so nothing folds this one.</summary>
    internal const int IronTrapdoorOpenWestState = 60;

    /// <summary><c>minecraft:oak_trapdoor[half=bottom,open=false]</c>: a 3/16 slab at the cell's floor, which is a floor rather than a barrier.</summary>
    internal const int ClosedTrapdoorBottomState = 39;

    /// <summary>Fixture data for <c>OpenFenceGateState</c>.</summary>
    internal const int OpenFenceGateState = 40;

    /// <summary>Fixture data for <c>UnreadableDoorState</c>.</summary>
    internal const int UnreadableDoorState = 41;

    /// <summary>Fixture data for <c>SoulSoilState</c>.</summary>
    internal const int SoulSoilState = 61;

    /// <summary><c>minecraft:pointed_dripstone[vertical_direction=up,thickness=tip]</c>: a STALAGMITE tip, and the one dripstone state whose landing is not ordinary. It adds 2.5 to the fall distance and doubles the multiplier. Its shape is <c>SHAPE_TIP_UP = column-shape construction</c>, which tops at 0.6875, so it really does hold a body up and must stay a floor.</summary>
    internal const int DripstoneTipUpState = 62;

    /// <summary><c>minecraft:pointed_dripstone[vertical_direction=down,thickness=tip]</c>: a STALACTITE tip. Same block, same <c>thickness</c>, opposite <c>vertical_direction</c>, and therefore an ORDINARY landing - the anti-over-reach control that a one-property implementation fails.</summary>
    internal const int DripstoneTipDownState = 63;

    /// <summary><c>minecraft:pointed_dripstone[vertical_direction=up,thickness=base]</c>: the base of a stalagmite column. Points up like <see cref="DripstoneTipUpState"/> and is still an ordinary landing, which is the other half of the control pair.</summary>
    internal const int DripstoneBaseUpState = 64;

    /// <summary>Fixture data for <c>OakPressurePlateState</c>.</summary>
    internal const int OakPressurePlateState = 65;

    /// <summary>Fixture data for <c>StonePressurePlateState</c>.</summary>
    internal const int StonePressurePlateState = 66;

    /// <summary>Fixture data for <c>PolishedBlackstonePressurePlateState</c>.</summary>
    internal const int PolishedBlackstonePressurePlateState = 67;

    /// <summary>Fixture data for <c>LightWeightedPressurePlateState</c>.</summary>
    internal const int LightWeightedPressurePlateState = 68;

    /// <summary>Fixture data for <c>HeavyWeightedPressurePlateState</c>.</summary>
    internal const int HeavyWeightedPressurePlateState = 69;

    /// <summary>A pressure plate whose <c>powered</c> property no source can resolve, i.e. the pre-flattening bands. It is the plate twin of <see cref="UnreadableDoorState"/> and exists for the same reason: on protocols 47-340 every property read answers false, and a resolver that reasoned about a plate it cannot tell pressed from unpressed would be guessing metadata. It maps to <c>minecraft:oak_pressure_plate</c>, so the NAME matches and only the property read fails - which is exactly the shape that would slip past a name-only rule.</summary>
    internal const int UnreadablePressurePlateState = 70;

    /// <summary>Fixture data for <c>BambooState</c>.</summary>
    internal const int BambooState = 71;

    /// <summary>Fixture data for <c>WaterloggedLadderState</c>.</summary>
    internal const int WaterloggedLadderState = 72;

    /// <summary><c>minecraft:soul_sand</c>, shape 278: <c>[[0,0,0,1,0.875,1]]</c>.</summary>
    internal const int SoulSandState = 31;

    /// <summary><c>minecraft:dirt_path</c>, shape 242: <c>[[0,0,0,1,0.9375,1]]</c>.</summary>
    internal const int DirtPathState = 32;

    /// <summary><c>minecraft:snow[layers=2]</c>, shape 226: <c>[[0,0,0,1,0.125,1]]</c>.</summary>
    internal const int SnowLayerTwoState = 33;

    /// <summary><c>minecraft:snow[layers=5]</c>, shape 27: <c>[[0,0,0,1,0.5,1]]</c>.</summary>
    internal const int SnowLayerFiveState = 34;

    /// <summary><c>minecraft:oak_stairs[half=top,facing=north]</c>, shape 28: two boxes both topping at 1.0, so an upside-down stair IS full-cover at 1.0 and is a full block from below.</summary>
    internal const int StairsTopState = 35;

    /// <summary><c>minecraft:white_carpet</c>, shape 117: <c>[[0,0,0,1,0.0625,1]]</c>.</summary>
    internal const int CarpetState = 24;

    /// <summary><c>minecraft:honey_block</c>, shape 135: inset 1/16 on each side, top 0.9375.</summary>
    internal const int HoneyState = 25;

    /// <summary><c>minecraft:lily_pad</c>, shape 271: the same inset, top 0.09375.</summary>
    internal const int LilyPadState = 26;

    /// <summary><c>minecraft:snow[layers=1]</c>, shape 0: the EMPTY shape, a pass-through.</summary>
    internal const int SnowLayerOneState = 27;

    /// <summary><c>minecraft:campfire</c>, shape 143: <c>[[0,0,0,1,0.4375,1]]</c>.</summary>
    internal const int CampfireState = 28;

    /// <summary><c>minecraft:cauldron</c>, shape 144: 1.0-high walls around a 0.25 inner floor.</summary>
    internal const int CauldronState = 29;

    /// <summary><c>minecraft:oak_fence_gate[open=false]</c>, shape 11: a 1.5-high panel.</summary>
    internal const int ClosedFenceGateState = 30;

    /// <summary>The first level-bearing water state id (<c>level=1</c>).</summary>
    internal const int FirstLeveledWater = 15;

    /// <summary>The last level-bearing water state id (<c>level=8</c>, the falling state).</summary>
    internal const int LastLeveledWater = 22;

    /// <summary><c>minecraft:ladder[facing=west]</c>: the same climbable block as state 3, but carrying the 3/16 collision plate vanilla gives it against the wall it hangs on.</summary>
    internal const int LadderWestState = 23;

    /// <summary>Creates the fixture block registry.</summary>
    public FixtureBlockData()
    {
        Blocks = new RegistryBuilder<BlockDefinition>(RegistryIds.Block, BlockCount)
            .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0))
            .Add(1, Identifier.Minecraft("stone"), new BlockDefinition(1, 1, 1))
            .Add(2, Identifier.Minecraft("water"), new BlockDefinition(2, 2, 2))
            .Add(3, Identifier.Minecraft("ladder"), new BlockDefinition(3, 3, 3))
            .Add(4, Identifier.Minecraft("oak_slab"), new BlockDefinition(4, 4, 4))
            .Add(5, Identifier.Minecraft("oak_slab_top"), new BlockDefinition(5, 5, 5))
            .Add(6, Identifier.Minecraft("snow"), new BlockDefinition(6, 6, 6))
            .Add(7, Identifier.Minecraft("oak_stairs"), new BlockDefinition(7, 7, 7))
            .Add(8, Identifier.Minecraft("oak_fence"), new BlockDefinition(8, 8, 8))
            .Add(9, Identifier.Minecraft("magma_block"), new BlockDefinition(9, 9, 9))
            .Add(10, Identifier.Minecraft("lava"), new BlockDefinition(10, 10, 10))
            .Add(11, Identifier.Minecraft("powder_snow"), new BlockDefinition(11, 11, 11))
            .Add(12, Identifier.Minecraft("flowing_water"), new BlockDefinition(12, 12, 12))
            .Add(13, Identifier.Minecraft("white_carpet"), new BlockDefinition(CarpetState, CarpetState, CarpetState))
            .Add(14, Identifier.Minecraft("honey_block"), new BlockDefinition(HoneyState, HoneyState, HoneyState))
            .Add(15, Identifier.Minecraft("lily_pad"), new BlockDefinition(LilyPadState, LilyPadState, LilyPadState))
            .Add(
                16,
                Identifier.Minecraft("snow_layer_one"),
                new BlockDefinition(SnowLayerOneState, SnowLayerOneState, SnowLayerOneState))
            .Add(17, Identifier.Minecraft("campfire"), new BlockDefinition(CampfireState, CampfireState, CampfireState))
            .Add(18, Identifier.Minecraft("cauldron"), new BlockDefinition(CauldronState, CauldronState, CauldronState))
            .Add(
                19,
                Identifier.Minecraft("oak_fence_gate"),
                new BlockDefinition(ClosedFenceGateState, ClosedFenceGateState, ClosedFenceGateState))
            .Add(20, Identifier.Minecraft("soul_sand"), new BlockDefinition(SoulSandState, SoulSandState, SoulSandState))
            .Add(21, Identifier.Minecraft("dirt_path"), new BlockDefinition(DirtPathState, DirtPathState, DirtPathState))
            .Add(
                22,
                Identifier.Minecraft("snow_layer_two"),
                new BlockDefinition(SnowLayerTwoState, SnowLayerTwoState, SnowLayerTwoState))
            .Add(
                23,
                Identifier.Minecraft("snow_layer_five"),
                new BlockDefinition(SnowLayerFiveState, SnowLayerFiveState, SnowLayerFiveState))
            .Add(
                24,
                Identifier.Minecraft("oak_stairs_top"),
                new BlockDefinition(StairsTopState, StairsTopState, StairsTopState))
            .Add(
                25,
                Identifier.Minecraft("oak_door"),
                new BlockDefinition(OpenDoorNorthState, UnreadableDoorState, ClosedDoorWestState))
            .Add(
                26,
                Identifier.Minecraft("oak_trapdoor"),
                new BlockDefinition(OpenTrapdoorNorthState, ClosedTrapdoorBottomState, ClosedTrapdoorBottomState))
            .Add(27, Identifier.Minecraft("fire"), new BlockDefinition(FireState, FireState, FireState))
            .Add(28, Identifier.Minecraft("soul_fire"), new BlockDefinition(SoulFireState, SoulFireState, SoulFireState))
            .Add(29, Identifier.Minecraft("cactus"), new BlockDefinition(CactusState, CactusState, CactusState))
            .Add(
                30,
                Identifier.Minecraft("iron_door"),
                new BlockDefinition(IronDoorClosedState, IronDoorClosedState, IronDoorClosedState))
            .Add(
                31,
                Identifier.Minecraft("iron_trapdoor"),
                new BlockDefinition(
                    IronTrapdoorClosedTopState, IronTrapdoorClosedTopState, IronTrapdoorClosedTopState))
            .Add(
                32,
                Identifier.Minecraft("stone_button"),
                new BlockDefinition(
                    StoneButtonWallSouthState, StoneButtonWallSouthState, StoneButtonWallSouthState))
            .Add(
                33,
                Identifier.Minecraft("oak_button"),
                new BlockDefinition(OakButtonWallSouthState, OakButtonWallEastState, OakButtonWallSouthState))
            .Add(
                34,
                Identifier.Minecraft("lever"),
                new BlockDefinition(LeverWallSouthState, LeverWallSouthState, LeverWallSouthState))
            .Add(
                35,
                Identifier.Minecraft("redstone_wire"),
                new BlockDefinition(RedstoneWireState, RedstoneWireState, RedstoneWireState))
            .Add(
                36,
                Identifier.Minecraft("scaffolding"),
                new BlockDefinition(ScaffoldingState, ScaffoldingState, ScaffoldingState))
            .Add(37, Identifier.Minecraft("cobweb"), new BlockDefinition(CobwebState, CobwebState, CobwebState))
            .Add(
                38,
                Identifier.Minecraft("bubble_column"),
                new BlockDefinition(BubbleColumnDownState, BubbleColumnUpState, BubbleColumnDownState))
            .Add(
                39,
                Identifier.Minecraft("slime_block"),
                new BlockDefinition(SlimeBlockState, SlimeBlockState, SlimeBlockState))
            .Add(
                40,
                Identifier.Minecraft("hay_block"),
                new BlockDefinition(HayBlockState, HayBlockState, HayBlockState))
            .Add(
                41,
                Identifier.Minecraft("soul_soil"),
                new BlockDefinition(SoulSoilState, SoulSoilState, SoulSoilState))
            .Add(
                42,
                Identifier.Minecraft("pointed_dripstone"),
                new BlockDefinition(DripstoneTipUpState, DripstoneBaseUpState, DripstoneTipUpState))
            .Add(
                48,
                Identifier.Minecraft("bamboo"),
                new BlockDefinition(BambooState, BambooState, BambooState))
            .Add(
                43,
                Identifier.Minecraft("oak_pressure_plate"),
                new BlockDefinition(OakPressurePlateState, OakPressurePlateState, OakPressurePlateState))
            .Add(
                44,
                Identifier.Minecraft("stone_pressure_plate"),
                new BlockDefinition(StonePressurePlateState, StonePressurePlateState, StonePressurePlateState))
            .Add(
                45,
                Identifier.Minecraft("polished_blackstone_pressure_plate"),
                new BlockDefinition(
                    PolishedBlackstonePressurePlateState,
                    PolishedBlackstonePressurePlateState,
                    PolishedBlackstonePressurePlateState))
            .Add(
                46,
                Identifier.Minecraft("light_weighted_pressure_plate"),
                new BlockDefinition(
                    LightWeightedPressurePlateState,
                    LightWeightedPressurePlateState,
                    LightWeightedPressurePlateState))
            .Add(
                47,
                Identifier.Minecraft("heavy_weighted_pressure_plate"),
                new BlockDefinition(
                    HeavyWeightedPressurePlateState,
                    HeavyWeightedPressurePlateState,
                    HeavyWeightedPressurePlateState))
            .Build();
    }

    /// <inheritdoc/>
    public Registry<BlockDefinition> Blocks { get; }

    /// <inheritdoc/>
    public int UnknownStateId => 0;

    /// <inheritdoc/>
    public bool IsLegacy => false;

    /// <inheritdoc/>
    public int StateCount => Count;

    /// <inheritdoc/>
    public bool IsValidState(int stateId) => stateId is >= 0 and < Count;

    /// <inheritdoc/>
    /// <remarks>States 13 and 14 are the waterlogged variants of the fence and the stair, so they map back onto those blocks rather than onto invented ones. This is the one place the table is genuinely many-states-to-one-block, which is what a <c>waterlogged</c> property IS.</remarks>
    public int GetBlockNetworkId(int stateId) => stateId switch
    {
        LadderWestState => 3,   // minecraft:ladder[facing=west]
        13 => 8,    // waterlogged oak_fence
        14 => 7,    // waterlogged oak_stairs
        >= FirstLeveledWater and <= LastLeveledWater => 2,   // minecraft:water at level 1..8
        CarpetState => 13,
        HoneyState => 14,
        LilyPadState => 15,
        SnowLayerOneState => 16,
        CampfireState => 17,
        CauldronState => 18,
        ClosedFenceGateState => 19,
        SoulSandState => 20,
        DirtPathState => 21,
        SnowLayerTwoState => 22,
        SnowLayerFiveState => 23,
        StairsTopState => 24,
        OpenDoorNorthState or ClosedDoorWestState or UnreadableDoorState => 25,
        OpenTrapdoorNorthState or OpenTrapdoorBottomWestState or ClosedTrapdoorBottomState
            or ClosedTrapdoorTopState => 26,
        OpenFenceGateState => 19,
        IronDoorClosedState => 30,
        IronTrapdoorClosedTopState or IronTrapdoorOpenWestState => 31,
        StoneButtonWallSouthState => 32,
        OakButtonWallSouthState or OakButtonWallEastState => 33,
        LeverWallSouthState => 34,
        RedstoneWireState => 35,
        ScaffoldingState => 36,
        CobwebState => 37,
        BubbleColumnUpState or BubbleColumnDownState => 38,
        SlimeBlockState => 39,
        HayBlockState => 40,
        FireState => 27,
        SoulFireState => 28,
        CactusState => 29,
        DripstoneTipUpState or DripstoneTipDownState or DripstoneBaseUpState => 42,
        BambooState => 48,
        WaterloggedLadderState => 3,   // minecraft:ladder[waterlogged=true]
        OakPressurePlateState or UnreadablePressurePlateState => 43,
        StonePressurePlateState => 44,
        PolishedBlackstonePressurePlateState => 45,
        LightWeightedPressurePlateState => 46,
        HeavyWeightedPressurePlateState => 47,
        >= 0 and < 13 => stateId,
        _ => 0,
    };

    /// <inheritdoc/>
    public int GetDefaultStateId(int blockNetworkId) => blockNetworkId;

    /// <summary>Fixture data for <c>FlagReads</c>.</summary>
    public long FlagReads { get; private set; }

    /// <inheritdoc/>
    public BlockFlags GetFlags(int stateId)
    {
        FlagReads++;
        return stateId switch
        {
            0 => BlockFlags.Air | BlockFlags.Replaceable,
            2 or 10 or 12 => BlockFlags.Fluid | BlockFlags.Replaceable,
            >= FirstLeveledWater and <= LastLeveledWater => BlockFlags.Fluid | BlockFlags.Replaceable,
            3 or LadderWestState => BlockFlags.Climbable,

            // Scaffolding is climbable and its shape is neither empty nor a single unit cube, so ClassifyShape gives it BlocksMotion without Solid, alongside the curated Climbable flag.
            ScaffoldingState => BlockFlags.Climbable | BlockFlags.BlocksMotion,

            // Partial-height, non-full-cube shapes: they block motion but never set Solid (mirrors BlockAttributeResolver.ClassifyShape, which reserves Solid for a single full unit cube).
            4 or 5 or 6 or 7 or 8 => BlockFlags.BlocksMotion,
            CarpetState or HoneyState or LilyPadState or CampfireState or CauldronState
                or ClosedFenceGateState or SoulSandState or DirtPathState or SnowLayerTwoState
                or SnowLayerFiveState or StairsTopState => BlockFlags.BlocksMotion,

            // Every door and trapdoor state carries a collision box - opening one ROTATES the 3/16 panel rather than removing it - so BlockAttributeResolver.ClassifyShape gives them all BlocksMotion, open ones included. That is precisely why passability cannot be read off the flag for this family. An OPEN fence gate is the exception: its collision shape is

            OpenDoorNorthState or ClosedDoorWestState or OpenTrapdoorNorthState
                or OpenTrapdoorBottomWestState or ClosedTrapdoorBottomState or UnreadableDoorState
                or IronDoorClosedState or ClosedTrapdoorTopState or IronTrapdoorClosedTopState
                or IronTrapdoorOpenWestState => BlockFlags.BlocksMotion,
            OpenFenceGateState => BlockFlags.None,

            // Powder snow's collision ref is the empty shape, so it blocks motion no more than a flower does; only the curated hazard set keeps a body or a foot out of it. snow[layers=1] is the same story with no hazard behind it: ClassifyShape gives an empty collision ref neither BlocksMotion nor Solid, and the body simply walks over the cell.
            11 or SnowLayerOneState or FireState or SoulFireState or CobwebState => BlockFlags.None,

            // A bubble column CONTAINS water and is not water: Waterlogged, no Fluid, no collision

            // intrinsically_waterlogged list carries it.
            BubbleColumnUpState or BubbleColumnDownState => BlockFlags.Waterlogged,

            // Both are full unit cubes, so ClassifyShape gives them Solid and BlocksMotion exactly as it gives stone. What separates them from stone is their fallOn multiplier, which is not a flag at all.
            SlimeBlockState or HayBlockState => BlockFlags.Solid | BlockFlags.BlocksMotion,

            // eight button states and all lever states collision shape 0 (the empty shape) and BlockAttributeResolver.ClassifyShape gives them neither BlocksMotion nor Solid. A lane cell holding a wall button is as walkable as air, which is what course row L1 depends on.
            StoneButtonWallSouthState or OakButtonWallSouthState or OakButtonWallEastState
                or LeverWallSouthState or RedstoneWireState => BlockFlags.None,

            //  light weighted,  heavy weighted,  polished blackstone), so

            // sixteen. A plate cell is therefore already walkable and contributes no support height, which is why this item changes no passability rule.
            OakPressurePlateState or StonePressurePlateState or PolishedBlackstonePressurePlateState
                or LightWeightedPressurePlateState or HeavyWeightedPressurePlateState
                or UnreadablePressurePlateState => BlockFlags.None,

            // A cactus is a 15/16 box: it blocks motion and it is never Solid.
            CactusState => BlockFlags.BlocksMotion,

            // Pointed dripstone is a narrow column - 6/16 at the tip, 12/16 at the base - so it blocks

            // hazard: the physics walks a stalagmite deck perfectly well and the danger is entirely in the arrival velocity, which is FallDamageModel's job, not IsHazard's.
            DripstoneTipUpState or DripstoneTipDownState or DripstoneBaseUpState => BlockFlags.BlocksMotion,

            // A bamboo stalk is a 3/16 post: BlocksMotion, never Solid, never a hazard.
            BambooState => BlockFlags.BlocksMotion,

            // Waterlogged fence / waterlogged stair. BlockAttributeResolver sets Waterlogged from the

            13 or 14 => BlockFlags.BlocksMotion | BlockFlags.Waterlogged,
            WaterloggedLadderState => BlockFlags.Climbable | BlockFlags.Waterlogged,
            _ => BlockFlags.Solid | BlockFlags.BlocksMotion,
        };
    }

    /// <inheritdoc/>
    public float GetFriction(int stateId) => 0.6f;

    /// <summary>Fixture data for <c>GetSpeedFactor</c>.</summary>
    public float GetSpeedFactor(int stateId) => stateId switch
    {
        SoulSandState or HoneyState => 0.4f,
        _ => 1.0f,
    };

    /// <summary>Fixture data for <c>GetJumpFactor</c>.</summary>
    public float GetJumpFactor(int stateId) => stateId switch
    {
        HoneyState => 0.5f,
        _ => 1.0f,
    };

    /// <inheritdoc/>
    public IReadOnlyList<string> GetPropertyNames(int stateId) => stateId switch
    {
        LadderWestState => Facing,
        7 or 8 or 13 or 14 => Waterloggable,
        >= FirstLeveledWater and <= LastLeveledWater => Levelled,
        ClosedFenceGateState or OpenDoorNorthState or ClosedDoorWestState
            or OpenFenceGateState or IronDoorClosedState => Openable,

        // An open trapdoor carries `half` as well as `open`; only the bottom fold leaves a floor. ClosedTrapdoorBottomState carries it too, so the pair reads the same property name.
        OpenTrapdoorNorthState or ClosedTrapdoorBottomState => OpenableHalf,
        OpenTrapdoorBottomWestState => OpenableFacingHalf,

        // A closed trapdoor carries `facing` as well as `open`; the open panel stands against the wall opposite FACING.

        ClosedTrapdoorTopState or IronTrapdoorClosedTopState => OpenableFacing,
        IronTrapdoorOpenWestState => OpenableFacingHalf,
        StoneButtonWallSouthState or OakButtonWallSouthState or OakButtonWallEastState
            or LeverWallSouthState => Attachable,

        // A plain plate carries `powered`, a boolean; a weighted one carries `power`, an int 0-15

        // asks whether the property RESOLVES, because a source that cannot read it is a pre-flattening source and reasoning about a plate it cannot tell pressed from unpressed would be a guess.
        OakPressurePlateState or StonePressurePlateState
            or PolishedBlackstonePressurePlateState => Pressable,
        LightWeightedPressurePlateState or HeavyWeightedPressurePlateState => Weighable,
        DripstoneTipUpState or DripstoneTipDownState or DripstoneBaseUpState => Speleothem,
        _ => Array.Empty<string>(),
    };

    /// <inheritdoc/>
    public bool TryGetPropertyValue(int stateId, string propertyName, out string value)
    {
        if (propertyName == "facing" && stateId == LadderWestState)
        {
            value = "west";
            return true;
        }

        if (propertyName == "drag" && stateId is BubbleColumnUpState or BubbleColumnDownState)
        {
            value = stateId == BubbleColumnDownState ? "true" : "false";
            return true;
        }

        if (propertyName == "waterlogged" && stateId is 7 or 8 or 13 or 14)
        {
            value = stateId is 13 or 14 ? "true" : "false";
            return true;
        }

        // The door family's `open` property. UnreadableDoorState is deliberately absent: it is the pre-flattening case, where TryGetProperty answers false for every name.
        if (propertyName == "open")
            switch (stateId)
            {
                case OpenDoorNorthState:
                case OpenTrapdoorNorthState:
                case OpenTrapdoorBottomWestState:
                case IronTrapdoorOpenWestState:
                case OpenFenceGateState:
                    value = "true";
                    return true;
                case ClosedDoorWestState:
                case ClosedTrapdoorBottomState:
                case ClosedFenceGateState:
                case IronDoorClosedState:
                case ClosedTrapdoorTopState:
                case IronTrapdoorClosedTopState:
                    value = "false";
                    return true;
                default:
                    break;
            }

        // The speleothem pair. TIP_DIRECTION says stalagmite from stalactite and THICKNESS says tip from shaft, and the stalagmite landing needs BOTH: fall on behavior is `TIP_DIRECTION == UP && THICKNESS == TIP`.
        if (propertyName == "vertical_direction"
            && stateId is DripstoneTipUpState or DripstoneTipDownState or DripstoneBaseUpState)
        {
            value = stateId == DripstoneTipDownState ? "down" : "up";
            return true;
        }

        if (propertyName == "thickness"
            && stateId is DripstoneTipUpState or DripstoneTipDownState or DripstoneBaseUpState)
        {
            value = stateId == DripstoneBaseUpState ? "base" : "tip";
            return true;
        }

        // A plain plate's `powered` and a weighted plate's `power`. Both fixture states are the UNPRESSED one, because that is what the planner sees: it plans against a world the body has not walked into yet.
        if (propertyName == "powered"
            && stateId is OakPressurePlateState or StonePressurePlateState
                or PolishedBlackstonePressurePlateState)
        {
            value = "false";
            return true;
        }

        if (propertyName == "power"
            && stateId is LightWeightedPressurePlateState or HeavyWeightedPressurePlateState)
        {
            value = "0";
            return true;
        }

        // A button's and a lever's attachment, which is what decides where their DIRECT signal goes: connected direction behavior returns FACING for face=WALL, and the block they hang on is the neighbour in the OPPOSITE direction.
        if (propertyName == "face"
            && stateId is StoneButtonWallSouthState or OakButtonWallSouthState
                or OakButtonWallEastState or LeverWallSouthState)
        {
            value = "wall";
            return true;
        }

        // `half` determines which way an open trapdoor folds.
        if (propertyName == "half")
            switch (stateId)
            {
                case OpenTrapdoorBottomWestState:
                case ClosedTrapdoorBottomState:
                    value = "bottom";
                    return true;
                case OpenTrapdoorNorthState:
                case IronTrapdoorOpenWestState:
                    value = "top";
                    return true;
                default:
                    break;
            }

        if (propertyName == "facing")
        {
            switch (stateId)
            {
                case ClosedTrapdoorTopState:
                case IronTrapdoorClosedTopState:
                case OpenTrapdoorBottomWestState:
                case IronTrapdoorOpenWestState:
                    // facing=east, so the open panel lands on the cell's WEST face.
                    value = "east";
                    return true;
                case StoneButtonWallSouthState:
                case OakButtonWallSouthState:
                case LeverWallSouthState:
                    value = "south";
                    return true;
                case OakButtonWallEastState:
                    value = "east";
                    return true;
                default:
                    break;
            }
        }

        if (propertyName == "level" && stateId is >= FirstLeveledWater and <= LastLeveledWater)
        {
            value = LevelNames[stateId - FirstLeveledWater];
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <inheritdoc/>
    public bool TryDecodeLegacy(int stateId, out int blockId, out int meta)
    {
        blockId = 0;
        meta = 0;
        return false;
    }

    /// <inheritdoc/>
    public int EncodeLegacy(int blockId, int meta) => (blockId << 4) | (meta & 0xF);

    private static readonly string[] Facing = ["facing"];

    private static readonly string[] Waterloggable = ["waterlogged"];

    private static readonly string[] Levelled = ["level"];

    private static readonly string[] Openable = ["open"];

    private static readonly string[] Attachable = ["face", "facing", "powered"];

    /// <summary>Fixture data for <c>Pressable</c>.</summary>
    private static readonly string[] Pressable = ["powered"];

    /// <summary>Fixture data for <c>Weighable</c>.</summary>
    private static readonly string[] Weighable = ["power"];

    /// <summary>Fixture data for <c>Speleothem</c>.</summary>
    private static readonly string[] Speleothem = ["vertical_direction", "thickness", "waterlogged"];

    private static readonly string[] OpenableFacing = ["open", "facing"];

    /// <summary>An open or closed trapdoor's <c>half</c>, which is what its CLOSED shape is keyed on.</summary>
    private static readonly string[] OpenableHalf = ["open", "half"];

    /// <summary>An open trapdoor carries both: <c>facing</c> shapes it open, <c>half</c> shapes it shut.</summary>
    private static readonly string[] OpenableFacingHalf = ["open", "facing", "half"];

    private static readonly string[] LevelNames = ["1", "2", "3", "4", "5", "6", "7", "8"];
}
