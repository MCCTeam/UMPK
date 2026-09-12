namespace Umpk.Client.Plugins;

/// <summary>
/// Raised when the server's announced plugin-channel set changes, that is when a <c>minecraft:register</c> or <c>minecraft:unregister</c> (or the pre-1.13 <c>REGISTER</c> / <c>UNREGISTER</c>) actually adds or removes something. An announcement that repeats what the set already said raises nothing.
/// <para>THREADING. This runs on whichever loop delivered the announcement: the connection's read loop for a play-phase announce, the session loop for a configuration-phase one. It must be cheap and must not block. A handler that throws is logged and swallowed.</para>
/// </summary>
public sealed class ServerAnnouncedChannelsChangedEventArgs(
    IReadOnlySet<Identifier> announced,
    IReadOnlyList<Identifier> added,
    IReadOnlyList<Identifier> removed) : EventArgs
{
    /// <summary>The full announced set after this change; the same snapshot <c>ServerAnnounced</c> returns.</summary>
    public IReadOnlySet<Identifier> Announced { get; } = announced;

    /// <summary>The channels this announcement added, in the order the payload listed them.</summary>
    public IReadOnlyList<Identifier> Added { get; } = added;

    /// <summary>The channels this announcement removed, in the order the payload listed them.</summary>
    public IReadOnlyList<Identifier> Removed { get; } = removed;
}
