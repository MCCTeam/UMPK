namespace Umpk.Client.Internal;

/// <summary>Resolves the identity of an attribute modifier across vanilla's 1.21 UUID-to-Identifier change, so a consumer can name one modifier the same way on every protocol.</summary>
/// <remarks>
/// <para>Below 1.21 an <c>AttributeModifier</c> is keyed by a UUID on the wire (<c>AttributeModifierEntry.LegacyUuid</c>); from 1.21 it is keyed by a namespaced string (<c>AttributeModifierEntry.ModernId</c>). <c>Umpk.Game.Entities.AttributeModifier</c> is keyed by <see cref="Identifier"/> either way, so the legacy UUIDs need a bridge and this is it.</para>
/// <para><b>Why getting ONE row wrong is the worst bug available here.</b> The bridge exists mainly for <c>minecraft:sprinting</c>. Vanilla's server installs a transient <c>ADD_MULTIPLIED_TOTAL +0.3</c> sprint modifier on its copy of the player, and the syncable <c>movement_speed</c> value is sent back to the owning player. UMPK's engine applies the same x1.3 itself from the tick's own input (<c>PhysicsConditions.BaseMovementSpeedAttribute</c> is sprint-free BY CONTRACT), so the wire copy must be excluded through <c>AttributeInstance.ValueExcluding(minecraft:sprinting)</c>. If the bridge fails to recognise the UUID, the modifier lands under an unrecognised id, the exclusion misses, and the resolved speed is 1.69x rather than 1.3x. That is exactly the failure <c>AttributeInstance.ValueExcluding</c>'s own remarks were written to warn about.</para>
/// <para><b>The complete classification of <c>movement_speed</c> modifiers a player can receive:</b></para>
/// <list type="table">
/// <item><term><c>minecraft:sprinting</c></term><description>
/// ADD_MULTIPLIED_TOTAL +0.3, UUID <c>662A6B8D-DA3E-4C1C-8813-96EA6097278D</c> <b>EXCLUDED</b>: the engine applies it.
/// </description></item>
/// <item><term><c>minecraft:effect.speed</c></term><description>
/// ADD_MULTIPLIED_TOTAL +0.2. <b>INCLUDED, deliberately.</b>
/// </description></item>
/// <item><term><c>minecraft:effect.slowness</c></term><description>
/// ADD_MULTIPLIED_TOTAL -0.15. <b>INCLUDED, deliberately.</b>
/// </description></item>
/// <item><term><c>minecraft:enchantment.soul_speed</c></term><description>
/// ADD_VALUE, UUID <c>87f46a96-686f-4796-b035-22e16ee9e038</c> <b>INCLUDED</b>. This is the soul-speed boost, which is computed by the server.
/// </description></item>
/// <item><term><c>minecraft:powder_snow</c></term><description>
/// ADD_VALUE (negative), UUID <c>1eaf83ff-7207-4596-b37a-d7a07b3ec4ce</c> <b>INCLUDED while item effects remain outside the engine.</b> The moment the engine models powder-snow slowdown, this becomes the second excluded row, for the identical reason sprint is the first. That change must add the exclusion in the same commit that models the slowdown, or it will ship a double-apply.
/// </description></item>
/// <item><term>item / armour <c>attribute_modifiers</c>, and <c>/attribute</c></term><description>
/// any operation, arbitrary ids. <b>INCLUDED</b> - which is why an unrecognised UUID must become a UNIQUE synthesised id rather than a shared one: two unknown modifiers that collided in the id-keyed dictionary would silently delete one of them.
/// </description></item>
/// </list>
/// <para>Speed and slowness are included ON PURPOSE, and that is a contract rather than an oversight: <c>PhysicsConditions.BaseMovementSpeedAttribute</c> is specified as "base plus equipment plus speed/slowness effects", and <c>PhysicsEngineHolder</c>'s <c>ConditionEffect</c> enum deliberately does not model them (it is exactly JumpBoost, Levitation, SlowFalling, DolphinsGrace, WaterBreathing, ConduitPower). Any future change that models Speed or Slowness in the engine must add them to the exclusion set in the SAME commit.</para>
/// </remarks>
internal static class AttributeModifierIds
{
    /// <summary>The sprint modifier's canonical id. This is the one modifier <c>PhysicsEngineHolder.ReadMovementSpeed</c> excludes.</summary>
    public static readonly Identifier Sprinting = Identifier.Minecraft("sprinting");

    /// <summary>The soul-speed boost's canonical id, for tests and diagnostics.</summary>
    public static readonly Identifier SoulSpeed = Identifier.Minecraft("enchantment.soul_speed");

    /// <summary>The powder-snow slowdown's canonical id.</summary>
    public static readonly Identifier PowderSnow = Identifier.Minecraft("powder_snow");

    /// <summary>The namespace unknown legacy modifier UUIDs are synthesised under. Deliberately not <c>minecraft</c>: an id UMPK invented must never be mistakable for one vanilla declares.</summary>
    private const string SynthesisedNamespace = "umpk";

    private static readonly Dictionary<Guid, Identifier> LegacyUuids = new()
    {
        [Guid.Parse("662A6B8D-DA3E-4C1C-8813-96EA6097278D")] = Sprinting,
        [Guid.Parse("87f46a96-686f-4796-b035-22e16ee9e038")] = SoulSpeed,
        [Guid.Parse("1eaf83ff-7207-4596-b37a-d7a07b3ec4ce")] = PowderSnow,
    };

    /// <summary>The canonical identifier for a wire modifier entry: the namespaced id when the protocol carries one (1.21+), the bridged identity when the UUID is one vanilla declares, and a synthesised <c>umpk:modifier/&lt;guid&gt;</c> otherwise.</summary>
    /// <remarks>The synthesised form is per-UUID rather than a single shared "unknown" id on purpose. An <c>AttributeInstance</c> holds at most one modifier per id, so folding every unrecognised modifier onto one key would drop all but the last of a stack's item modifiers and quietly change the resolved value. A distinct id keeps them all and resolves them exactly as vanilla does.</remarks>
    public static Identifier Resolve(Guid legacyUuid, string? modernId)
    {
        if (!string.IsNullOrEmpty(modernId) && Identifier.TryParse(modernId, out Identifier parsed))
            return parsed;

        return LegacyUuids.TryGetValue(legacyUuid, out Identifier known)
            ? known
            : new Identifier(SynthesisedNamespace, $"modifier/{legacyUuid:D}");
    }
}
