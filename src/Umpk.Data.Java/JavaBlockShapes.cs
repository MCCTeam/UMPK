using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Data.Java;

/// <summary>The version-accurate <see cref="IBlockShapeSource"/> over the generated per-band shape tables: the deduplicated AABB pool (<c>Descriptor.CollisionShapes</c>) plus the table that says which pooled shape each block state uses (<c>Descriptor.BlockShapeRefs</c>).</summary>
/// <remarks>
/// <para>Both tables are parsed once per protocol into a flat <see cref="Aabb"/> array plus an offset index, so a lookup is two array reads and the returned span is a window into shared storage: no allocation on the physics engine's per-tick path.</para>
/// <para>Coverage is not uniform across bands, and where it is missing this degrades to the same flag-derived unit cube the flags-only fallback used rather than inventing geometry:</para>
/// <list type="bullet">
/// <item>Flattened bands (protocol 393 and up) carry a per-BLOCK-STATE index and cover every state
/// of every block, so slabs, stairs, fences, walls, panes and carpets all resolve exactly.</item>
/// <item>Pre-flattening bands 107-340 (1.9-1.12.2) carry one shape per block id at
/// meta 0, PLUS a per-state supplement for FOURTEEN families: slab half, ladder facing, trapdoor half/open/facing, fence-gate open/axis, snow-layer height, straight-stair facing/half, closed-door facing, extended-piston facing, piston-head facing, anvil facing axis, cake bites, end-portal-frame eye, skull facing and end-rod facing axis. Every other block id still shares its meta-0 shape across all sixteen metas.</item>
/// <item>Pre-flattening band 47 (1.8) carries only a 12-block-id set (0-4, 8-11, and the
/// three half slabs 44/126/182), with the per-state supplement over those three slabs; every other block id on that band falls back to the flag-derived cube. The supplement can only refine a block the base table already covers, so the families above reach 107-340 and NOT 47.</item>
/// </list>
/// <para>What is left, and why each piece is left:</para>
/// <list type="bullet">
/// <item>Blocks with no collision need no supplement: torch, lever, button, rail, pressure plate,
/// tripwire hook, and portal have no collision for every metadata value. A portal's outline does turn with its axis, but the datasets carry no outline table at all.</item>
/// <item>METADATA-CARRYING but collision-INVARIANT: a cauldron adds the same five boxes whatever its
/// LEVEL says.</item>
/// <item>Neighbour-derived geometry cannot be represented by a per-state table: fences, walls, panes,
/// chests, redstone wire, vines and tripwire; a stair's inner and outer corner shapes, which depend on the stairs in front of and behind it; an open door's box, which needs the hinge from the door block above; and the UPPER half of a door, which needs the facing and open flag from the door block below. Those keep answering their block's meta-0 box.</item>
/// </list>
/// <para>The supplement can only refine coverage, never remove it: <see cref="ReadRefs"/> seeds every meta of a covered block with that block's own value before applying overrides, so an unlisted meta retains the base block shape rather than becoming silently non-solid.</para>
/// <para>Block flags and collision shapes intentionally diverge for one legacy case. Flags are a different table and are still derived per BLOCK id from the meta-0 shape table; <c>CollisionRefs</c> returns a single-element list on legacy datasets. On the ten bands that carry a fence gate, an open gate answers no collision boxes here while <c>BlockState.BlocksMotion</c> still reports true, and <c>Umpk.Pathfinding</c>'s <c>Moves/MoveHelper.IsPassable</c> ends in <c>return !state.BlocksMotion;</c>, reading that flag rather than these boxes: physics walks through the open gate, while the planner still refuses to route through it. This is conservative: the planner declines a valid route instead of attempting an invalid one. Correcting the divergence requires changing legacy flag derivation outside this type.</para>
/// <para>Outline (visual/raycast) shapes are answered with the collision shapes. The datasets carry no separate outline table, and the two differ for a small set of blocks (a fence's outline is one block tall while it collides at 1.5), so callers that need the visual box for those blocks are reading an approximation. Returning nothing instead would break raycasting outright.</para>
/// </remarks>
internal sealed class JavaBlockShapes : IBlockShapeSource
{
    /// <summary>Vanilla's pre-flattening state packing is <c>(blockId &lt;&lt; 4) | meta</c>.</summary>
    private const int LegacyMetaBits = 4;

    /// <summary>The flag-derived fallback for any index the dataset does not cover.</summary>
    private static readonly Aabb[] UnitCube = [new(0, 0, 0, 1, 1, 1)];

    private static readonly Aabb[] NoBoxes = [];

    /// <summary>Every pooled box, back to back; a shape is a window into this array.</summary>
    private readonly Aabb[] _boxes;

    /// <summary>Start offset of each pooled shape, with a terminator, so length is a subtraction.</summary>
    private readonly int[] _offsets;

    /// <summary>Per index: pool index + 1, or 0 for uncovered. The index is a block STATE id for every table this repo ships, legacy included, because <see cref="ReadRefs"/> expands the pre-flattening kind-2 table to one entry per <c>(blockId &lt;&lt; 4) | meta</c>. It is a block NETWORK id only for a kind-1 table, which no shipped band still uses; <see cref="_byBlockId"/> says which.</summary>
    private readonly int[] _refs;

    /// <summary>True when <see cref="_refs"/> is indexed by block network id rather than state id.</summary>
    private readonly bool _byBlockId;

    internal JavaBlockShapes(ReadOnlySpan<byte> pool, ReadOnlySpan<byte> refs)
    {
        (_boxes, _offsets) = ReadPool(pool);
        (_refs, _byBlockId) = ReadRefs(refs);

        int covered = 0;
        foreach (int value in _refs)
            if (value != 0)
                covered++;

        CoveredCount = covered;
    }

    /// <summary>Test seam: how many indices this band's dataset actually names a shape for.</summary>
    internal int CoveredCount { get; }

    /// <summary>Test seam: the size of the reference table. STATES on every band this repo ships, legacy included, since the pre-flattening kind-2 table is expanded to sixteen entries per block id (measured: 2928 on protocol 47, 4096 on 107-340). Block ids only for a kind-1 table.</summary>
    internal int RefCount => _refs.Length;

    /// <summary>Test seam: true when the dataset names a shape for this state rather than falling back.</summary>
    internal bool Covers(int stateId) => Lookup(stateId) >= 0;

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetCollisionShapes(BlockState state)
    {
        if (state.IsDefault)
            return NoBoxes;

        int pooled = Lookup(state.StateId);
        if (pooled >= 0)
            return Shape(pooled);

        // Uncovered states derive conservative geometry from motion and fluid flags.
        return state.BlocksMotion && !state.IsFluid ? UnitCube : NoBoxes;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetCollisionShapes(int stateId)
    {
        int pooled = Lookup(stateId);
        return pooled >= 0 ? Shape(pooled) : stateId == 0 ? NoBoxes : UnitCube;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetOutlineShapes(BlockState state) => GetCollisionShapes(state);

    /// <inheritdoc/>
    public ReadOnlySpan<Aabb> GetOutlineShapes(int stateId) => GetCollisionShapes(stateId);

    /// <summary>The pool index for a state id, or -1 when this band does not cover it.</summary>
    private int Lookup(int stateId)
    {
        if (stateId < 0)
            return -1;

        int index = _byBlockId ? stateId >> 4 : stateId;
        if (index >= _refs.Length)
            return -1;

        return _refs[index] - 1;
    }

    private ReadOnlySpan<Aabb> Shape(int poolIndex)
    {
        if (poolIndex + 1 >= _offsets.Length)
            return NoBoxes;

        int start = _offsets[poolIndex];
        return _boxes.AsSpan(start, _offsets[poolIndex + 1] - start);
    }

    /// <summary>Reads the packed AABB pool: <c>[VarInt shapeCount][per shape: VarInt boxCount][6 big-endian doubles per box]</c>, in the block-local 0..1 coordinates the physics engine expects.</summary>
    private static (Aabb[] Boxes, int[] Offsets) ReadPool(ReadOnlySpan<byte> table)
    {
        if (table.IsEmpty)
            return (NoBoxes, [0]);

        var reader = new PacketReader(table);
        int shapeCount = reader.ReadVarInt();
        var offsets = new int[shapeCount + 1];
        List<Aabb> boxes = [];
        for (int shape = 0; shape < shapeCount; shape++)
        {
            offsets[shape] = boxes.Count;
            int boxCount = reader.ReadVarInt();
            for (int box = 0; box < boxCount; box++)
            {
                double minX = reader.ReadDouble();
                double minY = reader.ReadDouble();
                double minZ = reader.ReadDouble();
                double maxX = reader.ReadDouble();
                double maxY = reader.ReadDouble();
                double maxZ = reader.ReadDouble();
                boxes.Add(new Aabb(minX, minY, minZ, maxX, maxY, maxZ));
            }
        }

        offsets[shapeCount] = boxes.Count;
        return ([.. boxes], offsets);
    }

    /// <summary>Reads the packed shape-reference table: <c>[VarInt kind][VarInt count][VarInt value * count]</c>, where a value of 0 means the band names no shape for that index and any other value is <c>poolIndex + 1</c>.</summary>
    /// <remarks>
    /// <para>Three kinds use explicit numeric discriminators:</para>
    /// <list type="bullet">
    /// <item><c>0</c>: indexed by block state id (flattened bands).</item>
    /// <item><c>1</c>: indexed by block NETWORK id (pre-flattening, no meta supplement). Resolving it
    /// needs <c>stateId &gt;&gt; 4</c>, which is exactly why all sixteen metas of such a block share one box.</item>
    /// <item><c>2</c>: the same per-block-id array, then
    /// <c>[VarInt overrideCount][VarInt stateId][VarInt value]*</c> for the individual legacy states whose collision differs from their block's meta-0 shape. It is EXPANDED here into a per-state array (block value copied across all sixteen metas, then the overrides applied), so the hot lookup stays one array read and no meta can lose the coverage its block had.</item>
    /// </list>
    /// <para>An unknown kind is treated as "no data" rather than misread, which degrades to the flag-derived fallback instead of answering geometry from a format nobody wrote.</para>
    /// </remarks>
    private static (int[] Refs, bool ByBlockId) ReadRefs(ReadOnlySpan<byte> table)
    {
        if (table.IsEmpty)
            return ([], false);

        var reader = new PacketReader(table);
        int kind = reader.ReadVarInt();
        if (kind is not (0 or 1 or 2))
            return ([], false);

        int count = reader.ReadVarInt();
        var refs = new int[count];
        for (int i = 0; i < count; i++)
            refs[i] = reader.ReadVarInt();

        if (kind != 2)
            return (refs, kind == 1);

        // Expand block ids to legacy states: every meta starts on its block's shape, so the supplement can only ever REFINE coverage. LegacyMetaBits is vanilla's own (id << 4) | meta packing.
        var byState = new int[count << LegacyMetaBits];
        for (int blockId = 0; blockId < count; blockId++)
        {
            int value = refs[blockId];
            if (value == 0)
                continue;

            int start = blockId << LegacyMetaBits;
            for (int meta = 0; meta < 1 << LegacyMetaBits; meta++)
                byState[start + meta] = value;

        }

        int overrides = reader.ReadVarInt();
        for (int i = 0; i < overrides; i++)
        {
            int stateId = reader.ReadVarInt();
            int value = reader.ReadVarInt();
            if ((uint)stateId < (uint)byState.Length)
                byState[stateId] = value;

        }

        return (byState, false);
    }
}
