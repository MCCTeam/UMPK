using Umpk.Game.Registries;

namespace Umpk.Game.Entities;

/// <summary>One attribute on an entity: a base value plus a set of <see cref="AttributeModifier"/>s keyed by id. <see cref="Value"/> resolves modifiers in protocol order (add_value, then add_multiplied_base, then add_multiplied_total) and, for ranged attributes, sanitizes the result (NaN maps to the minimum, then clamp to [min, max]); non-ranged attributes pass through.</summary>
public sealed class AttributeInstance
{
    private readonly Dictionary<Identifier, AttributeModifier> _modifiers = [];

    /// <summary>Creates an instance for a resolved attribute, defaulting the base to the definition default.</summary>
    /// <param name="attribute">The attribute registry handle (carries the default/min/max).</param>
    public AttributeInstance(RegistryEntry<AttributeDefinition> attribute)
    {
        Attribute = attribute;
        BaseValue = attribute.IsDefault ? 0.0 : attribute.Value.DefaultValue;
    }

    /// <summary>The attribute this instance resolves.</summary>
    public RegistryEntry<AttributeDefinition> Attribute { get; }

    /// <summary>The base value before modifiers. Server updates set this directly.</summary>
    public double BaseValue { get; set; }

    /// <summary>The current modifiers, keyed by id.</summary>
    public IReadOnlyCollection<AttributeModifier> Modifiers => _modifiers.Values;

    /// <summary>Adds or replaces the modifier with <paramref name="modifier"/>'s id.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="modifier"/> is null.</exception>
    public void AddOrReplaceModifier(AttributeModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        _modifiers[modifier.Id] = modifier;
    }

    /// <summary>Removes the modifier with the given id; returns true when one was removed.</summary>
    public bool RemoveModifier(Identifier id) => _modifiers.Remove(id);

    /// <summary>True when a modifier with the given id is present.</summary>
    public bool HasModifier(Identifier id) => _modifiers.ContainsKey(id);

    /// <summary>Gets the modifier with the given id, if present.</summary>
    public bool TryGetModifier(Identifier id, out AttributeModifier modifier) => _modifiers.TryGetValue(id, out modifier!);

    /// <summary>Removes every modifier.</summary>
    public void ClearModifiers() => _modifiers.Clear();

    /// <summary>The resolved final value: base + add_value amounts, then add_multiplied_base against that sum, then compounding add_multiplied_total, clamped to the definition's [min, max].</summary>
    public double Value => Resolve(excluded: null);

    /// <summary>The resolved value with the modifier carrying <paramref name="excludedId"/> left out, resolved otherwise exactly as <see cref="Value"/> is (same operation order, same sanitization). When no modifier carries that id the result is <see cref="Value"/>.</summary>
    /// <remarks>This exists for the modifiers a consumer applies for itself and must therefore not receive twice. The concrete case is <c>minecraft:sprinting</c> on <c>minecraft:movement_speed</c>: the server installs that transient modifier when a client announces sprint, and broadcasts the syncable attribute back. A physics layer that applies the sprint multiply itself from the tick's input (which UMPK's does, see <c>Umpk.Physics.PhysicsConditions.BaseMovementSpeedAttribute</c>) must therefore read the attribute through this and not through <see cref="Value"/>, or the modifier lands twice. <see cref="AddOrReplaceModifier"/>'s id keying does NOT cover that: it dedups two modifiers in the same map, not a map entry against a multiply performed somewhere else.</remarks>
    public double ValueExcluding(Identifier excludedId) => Resolve(excludedId);

    private double Resolve(Identifier? excluded)
    {
        double running = BaseValue;
        foreach (AttributeModifier modifier in _modifiers.Values)
            if (modifier.Operation == AttributeModifierOperation.AddValue && !IsExcluded(modifier, excluded))
                running += modifier.Amount;

        double afterBaseAdd = running;
        double result = running;
        foreach (AttributeModifier modifier in _modifiers.Values)
            if (modifier.Operation == AttributeModifierOperation.AddMultipliedBase && !IsExcluded(modifier, excluded))
                result += afterBaseAdd * modifier.Amount;

        foreach (AttributeModifier modifier in _modifiers.Values)
            if (modifier.Operation == AttributeModifierOperation.AddMultipliedTotal && !IsExcluded(modifier, excluded))
                result *= 1.0 + modifier.Amount;

        return Sanitize(result);
    }

    private static bool IsExcluded(AttributeModifier modifier, Identifier? excluded)
        => excluded is not null && modifier.Id == excluded;

    // Unbounded attributes preserve the value. Ranged attributes map NaN to the minimum and clamp to [min, max].
    private double Sanitize(double value)
    {
        if (Attribute.IsDefault)
            return value;

        AttributeDefinition definition = Attribute.Value;
        if (!definition.IsRanged)
            return value;

        if (double.IsNaN(value))
            return definition.MinValue;

        return Math.Clamp(value, definition.MinValue, definition.MaxValue);
    }
}
