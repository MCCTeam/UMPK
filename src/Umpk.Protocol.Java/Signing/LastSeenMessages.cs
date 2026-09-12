namespace Umpk.Protocol.Java.Signing;

/// <summary>A single acknowledged-message entry: a player UUID paired with the signature of the last message the session saw from that player.</summary>
public sealed record AcknowledgedMessage(Guid ProfileId, byte[] Signature);

/// <summary>The 1.19.1/1.19.2 (v2) last-seen message window feeding the chat-signing acknowledgment. It holds the recent (uuid, signature) entries with the protocol's eviction rule: add to the front, evict the oldest entry, and null an older entry from the same sender without filling the hole.</summary>
/// <remarks>This shape is v2-ONLY. The 1.19.3+ era carries the ordered signatures behind an offset/bitset ack window instead, which is a different data structure with a different eviction rule and lives in <see cref="LastSeenMessagesTracker"/> (whose <see cref="LastSeenMessagesTracker.WindowSize"/> is the v3 window size).</remarks>
public sealed class LastSeenMessagesCollector
{
    /// <summary>The vanilla 1.19.1/1.19.2 window size, capped at 5 entries by the wire itself.</summary>
    public const int Window1_19 = 5;

    private readonly AcknowledgedMessage?[] _entries;

    private int _count;

    /// <summary>Creates a collector with the given window size.</summary>
    public LastSeenMessagesCollector(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        _entries = new AcknowledgedMessage?[size];
    }

    /// <summary>The number of live entries currently held (excluding evicted holes).</summary>
    public int Count => _count;

    /// <summary>Adds an entry to the front, evicting the oldest and nulling any older entry with the same sender (the vanilla rule that keeps at most one entry per sender without compacting).</summary>
    public void Add(AcknowledgedMessage entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        AcknowledgedMessage? carry = entry;
        for (int i = 0; i < _count; i++)
        {
            AcknowledgedMessage? current = _entries[i];
            _entries[i] = carry;
            if (current is null || current.ProfileId == entry.ProfileId)
                return;

            carry = current;
        }

        if (_count < _entries.Length)
            _entries[_count++] = carry;

    }

    /// <summary>Snapshots the current live entries, newest first (evicted holes removed).</summary>
    public IReadOnlyList<AcknowledgedMessage> Snapshot()
    {
        var result = new List<AcknowledgedMessage>(_count);
        for (int i = 0; i < _count; i++)
            if (_entries[i] is { } entry)
                result.Add(entry);

        return result;
    }
}
