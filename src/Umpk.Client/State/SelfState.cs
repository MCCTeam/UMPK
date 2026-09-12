using Umpk.Game.Players;
using Umpk.Geometry;

namespace Umpk.Client.State;

/// <summary>The tracked state of the local player: identity, position/rotation, health, abilities, experience, held slot, and the effect/cooldown side-tables that feed physics conditions. Mutated only on the session loop by appliers; readable off-loop as an eventually-consistent snapshot.</summary>
public sealed class SelfState
{
    /// <summary>The local player's entity id, assigned at join.</summary>
    public int EntityId { get; internal set; }

    /// <summary>The local player's uuid.</summary>
    public Guid Uuid { get; internal set; }

    /// <summary>The local player's username.</summary>
    public string Username { get; internal set; } = string.Empty;

    /// <summary>Current position (feet).</summary>
    public Vec3d Position { get; internal set; }

    /// <summary>Current velocity.</summary>
    public Vec3d Velocity { get; internal set; }

    /// <summary>Yaw in degrees.</summary>
    public float Yaw { get; internal set; }

    /// <summary>Pitch in degrees.</summary>
    public float Pitch { get; internal set; }

    /// <summary>Whether the player is on the ground per the last physics/move step.</summary>
    public bool OnGround { get; internal set; } = true;

    /// <summary>Whether the player has received the initial position and joined the world.</summary>
    public bool HasSpawned { get; internal set; }

    /// <summary>Health (0-20 by default).</summary>
    public float Health { get; internal set; } = 20f;

    /// <summary>Whether <see cref="Health"/>, <see cref="Food"/> and <see cref="Saturation"/> have ever been written by the server, as opposed to still carrying their spawn defaults.</summary>
    /// <remarks>The defaults are a healthy, fed player, so a consumer cannot tell "20 health" from "nobody has told us yet" by value. That matters to anything that spends health as a budget: assuming full health is exactly the direction that plans a fall the bot cannot survive. Written once, by <c>SelfApplier</c> on the first <c>set_health</c>, and never cleared.</remarks>
    public bool HealthObserved { get; internal set; }

    /// <summary>Food level.</summary>
    public int Food { get; internal set; } = 20;

    /// <summary>Food saturation.</summary>
    public float Saturation { get; internal set; }

    /// <summary>Remaining breath, in ticks, counting down from <see cref="MaxAirSupply"/> while the eyes are underwater and back up four times as fast once they are not. Negative values are the drowning window: the counter resets to 0 and damages the player when it reaches -20.</summary>
    /// <remarks>Written from two places, last write wins, both on the session loop: the server's <c>set_entity_data</c> for the local player's own entity id (which is authoritative), and the client's per-tick prediction between those frames (see <c>AirSupplyRule</c>). There is no change EVENT for it, unlike <see cref="Health"/>: this value moves on every single tick spent underwater, so an event would be twenty publications a second for something a per-tick consumer already reads straight off this state or off a snapshot.</remarks>
    public int AirSupply { get; internal set; } = Internal.AirSupplyRule.TotalAirSupply;

    /// <summary>A full lung is 300 ticks on every version and for every player. Player-specific logic does not override this shared value.</summary>
    public int MaxAirSupply => Internal.AirSupplyRule.TotalAirSupply;

    /// <summary>Experience bar progress from 0 to 1.</summary>
    public float ExperienceProgress { get; internal set; }

    /// <summary>Experience level.</summary>
    public int ExperienceLevel { get; internal set; }

    /// <summary>Total experience.</summary>
    public int TotalExperience { get; internal set; }

    /// <summary>The selected hotbar slot, from 0 to 8.</summary>
    public int HeldSlot { get; internal set; }

    /// <summary>Current game mode.</summary>
    public GameMode GameMode { get; internal set; } = GameMode.Survival;

    /// <summary>Whether the player is currently sneaking (client-driven input state).</summary>
    public bool Sneaking { get; internal set; }

    /// <summary>Whether the player is currently sprinting AS ANNOUNCED: the last value sent to the server with a START_SPRINTING / STOP_SPRINTING player command.</summary>
    /// <remarks>This is the last announced value, not the current physics input. The live bit is the physics engine's per-tick input; this latch lets the tick loop send exactly one packet per transition. It is cleared on join and respawn because the rebuilt player starts with the latch false.</remarks>
    public bool Sprinting { get; internal set; }

    /// <summary>The locally held sprint key, kept separate from the last announced entity state.</summary>
    internal bool SprintRequested { get; set; }

    /// <summary>Whether the player is invulnerable per server abilities.</summary>
    public bool Invulnerable { get; internal set; }

    /// <summary>Whether the player is currently flying.</summary>
    public bool Flying { get; internal set; }

    /// <summary>Whether the player may toggle flight.</summary>
    public bool MayFly { get; internal set; }

    /// <summary>Whether creative instant-build is allowed.</summary>
    public bool InstantBuild { get; internal set; }

    /// <summary>Server-advertised flying speed.</summary>
    public float FlyingSpeed { get; internal set; } = 0.05f;

    /// <summary>Server-advertised walking speed.</summary>
    public float WalkingSpeed { get; internal set; } = 0.1f;

    /// <summary>The last death location (dimension + position) reported by respawn data, if any.</summary>
    public (string Dimension, BlockPos Position)? LastDeathLocation { get; internal set; }

    /// <summary>The current view distance advertised by the server.</summary>
    public int ViewDistance { get; internal set; }

    /// <summary>The current simulation distance advertised by the server.</summary>
    public int SimulationDistance { get; internal set; }

    /// <summary>Whether the server enforces secure chat.</summary>
    public bool EnforcesSecureChat { get; internal set; }

    /// <summary>The camera entity id when the server has set the camera to a non-self entity.</summary>
    public int? CameraEntityId { get; internal set; }

    /// <summary>Per-item cooldown expiry (item id to remaining ticks at set time).</summary>
    public IReadOnlyDictionary<int, int> ItemCooldowns => _itemCooldowns;

    private readonly Dictionary<int, int> _itemCooldowns = [];

    internal void SetItemCooldown(int itemId, int ticks)
    {
        if (ticks <= 0)
            _itemCooldowns.Remove(itemId);

        else
            _itemCooldowns[itemId] = ticks;

    }

    /// <summary>The local player's active status effects, keyed by effect network id. The server sends <c>update_mob_effect</c> / <c>remove_mob_effect</c> for the player itself (vanilla only broadcasts a mob's effect packet to the entity and its player passengers), so unlike other entities the self effects are not in the shared entity store; they are tracked here as a queryable snapshot.</summary>
    public IReadOnlyDictionary<int, ActiveEffect> ActiveEffects => _effects;

    private readonly Dictionary<int, ActiveEffect> _effects = [];

    /// <summary>Applies (or refreshes) a self status effect. Session-loop only.</summary>
    internal void ApplyEffect(ActiveEffect effect) => _effects[effect.EffectId] = effect;

    /// <summary>Removes a self status effect by id; returns true when one was present. Session-loop only.</summary>
    internal bool RemoveEffect(int effectId) => _effects.Remove(effectId);

    /// <summary>Drops every self status effect. Session-loop only.</summary>
    /// <remarks>Called for a respawn that destroys the player entity, which is the one transition after which the table is stale and no packet says so: vanilla's server never sends <c>remove_mob_effect</c> for the effects a death took away, it simply re-sends whatever the freshly built <c>ServerPlayer</c> has, which after a death is nothing. See <c>ConnectionApplier.ApplyRespawnAsync</c> for which respawns qualify.</remarks>
    internal void ClearEffects() => _effects.Clear();

    /// <summary>The local player's own attribute map, seeded from player defaults and updated by <c>update_attributes</c>. It is tracked here because the local player is not a member of the shared <c>EntityStore</c>.</summary>
    public SelfAttributes Attributes { get; } = new();
}

/// <summary>One active status effect on the local player: the effect network id, amplifier (0 = level I), the duration in ticks AS APPLIED (-1 = infinite), the packed flags (ambient/particles/icon), and the session tick the apply landed on.</summary>
/// <remarks><see cref="Duration"/> is the wire value verbatim and is NEVER decremented, because the wire value is the only number the server ever states: the server sends an update on add or refresh and a removal on expiry, with nothing in between. Decrementing it in place would replace an observation with a prediction that no later packet corrects, which for a long potion is never. The derived number lives in <see cref="RemainingTicksAt"/> instead, the same predict-but-keep-the-observation discipline <c>AirSupplyRule</c> follows.</remarks>
/// <param name="EffectId">The effect network (registry) id.</param>
/// <param name="Amplifier">The amplifier; 0 is level I.</param>
/// <param name="Duration">The duration in ticks as the server applied it; -1 for infinite.</param>
/// <param name="Flags">The packed effect flags byte.</param>
public sealed record ActiveEffect(int EffectId, int Amplifier, int Duration, byte Flags)
{
    /// <summary>The <see cref="AppliedAtTick"/> value that means "this effect carries no stamp".</summary>
    public const long UnstampedTick = -1;

    /// <summary>The <see cref="RemainingTicksAt"/> answer for an effect that never expires.</summary>
    public const int InfiniteRemaining = -1;

    /// <summary>The <see cref="RemainingTicksAt"/> answer for an effect that is present but cannot be dated. A consumer gating on "duration is at least N" must refuse on this rather than read it as a number.</summary>
    public const int UnknownRemaining = int.MinValue;

    /// <summary>The session tick (<c>ClientState.SessionTick</c>) this effect was applied or last refreshed at, or <see cref="UnstampedTick"/> when it carries no stamp.</summary>
    /// <remarks>An <c>init</c> property with a default rather than a positional parameter, deliberately: this is a public record and adding a positional parameter would be a source break for every caller that constructs one.</remarks>
    public long AppliedAtTick { get; init; } = UnstampedTick;

    /// <summary>Whether the effect never expires on its own (the wire's own -1 duration).</summary>
    public bool IsInfinite => Duration < 0;

    /// <summary>How many ticks of this effect are left as of <paramref name="nowTick"/>, derived from <see cref="Duration"/> and <see cref="AppliedAtTick"/>.</summary>
    /// <remarks>Errors only by client/server tick drift, which is the same drift every other prediction on this loop already lives with. Floors at zero rather than going negative, because <c>remove_mob_effect</c> is what actually retires the entry and it may land a tick or two after the arithmetic says the effect ended.</remarks>
    /// <param name="nowTick">The current session tick.</param>
    /// <returns>The remaining ticks, <see cref="InfiniteRemaining"/> for an infinite effect, or <see cref="UnknownRemaining"/> when the effect carries no usable stamp.</returns>
    public int RemainingTicksAt(long nowTick)
    {
        if (IsInfinite)
            return InfiniteRemaining;

        // No stamp, or a stamp from after "now": an effect that survived a counter reset cannot be dated, and inventing an elapsed for it would hand a consumer a number it would act on.
        if (AppliedAtTick == UnstampedTick || nowTick < AppliedAtTick)
            return UnknownRemaining;

        long elapsed = nowTick - AppliedAtTick;
        return elapsed >= Duration ? 0 : Duration - (int)elapsed;
    }
}
