using Umpk.Game.Registries;

namespace Umpk.Game.Items;

/// <summary>One enchantment applied at a level. The enchantment is a registry handle so the same value type serves both the modern id-keyed registry and the legacy numeric-id bridge (which resolves the id to a handle at the model boundary). Levels are 1-based as on the wire.</summary>
/// <param name="Enchantment">The enchantment registry entry.</param>
/// <param name="Level">The enchantment level (1-based).</param>
public readonly record struct EnchantmentInstance(RegistryEntry<EnchantmentDefinition> Enchantment, int Level);
