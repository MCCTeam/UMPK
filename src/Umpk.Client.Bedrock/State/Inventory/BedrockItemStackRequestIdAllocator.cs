namespace Umpk.Client.Bedrock.State.Inventory;

/// <summary>Allocates distinct odd-negative request identities for one connection lifetime without resetting across inventory generations.</summary>
internal sealed class BedrockItemStackRequestIdAllocator
{
    private readonly Lock _gate = new();
    private int _nextRequestId;
    private bool _exhausted;

    /// <summary>Starts the native client sequence at -3.</summary>
    internal BedrockItemStackRequestIdAllocator() : this(-3)
    {
    }

    /// <summary>Starts at an unallocated member of the descending sequence, allowing bounded exhaustion checks.</summary>
    internal BedrockItemStackRequestIdAllocator(int firstRequestId)
    {
        if (firstRequestId > -3 || (firstRequestId & 1) == 0)
            throw new ArgumentOutOfRangeException(nameof(firstRequestId), firstRequestId, "The initial request identity must be odd and at most -3.");

        _nextRequestId = firstRequestId;
    }

    /// <summary>Allocates the next identity, or returns false with zero after the final odd-negative identity has been emitted.</summary>
    internal bool TryAllocate(out int requestId)
    {
        lock (_gate)
        {
            if (_exhausted)
            {
                requestId = 0;
                return false;
            }

            requestId = _nextRequestId;
            if (requestId == int.MinValue + 1)
                _exhausted = true;
            else
                _nextRequestId = checked(requestId - 2);

            return true;
        }
    }
}
