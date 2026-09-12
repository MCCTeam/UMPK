namespace Umpk.Client.Movement;

/// <summary>Grants at most one <see cref="IMovementLease"/> at a time. Lease acquisition is atomic; releasing a lease clears ownership so the next acquirer succeeds.</summary>
internal sealed class MovementLeaseManager
{
    private readonly Lock _gate = new();
    private Lease? _current;

    /// <summary>The tag of the current lease owner, if any.</summary>
    public string? CurrentOwner
    {
        get
        {
            lock (_gate)
                return _current?.OwnerTag;

        }
    }

    /// <summary>Tries to acquire the movement lease; returns null when it is already held.</summary>
    public IMovementLease? TryAcquire(string ownerTag)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerTag);
        lock (_gate)
        {
            if (_current is not null)
                return null;

            var lease = new Lease(this, ownerTag);
            _current = lease;
            return lease;
        }
    }

    private void Release(Lease lease)
    {
        lock (_gate)
            if (ReferenceEquals(_current, lease))
                _current = null;

    }

    private sealed class Lease(MovementLeaseManager owner, string ownerTag) : IMovementLease
    {
        private MovementLeaseManager? _owner = owner;

        public string OwnerTag { get; } = ownerTag;

        public bool IsHeld => _owner is not null;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(this);
    }
}
