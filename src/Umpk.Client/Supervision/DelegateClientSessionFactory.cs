namespace Umpk.Client;

/// <summary>An <see cref="IClientSessionFactory"/> built from a single delegate, for a host whose client construction is a plain function of the attempt (no custom release behavior). Use the interface directly to override <see cref="IClientSessionFactory.ReleaseAsync"/>.</summary>
public sealed class DelegateClientSessionFactory : IClientSessionFactory
{
    private readonly Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> _create;

    /// <summary>Creates the factory from a build delegate.</summary>
    public DelegateClientSessionFactory(Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
    }

    /// <inheritdoc />
    public ValueTask<UmpkClient> CreateAsync(SessionAttempt attempt, CancellationToken ct) => _create(attempt, ct);
}
