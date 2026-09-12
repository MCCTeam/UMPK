using Umpk.Game.Players;

namespace Umpk.Physics;

/// <summary>
/// The game-state snapshot the host pushes into the engine through <see cref="PlayerPhysics.SetConditions"/>. The engine NEVER reads these from anywhere itself: abilities, effects, equipment, and the resolved movement-speed attribute are computed by the integration layer from the entity attribute map and effect list and pushed here whenever they change.
///
/// <para>Pushed on change, not per tick: only refresh when abilities/effects/gamemode/equipment change. <see cref="BaseMovementSpeedAttribute"/> is the resolved value EXCLUDING the sprint modifier, which the engine applies itself from the per-tick input; see that member.</para>
/// </summary>
public readonly record struct PhysicsConditions
{
    /// <summary>Whether creative/spectator flight is currently active (flying behavior).</summary>
    public bool CreativeFlying { get; init; }

    /// <summary>Whether the player may fly (mayfly behavior). Affects fall-damage semantics for hosts.</summary>
    public bool MayFly { get; init; }

    /// <summary>The current game mode.</summary>
    public GameMode GameMode { get; init; }

    /// <summary>The resolved movement-speed attribute value (Attributes.MOVEMENT_SPEED) with the <c>minecraft:sprinting</c> modifier EXCLUDED: base plus equipment plus speed/slowness effects, and nothing else. Default player value is 0.1.</summary>
    /// <remarks>
    /// <para>The sprint modifier is deliberately not part of this value. The engine knows the per-tick sprint state and multiplies by <see cref="PhysicsConstants.SprintSpeedModifier"/> when the input holds Sprint. This split reproduces the resolved value without double application.</para>
    /// <para>A host that resolves this from a real attribute map must therefore exclude the <c>minecraft:sprinting</c> modifier when it reads it (see <c>Umpk.Game.Entities.AttributeInstance.ValueExcluding</c>). Folding the modifier in here and letting the engine apply it again is a 69% overspeed, not a 30% one.</para>
    /// </remarks>
    public float BaseMovementSpeedAttribute { get; init; }

    /// <summary>The resolved fly speed (flying speed behavior); default 0.05. Used by creative-fly travel. Sprinting doubling is applied by the engine.</summary>
    public float FlyingSpeed { get; init; }

    /// <summary>Jump-boost effect amplifier (0 when absent; power = 0.1 * amplifier when present).</summary>
    public int JumpBoostAmplifier { get; init; }

    /// <summary>Whether the jump-boost effect is present.</summary>
    public bool HasJumpBoost { get; init; }

    /// <summary>Whether the slow-falling effect is present (caps downward gravity).</summary>
    public bool HasSlowFalling { get; init; }

    /// <summary>Levitation effect amplifier (used when <see cref="HasLevitation"/>).</summary>
    public int LevitationAmplifier { get; init; }

    /// <summary>Whether the levitation effect is present (overrides gravity with upward drift).</summary>
    public bool HasLevitation { get; init; }

    /// <summary>Whether the dolphins-grace effect is present (water slow-down override).</summary>
    public bool HasDolphinsGrace { get; init; }

    /// <summary>Water movement efficiency attribute (Attributes.WATER_MOVEMENT_EFFICIENCY); default 0.</summary>
    public float WaterMovementEfficiency { get; init; }

    /// <summary>The factor a crouching tick scales the raw movement impulse by (Attributes.SNEAKING_SPEED); default 0.3. Swift sneak REPLACES this value, it does not multiply it a second time.</summary>
    /// <remarks>
    /// <para>The factor applies to movement impulse, not resolved speed. In enchantment-based protocols it is <c>clamp(0.3 + 0.15 * level, 0, 1)</c>; attribute-based protocols expose the same value as <c>minecraft:sneaking_speed</c>.</para>
    /// <para>This is a <c>float</c> because the attribute read narrows there, and the narrowing is load bearing: resolved in double the 1.21 attribute gives <c>0.7500000178813935</c> at level 3, not <c>0.75</c>, because the modifier amount is a <c>float</c> widened for the attribute API. A host that resolves this from a real attribute map must narrow at the read or it diverges at every level.</para>
    /// <para>A host that PREDICTS what the engine will do with a crouching press - the sub-block approach controller is the one that does - must read this value rather than keep its own copy of 0.3. The two diverging is not a cosmetic drift: under swift sneak III the controller would predict a press landing at 40% of where it actually lands, and then mis-rank coast against sneak against walk.</para>
    /// </remarks>
    public float SneakingSpeedFactor { get; init; }

    /// <summary>The movement-efficiency attribute (Attributes.MOVEMENT_EFFICIENCY), in [0,1]; default 0. It LERPS the block speed factor toward 1, so 1 is "this floor does not slow me at all" and 0 is "leave the floor's own factor alone".</summary>
    /// <remarks>
    /// <para>From 1.21, the block speed factor is interpolated toward 1 by the movement-efficiency attribute. Soul speed's data-driven definition adds <c>MOVEMENT_EFFICIENCY = 1.0</c> while the wearer stands on a soul-speed block, which makes the lerp return exactly 1.</para>
    /// <para>A host on 1.21+ pushes the attribute here and nothing else; below that it pushes <see cref="SoulSpeedLevel"/> instead and this stays 0. The two are never both non-zero, so the engine's <c>max</c> of them is not a blend of live sources - it just keeps an era branch out of the engine, which is the worst place for one.</para>
    /// </remarks>
    public float MovementEfficiency { get; init; }

    /// <summary>The soul-speed enchantment level on the boots, or 0. Consumed POSITIONALLY: the engine applies the bypass only while the block below the feet is a soul-speed block. Zero on 1.21+, where <see cref="MovementEfficiency"/> carries the same fact off the wire instead.</summary>
    /// <remarks>
    /// <para>On 1.16-1.20.6, the client computes this bypass every tick. A positive level makes the block speed factor 1 only while the movement-affecting block below the feet is soul sand or soul soil.</para>
    /// <para>A LEVEL is pushed rather than a resolved efficiency on purpose. The resolved answer depends on the block underfoot, which changes every tick, while the equipment changes rarely; pushing a per-tick value into a frozen snapshot would either be stale or force an invalidation every tick. This is the same split the water-movement-efficiency read already makes when it leaves the off-ground halving to the engine.</para>
    /// <para>The engine never synthesises soul speed's boost from this level. The server supplies it as an <c>update_attributes</c> modifier folded into <see cref="BaseMovementSpeedAttribute"/>. Computing it here would apply it twice.</para>
    /// </remarks>
    public int SoulSpeedLevel { get; init; }

    /// <summary>Whether this body may walk on powder snow. For a player that is leather boots in the feet slot and nothing else; no other boot material and no enchantment does it.</summary>
    /// <remarks>
    /// <para>The player predicate is true only for leather boots. The engine takes the resolved answer rather than an item id for the same reason it takes <see cref="BaseMovementSpeedAttribute"/> rather than an attribute map: what the body is wearing is the integration layer's business, and the engine only ever needs the one bit.</para>
    /// <para>It is a whole bit rather than part of the shape table because it is resolved per body at the collision call site. Shape data has no entity context and records powder snow as empty, so <see cref="PowderSnowCollision"/> synthesises the cube here. That is exactly the division <see cref="ScaffoldingCollision"/> already makes, and it is why neither feature needs a dataset regeneration.</para>
    /// <para><b>It grants no speed change.</b> The <c>minecraft:powder_snow</c> modifier on <c>movement_speed</c> is <c>-0.05F * frozenPercent</c> and applies only while frozen ticks are positive. Ticks frozen accumulate only while the body is in powder snow and can freeze. Freeze-immune leather wearables make that predicate false. So the body this flag describes can never carry that modifier, and a body walking on top of the snow is not inside it. When one does arrive it arrives folded into <see cref="BaseMovementSpeedAttribute"/> off the wire, like speed and slowness, and it must NOT be added to the applier's exclusion set: the engine computes no frost of its own, so excluding it would drop a real slowdown rather than prevent a double one.</para>
    /// </remarks>
    public bool PowderSnowWalkable { get; init; }

    /// <summary>Whether an elytra is equipped in the chest slot.</summary>
    public bool ElytraEquipped { get; init; }

    /// <summary>Whether the player is currently fall-flying (gliding). The host owns the start/stop state transitions (server-authoritative shared flag 7); the engine consumes this to select the elytra travel branch.</summary>
    public bool ElytraFlying { get; init; }

    /// <summary>Whether the current dimension type is ultra-warm (vanilla <c>minecraft:the_nether</c>). Lava pushes an entity roughly three times as hard there: The lava-current push scale is 0.007 in an ultra-warm dimension and 0.0023333333333333335 otherwise. Water is unaffected.</summary>
    public bool UltraWarmDimension { get; init; }

    /// <summary>The default conditions: survival, speed 0.1, fly speed 0.05, crouch factor 0.3, no effects. Every one of those is vanilla's own value for a player with nothing equipped and nothing applied, which is what lets a host push this struct unchanged and stay byte-identical.</summary>
    public static PhysicsConditions Default => new()
    {
        GameMode = GameMode.Survival,
        BaseMovementSpeedAttribute = 0.1f,
        FlyingSpeed = PhysicsConstants.DefaultFlySpeed,
        SneakingSpeedFactor = PhysicsConstants.DefaultSneakingSpeedFactor,
    };
}
