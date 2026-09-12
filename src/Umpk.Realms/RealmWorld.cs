using System.Globalization;

namespace Umpk.Realms;

/// <summary>A player's Realms world as reported by <c>GET /worlds</c>. This is a decoded view of the wire payload; only fields needed for server selection and display are surfaced. Absent numeric fields default to zero and absent strings to empty.</summary>
/// <param name="Id">The Realms world id, used to resolve a join address.</param>
/// <param name="Name">The world display name.</param>
/// <param name="Motd">The message of the day, or empty when unset.</param>
/// <param name="Owner">The owner's gamertag, or empty when the caller is the owner.</param>
/// <param name="OwnerUuid">The owner's UUID when the payload carries it.</param>
/// <param name="State">The world lifecycle state (open, closed, uninitialized).</param>
/// <param name="WorldType">The raw world-type string (for example <c>NORMAL</c>, <c>MINIGAME</c>).</param>
/// <param name="Expired">True when the Realms subscription has expired.</param>
/// <param name="ExpiredTrial">True when a trial subscription has expired.</param>
/// <param name="DaysLeft">Days left on the subscription, when reported.</param>
/// <param name="MaxPlayers">The world player-slot capacity, when reported.</param>
/// <param name="ActiveSlot">The active world slot number, when reported.</param>
/// <param name="Member">True when the caller is a member (not the owner) of the world.</param>
public sealed record RealmWorld(
    long Id,
    string Name,
    string Motd,
    string Owner,
    Guid? OwnerUuid,
    RealmState State,
    string WorldType,
    bool Expired,
    bool ExpiredTrial,
    int DaysLeft,
    int MaxPlayers,
    int? ActiveSlot,
    bool Member)
{
    /// <summary>Matches <paramref name="selector"/> against <paramref name="worlds"/> by id, then by name: the id pass runs over the entire list first, so a world literally named after another world's id (for example a world called <c>"5678"</c>) never shadows the world whose real <see cref="Id"/> is 5678. A selector counts as an id only when it parses under <see cref="NumberStyles.None"/> (bare digits, no sign and no surrounding whitespace); anything else, including <c>" 1234"</c>, <c>"-1"</c>, or <c>"+1234"</c>, is a name candidate only. Name matching is case-insensitive (<see cref="StringComparison.OrdinalIgnoreCase"/>).</summary>
    /// <returns>The matching world, or null when nothing in <paramref name="worlds"/> matches.</returns>
    public static RealmWorld? Match(IReadOnlyList<RealmWorld> worlds, string selector)
    {
        ArgumentNullException.ThrowIfNull(worlds);
        ArgumentException.ThrowIfNullOrEmpty(selector);

        if (long.TryParse(selector, NumberStyles.None, CultureInfo.InvariantCulture, out long id))
            foreach (RealmWorld world in worlds)
                if (world.Id == id)
                    return world;

        foreach (RealmWorld world in worlds)
            if (string.Equals(world.Name, selector, StringComparison.OrdinalIgnoreCase))
                return world;

        return null;
    }
}
