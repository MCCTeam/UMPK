using Umpk.Client.Actions;

namespace Umpk.Client.Internal;

/// <summary>Builds the <see cref="ChatSigningScope"/> the chat and command send paths run inside, from a reader for the session's coordinator.</summary>
/// <remarks>
/// <para>This type exists for one property: the scope observes the coordinator EXACTLY ONCE per send. The field it reads is <c>UmpkClient._chatSigning</c>, which <c>TeardownSessionAsync</c> sets to null, so the former inline branch read the field twice: once for the null test and once for the call. A teardown landing between those two reads gave the null test a coordinator and the call a null, and the send died with a <see cref="NullReferenceException"/> instead of taking the unsigned path the null was supposed to select. One read into a local closes it by construction, because a local cannot be written by another thread; there is no lock and no volatile read here because none is needed for that.</para>
/// <para>A null coordinator is not an error: it is the ordinary state of an offline session, a non-signing version, and a session whose connection has ended. The scope hands <c>null</c> to the send body, which is the unsigned path.</para>
/// </remarks>
internal static class ChatSigningScopes
{
    /// <summary>Creates the send scope. <paramref name="read"/> is invoked once per send and its result is used for both the decision and the call.</summary>
    /// <param name="read">Reads the session's current signing coordinator, or null when there is none.</param>
    public static ChatSigningScope Create(Func<ChatSigningCoordinator?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return (send, ct) =>
        {
            ChatSigningCoordinator? coordinator = read();
            return coordinator is null ? send(null, ct) : coordinator.RunOrderedSendAsync(send, ct);
        };
    }
}
