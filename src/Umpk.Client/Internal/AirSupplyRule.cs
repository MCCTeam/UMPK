namespace Umpk.Client.Internal;

/// <summary>Minecraft's air-supply (breath) arithmetic for the local player, as one pure function of the previous value and this tick's conditions. Kept apart from the physics holder so the rule is directly assertable without simulating a water column.</summary>
/// <remarks>
/// <para>A full lung is 300 ticks, which is also the initial synchronized value.</para>
/// <para>While the eye is in water and the player is not immune, air falls by one per tick. Respiration can make the server skip decrements at random, so it is not predicted here. Reaching -20 resets air to zero. Out of water, air rises by four per tick up to the maximum.</para>
/// <para>The damage that accompanies the reset is deliberately absent because the server applies it. A client that subtracted health here would be inventing a number the next <c>set_health</c> frame contradicts.</para>
/// <para>Three deliberate departures are bounded by the server sending the local player a <c>set_entity_data</c> update whenever the value changes, so a prediction can lead the truth by at most one tick before it is overwritten:</para>
/// <list type="number">
/// <item><description>
/// ERA. Clients predicted this value through 1.21.4. From 1.21.5, clients only read the synchronized value. UMPK predicts on every protocol rather than gating on that boundary: the arithmetic is identical to the server's, so the prediction converges rather than drifts, and expressing the boundary properly is a dataset feature axis rather than a protocol literal in engine code.
/// </description></item>
/// <item><description>
/// BUBBLE COLUMNS. An eye inside <c>minecraft:bubble_column</c> is exempt from air drain, independent of the <c>drag</c> property. The caller must preserve that exemption while still classifying the column as water for buoyancy.
/// </description></item>
/// <item><description>
/// IMMUNE AND SUBMERGED. The value is held rather than refilled. Minecraft held it through 1.21.3; 1.21.4 added a refill branch, and 1.21.11 made refilling effect-dependent. Holding is the behaviour common to the whole 47-768 band (1.8 through 1.21.3), and it errs low, which is the safe direction for anything that decides when to surface.
/// </description></item>
/// </list>
/// </remarks>
internal static class AirSupplyRule
{
    /// <summary>A full lung, in ticks.</summary>
    public const int TotalAirSupply = 300;

    /// <summary>The value at which vanilla drowns the entity and resets the counter to zero.</summary>
    public const int DrowningPoint = -20;

    /// <summary>Ticks of air recovered per tick out of the water (<c>increaseAirSupply</c>).</summary>
    public const int RefillPerTick = 4;

    /// <summary>The air supply after one tick.</summary>
    /// <param name="current">The air supply at the end of the previous tick, in ticks.</param>
    /// <param name="eyeInWater">Whether the player's eye is in water this tick; wet feet are not enough.</param>
    /// <param name="drowningImmune">Whether water breathing, conduit power, or invulnerable abilities prevent drowning.</param>
    public static int Next(int current, bool eyeInWater, bool drowningImmune)
    {
        if (eyeInWater)
        {
            if (drowningImmune)
                return current;

            int next = current - 1;
            return next <= DrowningPoint ? 0 : next;
        }

        // Minecraft guards the refill on being below the maximum rather than clamping to it, so a value above the maximum (which only a server can produce) is left alone instead of being pulled down.
        return current < TotalAirSupply ? Math.Min(current + RefillPerTick, TotalAirSupply) : current;
    }
}
