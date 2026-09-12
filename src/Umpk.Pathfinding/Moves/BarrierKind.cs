namespace Umpk.Pathfinding.Moves;

/// <summary>What a door, trapdoor or fence gate is to a plan.</summary>
/// <remarks>
/// <para>For this family, the <c>open</c> property decides whether a body may occupy the cell. The passability rule does not read the collision shape.</para>
/// <para>Opening a door does not remove its collision box; it rotates the 3/16 panel ninety degrees onto another face. <c>oak_door[facing=east,half=lower,hinge=left,open=false]</c> has shape <c>[[0,0,0,0.1875,1,1]]</c> and the same state with <c>open=true</c> is shape 4 <c>[[0,0,0,1,1,0.1875]]</c>. Every door state carries a box, so a predicate ending in <c>!state.BlocksMotion</c> refuses an open door exactly as it refuses a closed one.</para>
/// </remarks>
public enum BarrierKind
{
    /// <summary>Not a door, trapdoor or fence gate. The ordinary passability rules decide the cell.</summary>
    None = 0,

    /// <summary>Open, and therefore passable with no interaction at all. A door or trapdoor in this state still leaves a 3/16 panel standing on one face of the cell, so the cell has 0.8125 blocks of free width for a 0.6-wide body; an open fence gate has the empty shape and nothing at all.</summary>
    PassableNow = 1,

    /// <summary>Closed, and one <c>use_item_on</c> away from open: every wooden and copper door, every wooden and copper trapdoor, and every fence gate.</summary>
    /// <remarks>
    /// <para>Iron doors and iron trapdoors cannot be opened by hand. Wooden and copper variants can. Fence gates are hand-openable.</para>
    /// <para>A hand-opened barrier stays open until another action changes it. This kind therefore carries no time window, while an activator-driven one must.</para>
    /// </remarks>
    NeedsInteraction = 2,

    /// <summary>Closed, not hand-openable, and with a button or lever in reach that powers it: the two iron blocks, when the world provides a switch. See <see cref="DoorActivation"/> for what counts as powering the door and for the window a button imposes on the crossing.</summary>
    /// <remarks>Resolving this is not a property read - it is a search of the door's redstone neighbourhood plus a search for a cell to stand in - so <c>MoveHelper.ClassifyBarrier(BlockState)</c> cannot answer it and never returns it. Only the context-aware overload does, and it memoises per door.</remarks>
    NeedsActivator = 3,

    /// <summary>Impassable to this plan: closed and not hand-openable with nothing to power it, or on a source that cannot resolve <c>open</c> at all (every pre-flattening protocol, where <c>BlockState.TryGetProperty</c> answers false). The second case is a deliberate refusal rather than a metadata guess.</summary>
    Wall = 4,
}
