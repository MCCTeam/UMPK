using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;

namespace Umpk.Client.Navigation;

/// <summary>Resolves which blocks a piston carries and which it breaks.</summary>
/// <remarks>
/// <para>A piston push sends no displacement packet (see <see cref="MovingPistonTracker"/>), so the client must derive the moved blocks. Otherwise, only the piston head and base could push the player.</para>
/// <para>The same sticky-block rules cover 1.14.4 through 26.2:</para>
/// <list type="number">
/// <item>
/// In 1.14.4 only slime is sticky; from 1.15.2 onward slime and honey are sticky. <c>minecraft:honey_block</c> does not exist before protocol 573 (measured against the committed block tables), so on 498 the two spellings answer identically.
/// </item>
/// <item>
/// From 1.15.2 onward honey and slime do not stick to each other. Because honey does not exist in 1.14.4, applying that exclusion there does not change the result.
/// </item>
/// </list>
/// <para>Two details remain version-dependent:</para>
/// <list type="number">
/// <item>
/// The hardcoded refusal set is obsidian at 498 and 578; adds crying obsidian and respawn anchors at 755 and 756; and adds reinforced deepslate from 759 through 777. Those blocks do not exist on the bands that do not name them (<c>crying_obsidian</c> and <c>respawn_anchor</c> first at protocol 735, <c>reinforced_deepslate</c> first at 759), so one union set is exact on all of them.
/// </item>
/// <item>
/// World-border containment uses strict <c>&gt;</c> with a <c>+1</c> offset on the min edge through 1.20.6 (protocol 766), <c>&gt;=</c> with no offset from 1.21 (protocol 767) on. Unlike the refusal set, this one canNOT be unified onto either formula: they disagree whenever the border's own bounds are not integers (an ordinary <c>/worldborder set/center</c> configuration), and whichever formula is picked is wrong on the other band for exactly that case. See <see cref="WorldBorderContainmentEra"/> for the two formulas and <see cref="_borderEra"/> for how the caller supplies the right one.
/// </item>
/// </list>
/// <para><see cref="ToDestroy"/> is computed because resolution needs it, but nothing acts on it. The server replaces those blocks with air and creates no moving block entity for them, so they push no entity; a client that also removed them from its world copy would be guessing ahead of the block updates the server does send for them.</para>
/// </remarks>
internal sealed class PistonStructureResolver
{
    /// <summary>The maximum number of blocks a piston can push.</summary>
    public const int MaxPushDepth = 12;

    private static readonly Identifier SlimeBlockId = Identifier.Minecraft("slime_block");
    private static readonly Identifier HoneyBlockId = Identifier.Minecraft("honey_block");
    private static readonly Identifier PistonId = Identifier.Minecraft("piston");
    private static readonly Identifier StickyPistonId = Identifier.Minecraft("sticky_piston");
    private static readonly Identifier PistonHeadId = Identifier.Minecraft("piston_head");

    /// <summary>The blocks <see cref="IsPushable"/> refuses by identity rather than by push reaction, unioned across every band this runs on. See the type remarks for why the union is exact and not a guess.</summary>
    private static readonly Identifier[] NeverPushable =
    [
        Identifier.Minecraft("obsidian"),
        Identifier.Minecraft("crying_obsidian"),
        Identifier.Minecraft("respawn_anchor"),
        Identifier.Minecraft("reinforced_deepslate"),
    ];

    private readonly Game.World.World _level;
    private readonly IBlockPushSource _push;
    private readonly BlockPos _pistonPos;
    private readonly Direction _pistonDirection;
    private readonly bool _extending;
    private readonly BlockPos? _clearedHead;

    /// <summary>Which world-border containment formula this session's protocol uses. Supplied by the caller (<see cref="MovingPistonTracker"/>, ultimately from <see cref="WorldBorderState.ContainmentEraForProtocol"/> over the session's own protocol version) rather than looked up here, because this class has no protocol number of its own - it only ever sees per-protocol DATA (<see cref="_push"/>, the block registry inside <see cref="_level"/>), never the raw version.</summary>
    private readonly WorldBorderContainmentEra _borderEra;

    private readonly BlockPos _startPos;
    private readonly List<BlockPos> _toPush = [];
    private readonly List<BlockPos> _toDestroy = [];

    /// <summary>Set when the resolution consulted something it does not actually know: a position outside a loaded column, or a block identifier the version's measured table does not carry.</summary>
    /// <remarks>A false pushability result normally means the block stops the line. Unknown terrain must not take that path because it would silently produce a truncated structure. This flag makes <see cref="Resolve"/> refuse the whole resolution and leave the client with only the piston's head and base.</remarks>
    private bool _refuse;

    /// <param name="level">The client's world copy.</param>
    /// <param name="push">The version's measured piston table.</param>
    /// <param name="pistonPos">The piston BASE position.</param>
    /// <param name="pistonDirection">The piston's <c>facing</c>.</param>
    /// <param name="extending">True for an extension, false for a sticky retraction.</param>
    /// <param name="borderEra">Which <c>world-border containment</c> formula applies, from <see cref="WorldBorderState.ContainmentEraForProtocol"/> over the session's protocol. Required rather than defaulted: the two formulas disagree at non-integer border edges (an ordinary <c>/worldborder</c> configuration), so a wrong default would be a silent, protocol-specific refusal exactly like the bug this parameter exists to prevent.</param>
    /// <param name="clearedHead">A position to read as AIR IF the real block there is <c>piston_head</c>, mirroring the first two lines of <c>piston structure movement</c>: During retraction, an existing piston head is treated as air before resolution runs BEFORE resolving. The client's world copy still holds that head (the server sent it when the piston extended), and <c>piston_head</c>'s reaction is BLOCK, so without this the retraction's own forward walk hits it and refuses a structure vanilla accepts. The condition is load-bearing, not decorative: <see cref="StateAt"/> only substitutes air when the position's REAL state is actually <c>piston_head</c>, so a stale or unexpected block there is read as itself rather than silently vanishing.</param>
    public PistonStructureResolver(
        Game.World.World level,
        IBlockPushSource push,
        BlockPos pistonPos,
        Direction pistonDirection,
        bool extending,
        WorldBorderContainmentEra borderEra,
        BlockPos? clearedHead = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(push);
        _level = level;
        _push = push;
        _borderEra = borderEra;
        _clearedHead = clearedHead;
        _pistonPos = pistonPos;
        _pistonDirection = pistonDirection;
        _extending = extending;
        if (extending)
        {
            PushDirection = pistonDirection;
            _startPos = pistonPos.Offset(pistonDirection);
        }
        else
        {
            PushDirection = pistonDirection.Opposite();
            _startPos = pistonPos.Offset(pistonDirection, 2);
        }
    }

    /// <summary>The direction the moved blocks travel.</summary>
    public Direction PushDirection { get; }

    /// <summary>The blocks to push, in movement order.</summary>
    public IReadOnlyList<BlockPos> ToPush => _toPush;

    /// <summary>The blocks the piston breaks instead of moving.</summary>
    public IReadOnlyList<BlockPos> ToDestroy => _toDestroy;

    /// <summary>Resolves the blocks affected by the piston movement.</summary>
    public bool Resolve()
    {
        _toPush.Clear();
        _toDestroy.Clear();
        _refuse = false;
        if (!_push.HasData)
            return false;

        BlockState start = StateAt(_startPos);
        if (!IsPushable(start, _startPos, PushDirection, canBreak: false, _pistonDirection))
        {
            if (_extending && ReactionOf(start) == PistonPushReaction.Destroy)
            {
                _toDestroy.Add(_startPos);
                return !_refuse;
            }

            return false;
        }

        if (!AddBlockLine(_startPos, PushDirection))
            return false;

        for (int i = 0; i < _toPush.Count; i++)
        {
            BlockPos pos = _toPush[i];
            if (IsSticky(StateAt(pos)) && !AddBranchingBlocks(pos))
                return false;

        }

        return !_refuse;
    }

    /// <summary>Whether the state is a slime or honey block.</summary>
    private static bool IsSticky(BlockState state)
    {
        Identifier id = state.Block.Id;
        return id == SlimeBlockId || id == HoneyBlockId;
    }

    /// <summary>Whether two neighboring states form one sticky group.</summary>
    private static bool CanStickToEachOther(BlockState a, BlockState b)
    {
        Identifier ida = a.Block.Id;
        Identifier idb = b.Block.Id;
        if (ida == HoneyBlockId && idb == SlimeBlockId)
            return false;

        if (ida == SlimeBlockId && idb == HoneyBlockId)
            return false;

        return IsSticky(a) || IsSticky(b);
    }

    /// <summary>Adds a push line starting at <paramref name="origin"/>.</summary>
    private bool AddBlockLine(BlockPos origin, Direction facing)
    {
        BlockState state = StateAt(origin);
        if (state.IsAir)
            return true;

        if (!IsPushable(state, origin, PushDirection, canBreak: false, facing))
            return true;

        if (origin == _pistonPos)
            return true;

        if (_toPush.Contains(origin))
            return true;

        int run = 1;
        if (run + _toPush.Count > MaxPushDepth)
            return false;

        while (IsSticky(state))
        {
            BlockPos behind = origin.Offset(PushDirection.Opposite(), run);
            BlockState previous = state;
            state = StateAt(behind);
            if (state.IsAir
                || !CanStickToEachOther(previous, state)
                || !IsPushable(state, behind, PushDirection, canBreak: false, PushDirection.Opposite())
                || behind == _pistonPos)
                break;

            if (++run + _toPush.Count > MaxPushDepth)
                return false;

        }

        int added = 0;
        for (int i = run - 1; i >= 0; i--)
        {
            _toPush.Add(origin.Offset(PushDirection.Opposite(), i));
            added++;
        }

        int ahead = 1;
        while (true)
        {
            BlockPos next = origin.Offset(PushDirection, ahead);
            int collision = _toPush.IndexOf(next);
            if (collision > -1)
            {
                ReorderListAtCollision(added, collision);
                for (int i = 0; i <= collision + added; i++)
                {
                    BlockPos pos = _toPush[i];
                    if (IsSticky(StateAt(pos)) && !AddBranchingBlocks(pos))
                        return false;

                }

                return true;
            }

            state = StateAt(next);
            if (state.IsAir)
                return true;

            if (!IsPushable(state, next, PushDirection, canBreak: true, PushDirection) || next == _pistonPos)
                return false;

            if (ReactionOf(state) == PistonPushReaction.Destroy)
            {
                _toDestroy.Add(next);
                return true;
            }

            if (_toPush.Count >= MaxPushDepth)
                return false;

            _toPush.Add(next);
            added++;
            ahead++;
        }
    }

    /// <summary>Reorders a branch that intersects blocks already scheduled to move.</summary>
    private void ReorderListAtCollision(int added, int collision)
    {
        List<BlockPos> head = [.. _toPush.GetRange(0, collision)];
        List<BlockPos> tail = [.. _toPush.GetRange(_toPush.Count - added, added)];
        List<BlockPos> middle = [.. _toPush.GetRange(collision, _toPush.Count - added - collision)];
        _toPush.Clear();
        _toPush.AddRange(head);
        _toPush.AddRange(tail);
        _toPush.AddRange(middle);
    }

    /// <summary>Adds blocks attached perpendicular to a sticky block.</summary>
    private bool AddBranchingBlocks(BlockPos origin)
    {
        BlockState centre = StateAt(origin);
        foreach (Direction direction in Directions)
        {
            if (direction.GetAxis() == PushDirection.GetAxis())
                continue;

            BlockPos side = origin.Offset(direction);
            BlockState state = StateAt(side);
            if (CanStickToEachOther(state, centre) && !AddBlockLine(side, direction))
                return false;

        }

        return true;
    }

    private static ReadOnlySpan<Direction> Directions =>
    [
        Direction.Down, Direction.Up, Direction.North, Direction.South, Direction.West, Direction.East,
    ];

    /// <summary>Vanilla <c>piston pushability</c> (1.21.11 ).</summary>
    /// <remarks>The world border test IS modelled, era-selected via <see cref="_borderEra"/>, using <see cref="Umpk.Game.World.WorldBorderState.IsWithinBounds"/>. It is not only a default-box formality: <see cref="Umpk.Game.World.World.Border"/> is server-driven and minigame/arena servers routinely SHRINK it, which is exactly the case where a piston near the edge diverges from vanilla if this is skipped OR if the wrong era's formula is used - <c>isWithinBounds</c> is not one formula across the protocols this resolver runs on (see <see cref="Umpk.Game.World.WorldBorderContainmentEra"/>). Vanilla runs the border test before even the air check, so it refuses a push at an out-of-bounds position regardless of what is there; that ordering is preserved by folding it into this same early return. The y-bound test is likewise modelled, because 1.18 lowered the floor to -64 and a piston at bedrock level is ordinary.</remarks>
    private bool IsPushable(BlockState state, BlockPos pos, Direction pushDirection, bool canBreak, Direction facing)
    {
        if (pos.Y < _level.Dimension.MinY || pos.Y >= _level.Dimension.MaxY
            || !_level.Border.IsWithinBounds(pos.X, pos.Z, _borderEra))
            return false;

        if (state.IsAir)
            return true;

        Identifier id = state.Block.Id;
        foreach (Identifier refused in NeverPushable)
            if (id == refused)
                return false;

        if (pushDirection == Direction.Down && pos.Y == _level.Dimension.MinY)
            return false;

        if (pushDirection == Direction.Up && pos.Y == _level.Dimension.MaxY - 1)
            return false;

        if (!_push.TryGet(id, out BlockPushInfo info))
        {
            // The version was measured but this identifier was not in it, which means a block the dataset does not know. Refusing is the honest answer; assuming NORMAL would push it.
            _refuse = true;
            return false;
        }

        if (id != PistonId && id != StickyPistonId)
        {
            if (info.Unbreakable)
                return false;

            switch (info.Reaction)
            {
                case PistonPushReaction.Block:
                    return false;
                case PistonPushReaction.Destroy:
                    return canBreak;
                case PistonPushReaction.PushOnly:
                    return pushDirection == facing;
                default:
                    // Normal and ignore reactions fall through.
                    break;
            }
        }
        else if (state.TryGetProperty("extended", out string extended) && extended == "true")
            return false;

        return !info.HasBlockEntity;
    }

    private PistonPushReaction ReactionOf(BlockState state) =>
        _push.TryGet(state.Block.Id, out BlockPushInfo info) ? info.Reaction : PistonPushReaction.Normal;

    /// <summary>The block at a position, flagging any read that fell outside a loaded column so <see cref="Resolve"/> can refuse rather than treat unknown terrain as air.</summary>
    /// <remarks>The <see cref="_clearedHead"/> substitution is gated on the real state there being <c>piston_head</c>. Clearing unconditionally would read whatever the client's world copy actually holds there as air, which is wrong whenever it is not a piston head.</remarks>
    private BlockState StateAt(BlockPos pos)
    {
        if (_level.GetColumn(pos) is null)
            _refuse = true;

        BlockState state = _level.GetBlock(pos);
        if (_clearedHead == pos && state.Block.Id == PistonHeadId)
            return _level.BlockData.GetState(0);

        return state;
    }
}
