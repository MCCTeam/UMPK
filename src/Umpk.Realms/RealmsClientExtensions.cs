namespace Umpk.Realms;

/// <summary>Convenience compositions over <see cref="IRealmsClient"/>. A host that wants more control than <see cref="ResolveWorldAsync"/> gives (for example, showing the full world list before picking one) composes <see cref="IRealmsClient.ListWorldsAsync"/> and <see cref="RealmWorld.Match"/> itself; this extension exists for the common case of "resolve this one selector to an address".</summary>
public static class RealmsClientExtensions
{
    /// <summary>Lists the worlds available to the account behind <paramref name="client"/>, matches <paramref name="worldSelector"/> against them with <see cref="RealmWorld.Match"/>, and joins the match. No failure here needs its own try/catch: a list or join failure already arrives as a classified <see cref="RealmsException"/> (from the P1 error classification), and a selector with no match is classified the same way, as <see cref="RealmsErrorKind.WorldNotFound"/>.</summary>
    /// <exception cref="RealmsException">No world matched <paramref name="worldSelector"/> (<see cref="RealmsErrorKind.WorldNotFound"/>), or the list or join call failed (whatever kind that call classified).</exception>
    public static async Task<RealmServerAddress> ResolveWorldAsync(this IRealmsClient client, string worldSelector, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);

        IReadOnlyList<RealmWorld> worlds = await client.ListWorldsAsync(ct).ConfigureAwait(false);
        RealmWorld? match = RealmWorld.Match(worlds, worldSelector);
        if (match is null)
            throw new RealmsException(RealmsErrorKind.WorldNotFound, "No Realms world matched '" + worldSelector + "'.");

        return await client.JoinWorldAsync(match.Id, ct).ConfigureAwait(false);
    }
}
