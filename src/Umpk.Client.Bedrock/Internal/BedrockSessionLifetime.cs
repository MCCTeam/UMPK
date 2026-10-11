using Umpk.Client.Bedrock.Session;

namespace Umpk.Client.Bedrock.Internal;

internal sealed class BedrockSessionLifetime : IDisposable
{
    private readonly CancellationTokenSource _owned = new();
    private readonly TaskCompletionSource<BedrockTermination> _termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _terminal;
    private bool _disposed;

    internal CancellationToken Token => _owned.Token;

    internal Task<BedrockTermination> Termination => _termination.Task;

    internal Task Ready => _ready.Task;

    internal bool TryComplete(BedrockTermination termination)
    {
        ArgumentNullException.ThrowIfNull(termination);
        if (Interlocked.CompareExchange(ref _terminal, 1, 0) != 0)
            return false;

        _termination.TrySetResult(termination);
        _ready.TrySetCanceled();
        try
        {
            _owned.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    internal void MarkReady()
    {
        if (Volatile.Read(ref _terminal) != 0)
            return;
        _ready.TrySetResult(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _owned.Dispose();
    }
}
