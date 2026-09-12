namespace Umpk.DataGen;

/// <summary>One block's resolved attributes, in the order <c>BlockDefs</c> emits its blocks.</summary>
internal sealed record BlockAttributeRow(
    string Name,
    int BlockId,
    double Friction,
    double SpeedFactor,
    double JumpFactor,
    int BaseFlags,
    IReadOnlyList<(string Name, IReadOnlyList<string> Values)> Properties,
    IReadOnlyList<int>? StateFlags);

/// <summary>Mirrors <c>Umpk.Game.Blocks.BlockFlags</c>. Duplicated here rather than referenced because DataGen is a standalone tool with no dependency on the runtime assemblies; the two must agree, and the reader-side test asserts concrete flag values for concrete blocks, which is what would catch drift.</summary>
[Flags]
internal enum EmittedBlockFlags
{
    None = 0,
    Air = 1 << 0,
    Fluid = 1 << 1,
    Waterlogged = 1 << 2,
    BlocksMotion = 1 << 3,
    Solid = 1 << 4,
    Climbable = 1 << 5,
    Replaceable = 1 << 6,
}

internal sealed partial class Emitter
{
    /// <summary>Turns a version's block table plus the curated attribute table into one row per block.</summary>
    /// <remarks>
    /// Sources, and what happens when one is absent:
    /// <list type="bullet">
    /// <item>Friction / speed / jump: curated per-block; vanilla defaults (0.6 / 1.0 / 1.0) otherwise.
    /// These are per-BLOCK in vanilla, not per-state, so no state loop is needed.</item>
    /// <item>Air / Fluid: the block's own identifier against the curated lists.</item>
    /// <item>Climbable / Replaceable: the curated flag lists for the dataset's ERA (the pre-flattening
    /// mirror names the same blocks under the identifiers 1.8-1.12.2 actually use), plus air and fluids being replaceable.</item>
    /// <item>BlocksMotion / Solid: the per-state collision shapes shipped in
    /// <c>block-shape-refs.json</c> (flat) or <c>shapes.json#collision_by_block_id</c> (legacy). A state with no collision box does not block motion; a state whose only box is the full unit cube is solid. Legacy datasets only list the handful of ids that are NOT full cubes, so an unlisted legacy block is a full cube, which is that dataset's documented convention.</item>
    /// <item>Waterlogged: read off the resolved <c>waterlogged</c> property value for that state, so it
    /// is only ever set where the property domains resolved, PLUS the curated <c>intrinsically_waterlogged</c> list for the blocks whose <c>getFluidState</c> is an unconditional water source and which therefore have no such property to read.</item>
    /// </list>
    /// </remarks>
    private static IEnumerable<BlockAttributeRow> ResolveBlockAttributes(VersionData version, BlockAttributeTable attributes)
    {
        bool legacy = version.Identity == "legacy";
        foreach (BlockEntry entry in version.Blocks.Blocks)
        {
            if (entry.Name is not { Length: > 0 } rawName || entry.BlockId is not int blockId)
                continue;

            string name = rawName.Contains(':', StringComparison.Ordinal) ? rawName : "minecraft:" + rawName;

            int min, max;
            if (entry.MinState is int minState && entry.MaxState is int maxState)
                (min, max) = (minState, maxState);

            else if (entry.StateId is int stateId)
                (min, max) = (stateId, stateId);

            else
                continue;

            int stateCount = Math.Max(1, (max - min) + 1);

            IReadOnlyList<string> propertyNames = entry.Properties ?? [];
            IReadOnlyList<IReadOnlyList<string>>? domains =
                attributes.ResolveDomains(name, propertyNames, stateCount);

            List<(string, IReadOnlyList<string>)> properties = [];
            for (int i = 0; i < propertyNames.Count; i++)
                properties.Add((propertyNames[i], domains is null ? [] : domains[i]));

            int waterloggedIndex = -1;
            if (domains is not null)
                for (int i = 0; i < propertyNames.Count; i++)
                    if (propertyNames[i] == "waterlogged")
                    {
                        waterloggedIndex = i;
                        break;
                    }

            bool isAir = attributes.Air.Contains(name);
            bool isFluid = attributes.Fluid.Contains(name);

            var semantic = EmittedBlockFlags.None;
            if (isAir)
                semantic |= EmittedBlockFlags.Air | EmittedBlockFlags.Replaceable;

            if (isFluid)
                semantic |= EmittedBlockFlags.Fluid | EmittedBlockFlags.Replaceable;

            if (attributes.ClimbableFor(legacy).Contains(name))
                semantic |= EmittedBlockFlags.Climbable;

            if (attributes.ReplaceableFor(legacy).Contains(name))
                semantic |= EmittedBlockFlags.Replaceable;

            // Blocks that CONTAIN water on every state and have no property to say so. Set here, on the whole-block semantic flags, rather than in the per-state loop below, because it is per-block by construction: vanilla's override takes the BlockState and ignores it.
            if (attributes.IntrinsicallyWaterlogged.Contains(name))
                semantic |= EmittedBlockFlags.Waterlogged;

            IReadOnlyList<int>? shapeRefs = CollisionRefs(version, legacy, name, blockId, stateCount);

            var stateFlags = new int[stateCount];
            for (int offset = 0; offset < stateCount; offset++)
            {
                EmittedBlockFlags flags = semantic;

                if (!isAir && !isFluid)
                {
                    int shapeIndex = shapeRefs is not null && offset < shapeRefs.Count ? shapeRefs[offset] : -1;
                    (bool blocks, bool solid) = ClassifyShape(version, shapeIndex);
                    if (blocks)
                        flags |= EmittedBlockFlags.BlocksMotion;

                    if (solid)
                        flags |= EmittedBlockFlags.Solid;

                }

                if (waterloggedIndex >= 0
                    && BlockAttributeTable.Decompose(domains!, offset)[waterloggedIndex] == "true")
                    flags |= EmittedBlockFlags.Waterlogged;

                stateFlags[offset] = (int)flags;
            }

            int baseFlags = stateFlags[0];
            bool uniform = true;
            foreach (int f in stateFlags)
                if (f != baseFlags)
                {
                    uniform = false;
                    break;
                }

            yield return new BlockAttributeRow(
                name,
                blockId,
                attributes.Friction.TryGetValue(name, out double friction) ? friction : 0.6,
                attributes.SpeedFactor.TryGetValue(name, out double speed) ? speed : 1.0,
                attributes.JumpFactor.TryGetValue(name, out double jump) ? jump : 1.0,
                baseFlags,
                properties,
                uniform ? null : stateFlags);
        }
    }

    /// <summary>The per-state collision shape indices for one block, or null when the dataset has none. Legacy datasets key a single shape per block id; flat datasets carry one index per state.</summary>
    private static IReadOnlyList<int>? CollisionRefs(VersionData version, bool legacy, string name, int blockId, int stateCount)
    {
        if (legacy)
        {
            if (version.Shapes.CollisionByBlockId is { } byId
                && byId.TryGetValue(blockId.ToString(System.Globalization.CultureInfo.InvariantCulture), out int index))
                return [index];

            // Documented legacy convention: an id the curated shape table does not list is a full cube.
            return null;
        }

        if (version.Shapes.Collision is { } collision && collision.TryGetValue(name, out IReadOnlyList<int>? refs))
            return refs;

        return null;
    }

    /// <summary>Classifies a collision shape into (blocks motion, is a full solid cube). A negative index means the dataset has no shape for this state, in which case a non-air, non-fluid block is treated as a full cube, which is what the shape datasets themselves document for unlisted entries.</summary>
    private static (bool BlocksMotion, bool Solid) ClassifyShape(VersionData version, int shapeIndex)
    {
        if (shapeIndex < 0 || shapeIndex >= version.Shapes.Shapes.Count)
            return (true, true);

        IReadOnlyList<IReadOnlyList<double>> boxes = version.Shapes.Shapes[shapeIndex];
        if (boxes.Count == 0)
            return (false, false);

        if (boxes.Count == 1 && IsFullCube(boxes[0]))
            return (true, true);

        return (true, false);
    }

    private static bool IsFullCube(IReadOnlyList<double> box)
        => box.Count == 6
        && box[0] == 0 && box[1] == 0 && box[2] == 0
        && box[3] == 1 && box[4] == 1 && box[5] == 1;
}
