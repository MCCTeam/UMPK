namespace Umpk.Game.Inventory;

/// <summary>A half-open range of slot indices <c>[Start, End)</c> plus its fill direction. Shift-click (quick-move) resolves a source slot to an ordered list of these target ranges.</summary>
/// <param name="Start">The inclusive lower slot index.</param>
/// <param name="End">The exclusive upper slot index.</param>
/// <param name="Reverse">When true, fill from <c>End-1</c> down to <c>Start</c>.</param>
public readonly record struct SlotRange(int Start, int End, bool Reverse = false)
{
    /// <summary>True when <paramref name="slot"/> falls in <c>[Start, End)</c>.</summary>
    public bool Contains(int slot) => slot >= Start && slot < End;
}
