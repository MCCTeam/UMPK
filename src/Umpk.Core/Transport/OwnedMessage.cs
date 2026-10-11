using System.Buffers;

namespace Umpk.Transport;

/// <summary>A single logical message plus the rented buffer that carries it, with exactly one owner at any moment.</summary>
/// <remarks>Ownership starts with the creator, moves to the connection only when a send reports <see cref="MessageAdmissionStatus.Accepted"/>, and moves to the caller on receive; whoever holds the message disposes it exactly once to return the buffer.</remarks>
public sealed class OwnedMessage : IDisposable
{
    private IMemoryOwner<byte>? _owner;

    /// <summary>Creates a message that owns the first <paramref name="length"/> bytes of <paramref name="owner"/>.</summary>
    /// <param name="owner">The rented buffer backing this message; ownership moves to the message and disposal returns it.</param>
    /// <param name="length">The logical message length in bytes, which must be non-negative and fit inside <paramref name="owner"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or exceeds the rented buffer.</exception>
    public OwnedMessage(IMemoryOwner<byte> owner, int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (length > owner.Memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        _owner = owner;
        Length = length;
    }

    /// <summary>Gets the logical message length in bytes, which may be shorter than the rented buffer.</summary>
    public int Length { get; }

    /// <summary>Gets the logical message bytes, sliced to <see cref="Length"/> from the rented buffer.</summary>
    /// <value>The first <see cref="Length"/> bytes of the rented buffer.</value>
    /// <exception cref="ObjectDisposedException">The message was already disposed.</exception>
    public Memory<byte> Memory
    {
        get
        {
            IMemoryOwner<byte> owner = Volatile.Read(ref _owner) ?? throw new ObjectDisposedException(nameof(OwnedMessage));
            return owner.Memory[..Length];
        }
    }

    /// <summary>Returns the rented buffer to its pool; safe to call more than once but releases the buffer only on the first call.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _owner, null)?.Dispose();
    }
}
