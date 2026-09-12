using Umpk.Commands;
using Umpk.Text;

namespace Umpk.Client.Commands;

/// <summary>The command source for host commands registered against a client session. Carries the owning client so command bodies can act on the session, and a reply sink the host supplies (default: the client logger). The server command tree does NOT plug into the <see cref="CommandService{TSource}"/> this source is used with: <see cref="Actions.ChatActions.CompleteAsync"/> merges this service's completions with the separately reconstructed server command tree (<c>State.ServerCommands.Tree</c>); the server tree never registers here and never executes through this service.</summary>
public sealed class ClientCommandSource : ICommandSource
{
    private readonly UmpkClient _client;
    private readonly Func<Component, CancellationToken, ValueTask> _reply;
    private RegistrySuggestionSource? _registrySuggestions;

    internal ClientCommandSource(UmpkClient client, Func<Component, CancellationToken, ValueTask> reply)
    {
        _client = client;
        _reply = reply;
    }

    /// <inheritdoc />
    public ValueTask ReplyAsync(Component message, CancellationToken cancellationToken = default)
        => _reply(message, cancellationToken);

    /// <inheritdoc />
    public T? GetService<T>()
        where T : class
    {
        // Registry-backed suggestions read the client's registries live (see RegistrySuggestionSource), so the source itself can be built once and cached here rather than rebuilt on every GetService<IRegistrySuggestionSource> call.
        if (typeof(T) == typeof(IRegistrySuggestionSource))
        {
            _registrySuggestions ??= new RegistrySuggestionSource(() => _client.State.Registries);
            return _registrySuggestions as T;
        }

        return _client as T;
    }
}
