using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>Which blocks in the SHIPPED dataset a lateral squeeze lane can ever open past, swept state by state over every protocol.</summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>Umpk.Pathfinding.Moves.SqueezeLane</c> refuses climbables, hazards and the barrier family before it measures anything. Ablating either of the last two leaves the whole planning suite green, which normally means a vacuous guard - and the reason it is not, here, is a fact about the DATA rather than about the code. This file is that fact, checked rather than asserted in a comment.</para>
/// <para><b>The inequality.</b> A body offset to a cell face spans <c>[0, 0.6]</c> or <c>[0.4, 1.0]</c> of its cell, so it clears a box only if the box's near face is at or beyond 0.6 or its far face at or before 0.4. A box centred in its cell with width <c>w</c> offers <c>(1 - w)/2</c> of run, which reaches 0.6 only at <c>w &lt;= -0.2</c>. No box is negatively wide, so the only candidates are blocks whose box is NOT centred - either because the shape itself is asymmetric, or because vanilla moves it by a hash of its own position.</para>
/// </remarks>
public sealed class SqueezeLaneCandidateTests
{
    private const double PlayerWidth = 0.6;

    private const double BodyHeight = 1.8;

    /// <summary>Every protocol whose dataset carries block shapes, so a change on one band cannot hide behind another.</summary>
    public static TheoryData<int> Protocols => [107, 110, 210, 315, 335, 340, 393, 404, 477, 578, 736, 763, 770, 774, 775, 776];

    /// <summary>Sweep every state of every block and collect the ones a flush body could pass on some axis at the most favourable position the offset can reach. The answer is bamboo plus four wall-attached families, and every one of them is accounted for.</summary>
    /// <remarks>
    /// <para><b>bamboo</b> is the feature.</para>
    /// <para><b>Every door and trapdoor</b> is a 3/16 plate against one wall of its cell, so on the axis ALONG the panel a flush body has 13/16 of clear run. This is what makes <c>SqueezeLane</c>'s barrier arm necessary: geometry alone would allow a body through a CLOSED door it never opened and never paid <c>ActionCosts.InteractLatency</c> for.</para>
    /// <para><b>ladder</b> is climbable, so <c>MoveHelper.CanWalkThrough</c> admits its cell long before the squeeze arm is reached; its plate is projected on the passable path, where missing it is the right answer.</para>
    /// <para><b>cocoa, the three amethyst buds, the nine shelves (26.x) and a bitten cake</b> are partial blocks whose box hugs one face, and a flush body really does clear them - vanilla lets a player walk past every one. They are admitted deliberately, and they are only reachable at all inside a region that also holds an offset block, because <c>PlanningWorldView.MayContainShapeOffset</c> is what switches the whole arm on. That asymmetry limits the behavior to regions that may contain offset blocks.</para>
    /// <para>A block that leaves this set is a block whose shape moved, which is exactly what this theory is here to say out loud.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void OnlyBambooAndTheWallAttachedFamilies_CanEverOpenAFlushLane(int protocol)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
        var candidates = new SortedSet<string>(StringComparer.Ordinal);

        foreach (RegistryEntry<BlockDefinition> entry in blocks)
        {
            Identifier id = entry.Id;
            BlockDefinition definition = entry.Value;
            double maxOffset = MaxHorizontalOffset(id);

            for (int state = definition.MinStateId; state <= definition.MaxStateId; state++)
            {
                if ((definition.FlagsForState(state) & BlockFlags.BlocksMotion) == 0)
                    continue;

                if (LeavesAFlushLane(shapes.GetCollisionShapes(state), maxOffset))
                {
                    candidates.Add(id.Path);
                    break;
                }
            }
        }

        // Bamboo where bamboo exists: it arrives with the flattening in 1.14 (protocol 477).
        Assert.Equal(blocks.ContainsKey(Identifier.Minecraft("bamboo")), candidates.Contains("bamboo"));

        // And dripstone NEVER, on any protocol that carries it. This is the census's 0.00%.
        Assert.DoesNotContain("pointed_dripstone", candidates);

        List<string> unexpected = candidates.Where(c => !IsAccountedFor(c)).ToList();
        Assert.True(
            unexpected.Count == 0,
            $"protocol {protocol}: a block outside the accounted-for families can open a flush lane, "
                + "so SqueezeLane's refusals no longer cover the shipped data: "
                + string.Join(", ", unexpected));
    }

    /// <summary>The families enumerated in the theory's remarks, each with its reason recorded there.</summary>
    private static bool IsAccountedFor(string path) =>
        // "trapdoor" bare is the PRE-flattening name; from 1.13 the same block is oak_trapdoor.
        path is "bamboo" or "ladder" or "cake" or "cocoa" or "trapdoor"
        || path.EndsWith("_door", StringComparison.Ordinal)
        || path.EndsWith("_trapdoor", StringComparison.Ordinal)
        || path.EndsWith("_shelf", StringComparison.Ordinal)
        || path.EndsWith("_amethyst_bud", StringComparison.Ordinal);

    /// <summary>The other half of the same claim, stated as the number the planner actually depends on: dripstone's widest reachable face is short of the run a flush body needs, at EVERY position, on every protocol that carries it.</summary>
    [Theory]
    [MemberData(nameof(Protocols))]
    public void ADripstoneFaceNeverReaches_TheRunAFlushBodyNeeds(int protocol)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        if (!blocks.TryGetValue(Identifier.Minecraft("pointed_dripstone"), out BlockDefinition? definition))
            return;

        IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
        for (int state = definition.MinStateId; state <= definition.MaxStateId; state++)
            foreach (Aabb box in shapes.GetCollisionShapes(state).ToArray())
            {
                if (box.MinY >= BodyHeight - 1.0 && box.MinY >= 1.0)
                    continue;

                Assert.True(
                    box.MinX + 0.125 < PlayerWidth && box.MaxX - 0.125 > 1.0 - PlayerWidth,
                    $"protocol {protocol}: dripstone state {state} box X [{box.MinX}, {box.MaxX}] "
                        + "would leave a flush lane at some position");
            }

    }

    /// <summary>Maximum horizontal offset is 0.25 for bamboo and 0.125 for pointed dripstone.</summary>
    private static double MaxHorizontalOffset(Identifier id)
    {
        if (id == Identifier.Minecraft("pointed_dripstone"))
            return 0.125;

        return id == Identifier.Minecraft("bamboo") ? 0.25 : 0.0;
    }

    /// <summary>Whether a body flush with a cell face clears every box on at least one horizontal axis, at the most favourable position the offset can reach.</summary>
    private static bool LeavesAFlushLane(ReadOnlySpan<Aabb> boxes, double maxOffset)
    {
        if (boxes.Length == 0)
            return false;

        for (int axis = 0; axis <= 1; axis++)
        {
            double lo = double.PositiveInfinity;
            double hi = double.NegativeInfinity;
            bool any = false;

            foreach (Aabb box in boxes)
            {
                // A box entirely above the body's head cannot close a lane.
                if (box.MinY >= BodyHeight)
                    continue;

                any = true;
                lo = Math.Min(lo, axis == 0 ? box.MinX : box.MinZ);
                hi = Math.Max(hi, axis == 0 ? box.MaxX : box.MaxZ);
            }

            if (!any)
                return true;

            if (lo + maxOffset >= PlayerWidth || hi - maxOffset <= 1.0 - PlayerWidth)
                return true;

        }

        return false;
    }
}
