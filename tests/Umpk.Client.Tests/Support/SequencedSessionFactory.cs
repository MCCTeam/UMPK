namespace Umpk.Client.Tests.Support;

/// <summary>An <see cref="IClientSessionFactory"/> that pulls one build step per <see cref="CreateAsync"/> call from a fixed script (<see cref="Then"/>), and records every <see cref="SessionAttempt"/> it was handed so a test can assert on <see cref="SessionAttempt.Attempt"/>, <see cref="SessionAttempt.Endpoint"/> and <see cref="SessionAttempt.PreviousDisconnect"/>. A call past the end of the script repeats the last step, so a bounded retry loop does not have to be scripted to the exact attempt count.</summary>
internal sealed class SequencedSessionFactory : IClientSessionFactory
{
    private readonly List<Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>>> _steps = [];
    private int _index;

    public List<SessionAttempt> Attempts { get; } = [];

    public int CreateCalls { get; private set; }

    public int ReleaseCalls { get; private set; }

    public SequencedSessionFactory Then(Func<SessionAttempt, CancellationToken, ValueTask<UmpkClient>> step)
    {
        _steps.Add(step);
        return this;
    }

    public ValueTask<UmpkClient> CreateAsync(SessionAttempt attempt, CancellationToken ct)
    {
        Attempts.Add(attempt);
        CreateCalls++;
        int index = Math.Min(_index, _steps.Count - 1);
        _index++;
        return _steps[index](attempt, ct);
    }

    public ValueTask ReleaseAsync(UmpkClient client, DisconnectInfo? disconnect, CancellationToken ct)
    {
        ReleaseCalls++;
        return client.DisposeAsync();
    }
}
