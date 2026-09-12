using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;

namespace Umpk.Client.Navigation;

/// <summary>Turns piston block events into live <see cref="MovingPiston"/> entities and applies their movement to the local player.</summary>
/// <remarks>
/// <para>Piston structure movement writes <c>moving_piston</c> blocks without the client-update flag, so no ordinary block update for them reaches a client. A block-event packet is the observable signal that a piston started extending or retracting. This class translates that event into the movement state a headless client can act on.</para>
/// <para>The source piston head during extension or base during retraction always moves. Each block resolved by <see cref="PistonStructureResolver"/> produces one additional moving entity. Push reaction, destroy speed, and block-entity presence are not carried by packets, so they come from the per-version <see cref="IBlockPushSource"/> dataset.</para>
/// <para>The source entity is never gated on the client's own resolution, and that is deliberate. The server sends the block event only after it has resolved a valid movement, so the event is already proof that a push happens. Requiring local resolution too would miss pushes in unloaded columns and versions without measured push data. The pushed blocks are therefore purely additive: where the resolver refuses, this behaves exactly as it did before it existed.</para>
/// <para>Only non-legacy block data, protocol 393 and newer, is modelled. Earlier data carries one shape per block id sampled at metadata 0 plus a five-family per-state supplement that does not include pistons, so <c>piston_head</c> would answer its <c>facing=down</c> boxes for all six facings and the push would be computed off the wrong geometry. Refusing to model it there avoids inventing a push; the legacy row already records <c>probe_no_predicate</c> because a half-block discrimination is not expressible in the legacy selector grammar either.</para>
/// </remarks>
internal sealed class MovingPistonTracker
{
    /// <summary>The block-event id for extension.</summary>
    private const int TriggerExtend = 0;

    /// <summary>The block-event id for retraction.</summary>
    private const int TriggerContract = 1;

    /// <summary>The block-event id for dropping a moving block.</summary>
    private const int TriggerDrop = 2;

    /// <summary>A ceiling on live entities so a redstone clock cannot grow the list without bound. Vanilla has no such limit because its entities are owned by chunks and die with them.</summary>
    /// <remarks>Worst case per <c>OnBlockEvent</c> call: 1 source entity (head or base) plus up to <see cref="PistonStructureResolver.MaxPushDepth"/> pushed/pulled entities from <c>AddPushedBlocks</c>, so a single event can add at most 13. <see cref="MaxTracked"/> tolerates roughly 4-5 such events landing before any of them retire, which covers an ordinary redstone clock comfortably; it is a hard ceiling <see cref="Add"/> enforces, not a promise that a large-scale contraption (many pistons firing in the same tick, e.g. a big flying machine or cannon array) never has an excess entity silently dropped. The test suite drives well past this ceiling in one burst and asserts the count never exceeds it.</remarks>
    private const int MaxTracked = 64;

    private static readonly Identifier PistonId = Identifier.Minecraft("piston");
    private static readonly Identifier StickyPistonId = Identifier.Minecraft("sticky_piston");
    private static readonly Identifier PistonHeadId = Identifier.Minecraft("piston_head");

    private readonly IBlockShapeSource _shapes;
    private readonly IBlockPushSource _push;
    private readonly Func<Game.World.World?> _world;
    private readonly WorldBorderContainmentEra _borderEra;
    private readonly List<MovingPiston> _active = [];
    private readonly Dictionary<(Direction Facing, bool Short), Aabb[]> _headShapes = [];

    /// <param name="shapes">The version's collision-shape source.</param>
    /// <param name="push">The version's measured piston push table.</param>
    /// <param name="world">Resolves the current world, or null before one exists.</param>
    /// <param name="borderEra">Which <c>world-border containment</c> formula this session's protocol uses; see <see cref="WorldBorderState.ContainmentEraForProtocol"/>. Threaded straight through to every <see cref="PistonStructureResolver"/> this class constructs, the same way <paramref name="push"/> already is.</param>
    public MovingPistonTracker(
        IBlockShapeSource shapes,
        IBlockPushSource push,
        Func<Game.World.World?> world,
        WorldBorderContainmentEra borderEra)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(push);
        ArgumentNullException.ThrowIfNull(world);
        _shapes = shapes;
        _push = push;
        _world = world;
        _borderEra = borderEra;
    }

    /// <summary>The number of moving pistons currently being simulated. Exposed for tests.</summary>
    public int ActiveCount => _active.Count;

    /// <summary>Applies the client-relevant piston state transition. Ignores every block event that is not a piston, exactly as vanilla does by dispatching on the block state the CLIENT holds rather than on the block id in the packet.</summary>
    public void OnBlockEvent(BlockPos position, int action, int param)
    {
        _ = param;
        Game.World.World? world = _world();
        if (world is null || world.BlockData.IsLegacy)
            return;

        BlockState state = world.GetBlock(position);
        if (state.IsDefault || !state.IsValid)
            return;

        Identifier block = state.Block.Id;
        if (block != PistonId && block != StickyPistonId)
            return;

        if (!state.TryGetProperty("facing", out string facingName)
            || !TryParseDirection(facingName, out Direction facing))
            return;

        switch (action)
        {
            case TriggerExtend:
                {
                    // The head entity moves to the block in front of the base, carrying the piston head as its moved state, extending and source.
                    BlockPos head = position.Offset(facing);
                    Retire(head);
                    if (TryHeadShape(world, facing, isShort: false, out Aabb[] shape))
                        Add(new MovingPiston(head, facing, extending: true, isSourcePiston: true, shape));

                    AddPushedBlocks(world, position, facing, extending: true, clearedHead: null);
                    break;
                }

            case TriggerContract:
            case TriggerDrop:
                {
                    // Retraction retires any extending head and places the retracting entity at the base. Its collision shape changes from the full head to the short head as it moves.
                    BlockPos head = position.Offset(facing);
                    Retire(head);
                    Retire(position);
                    if (TryHeadShape(world, facing, isShort: false, out Aabb[] full)
                        && TryHeadShape(world, facing, isShort: true, out Aabb[] shortened))
                        Add(new MovingPiston(
                            position, facing, extending: false, isSourcePiston: true, full, shortened));

                    if (ShouldPullOnRetract(world, block, position, facing, action))
                        AddPushedBlocks(world, position, facing, extending: false, clearedHead: head);

                    break;
                }

            default:
                break;
        }
    }

    /// <summary>Ticks piston entities after the player's movement report. This preserves the one-tick delay between a piston push and the movement report that includes it.</summary>
    public void Tick(PlayerPhysics engine)
    {
        if (_active.Count == 0)
            return;

        ArgumentNullException.ThrowIfNull(engine);

        // per-tick piston displacement limit's accumulator is per GAME TICK, so it is cleared once here and not once per piston: two pistons pushing the same player in one tick share the 0.51 budget.
        engine.BeginPistonTick();

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            MovingPiston piston = _active[i];
            piston.Tick(engine);
            if (piston.Removed)
                _active.RemoveAt(i);

        }
    }

    /// <summary>Drops every tracked piston. Called when the world is replaced.</summary>
    public void Clear() => _active.Clear();

    /// <summary>Resolves the piston structure and creates one moving block entity at each moved block's destination, carrying the block's original state.</summary>
    /// <remarks>
    /// <para>The destination is one block along <see cref="MovingPiston.MovementDirection"/>: the piston facing while extending and its opposite while retracting. The entity keeps the piston facing in both cases, while its progress direction supplies the sign.</para>
    /// <para>Blocks whose moved state has no collision boxes are skipped because they cannot push an entity. Skipping them keeps the tracked list short enough that <see cref="MaxTracked"/> is never the thing that drops a real pusher.</para>
    /// </remarks>
    private void AddPushedBlocks(
        Game.World.World world, BlockPos pistonPos, Direction facing, bool extending, BlockPos? clearedHead)
    {
        var resolver = new PistonStructureResolver(world, _push, pistonPos, facing, extending, _borderEra, clearedHead);
        if (!resolver.Resolve())
            return;

        Direction movement = extending ? facing : facing.Opposite();
        foreach (BlockPos source in resolver.ToPush)
        {
            BlockState moved = world.GetBlock(source);
            ReadOnlySpan<Aabb> shape = _shapes.GetCollisionShapes(moved);
            if (shape.Length == 0)
                continue;

            BlockPos destination = source.Offset(movement);
            Retire(destination);
            Add(new MovingPiston(
                destination, facing, extending, isSourcePiston: false, shape.ToArray()));
        }
    }

    /// <summary>The sticky-retraction gate: a retracting piston pulls the block two ahead of it back ONLY when the piston is sticky, the event is <c>TRIGGER_CONTRACT</c> and not <c>TRIGGER_DROP</c>, and that block is present, pushable and reacts NORMAL (or is itself a piston).</summary>
    /// <remarks>
    /// <para>The server cancels a pull when the block two ahead is already a <c>moving_piston</c>, but that state is never sent to clients, so the client world cannot observe this branch.</para>
    /// <para>The checks are not all independently load-bearing. The <c>block != StickyPistonId</c> and <c>action != TriggerContract</c> checks each catch a real case the resolver has no way to know about (piston stickiness and block-event action aren't inputs to <see cref="PistonStructureResolver"/> at all) and each has its own dedicated negative test. The air check and the reaction check do NOT: <c>Resolve()</c> calls <c>isPushable</c> on this SAME position with the SAME effective direction, and for every reaction value in <c>block-push.json</c>, that call refuses on its own (air short-circuits <c>AddBlockLine</c> to an empty push list before <c>isPushable</c> is even reached; BLOCK/DESTROY/PUSH_ONLY all fail their own arms). Their tests are still correct end-to-end assertions - removing <c>AddPushedBlocks</c> entirely would fail them - they just do not discriminate these two clauses specifically. They stay because they preserve the complete gate and would matter for a reaction value no supported band currently has (<c>IGNORE</c>), not because removing them is currently observable.</para>
    /// </remarks>
    private bool ShouldPullOnRetract(
        Game.World.World world, Identifier block, BlockPos position, Direction facing, int action)
    {
        if (block != StickyPistonId || action != TriggerContract)
            return false;

        BlockPos pulled = position.Offset(facing, 2);
        BlockState state = world.GetBlock(pulled);
        if (state.IsAir || !_push.TryGet(state.Block.Id, out BlockPushInfo info))
            return false;

        Identifier id = state.Block.Id;
        if (info.Reaction != PistonPushReaction.Normal && id != PistonId && id != StickyPistonId)
            return false;

        return true;
    }

    private void Add(MovingPiston piston)
    {
        if (_active.Count >= MaxTracked)
            return;

        _active.Add(piston);
    }

    /// <summary>Completes the client-side movement: the entity stops existing and stops pushing.</summary>
    private void Retire(BlockPos position)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
            if (_active[i].Position == position)
                _active.RemoveAt(i);

    }

    /// <summary>The collision boxes of <c>minecraft:piston_head[facing,short]</c> on this version's own block table. Collision shape selection reads only those two properties, so <c>type</c> is left at whatever the first matching state carries.</summary>
    private bool TryHeadShape(Game.World.World world, Direction facing, bool isShort, out Aabb[] shape)
    {
        if (_headShapes.TryGetValue((facing, isShort), out Aabb[]? cached))
        {
            shape = cached;
            return cached.Length > 0;
        }

        shape = [];
        IBlockDataSource data = world.BlockData;
        if (!data.Blocks.TryGet(PistonHeadId, out RegistryEntry<BlockDefinition> entry))
        {
            _headShapes[(facing, isShort)] = shape;
            return false;
        }

        string wantFacing = NameOf(facing);
        string wantShort = isShort ? "true" : "false";
        BlockDefinition definition = entry.Value;
        for (int stateId = definition.MinStateId; stateId <= definition.MaxStateId; stateId++)
        {
            if (!data.TryGetPropertyValue(stateId, "facing", out string gotFacing) || gotFacing != wantFacing)
                continue;

            if (!data.TryGetPropertyValue(stateId, "short", out string gotShort) || gotShort != wantShort)
                continue;

            shape = _shapes.GetCollisionShapes(new BlockState(data, stateId)).ToArray();
            break;
        }

        _headShapes[(facing, isShort)] = shape;
        return shape.Length > 0;
    }

    private static string NameOf(Direction direction) => direction switch
    {
        Direction.Down => "down",
        Direction.Up => "up",
        Direction.North => "north",
        Direction.South => "south",
        Direction.West => "west",
        _ => "east",
    };

    private static bool TryParseDirection(string name, out Direction direction)
    {
        switch (name)
        {
            case "down": direction = Direction.Down; return true;
            case "up": direction = Direction.Up; return true;
            case "north": direction = Direction.North; return true;
            case "south": direction = Direction.South; return true;
            case "west": direction = Direction.West; return true;
            case "east": direction = Direction.East; return true;
            default: direction = Direction.North; return false;
        }
    }
}
