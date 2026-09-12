namespace Umpk.Game.Entities;

/// <summary>The three attribute-modifier operations. Their numeric values are the wire ids: add_value = 0, add_multiplied_base = 1, and add_multiplied_total = 2.</summary>
public enum AttributeModifierOperation
{
    /// <summary>Adds the raw amount to the running total (applied first).</summary>
    AddValue = 0,

    /// <summary>Adds <c>base * amount</c> to the running total (applied second, all relative to the post-AddValue base).</summary>
    AddMultipliedBase = 1,

    /// <summary>Multiplies the running total by <c>1 + amount</c> (applied last, compounding).</summary>
    AddMultipliedTotal = 2,
}

/// <summary>A single attribute modifier: an identifier that makes it de-duplicable, an amount, and an operation. Identity is the <see cref="Id"/>; an attribute holds at most one modifier per id.</summary>
/// <param name="Id">The namespaced modifier id used by modern protocols.</param>
/// <param name="Amount">The modifier amount, interpreted per <paramref name="Operation"/>.</param>
/// <param name="Operation">How the amount combines into the resolved value.</param>
public sealed record AttributeModifier(Identifier Id, double Amount, AttributeModifierOperation Operation);
