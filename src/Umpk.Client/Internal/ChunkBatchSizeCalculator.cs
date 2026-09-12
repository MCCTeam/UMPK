namespace Umpk.Client.Internal;

/// <summary>Tracks how long the client takes to absorb a chunk batch and derives the "desired chunks per tick" figure the server expects back in <c>chunk_batch_received</c> (1.20.2+).</summary>
/// <remarks>
/// <para>This is not an optimisation. The server initially allows one unacknowledged batch and refuses to send further chunks until the client acknowledges. Chunks that never get sent remain pending, and entities are broadcast only for chunks no longer pending. A client that never acknowledges therefore ends up with roughly nine chunks of terrain and no entities outside them, on every version from 1.20.2 onward.</para>
/// <para>The calculation matches the game client: an exponentially weighted mean of nanoseconds-per-chunk, each sample clamped to within a factor of three of the running mean so one stalled batch cannot swing the estimate, with the old-sample weight saturating at 49. Vanilla's magic 7,000,000 is the nanosecond budget it is willing to spend per tick on chunk intake, so the quotient is a chunks-per-tick rate.</para>
/// </remarks>
internal sealed class ChunkBatchSizeCalculator
{
    private const int MaxOldSamplesWeight = 49;
    private const int ClampCoefficient = 3;
    private const double NanosPerTickBudget = 7_000_000.0;
    private const double InitialNanosPerChunk = 2_000_000.0;

    private readonly TimeProvider _time;
    private double _aggregatedNanosPerChunk = InitialNanosPerChunk;
    private int _oldSamplesWeight = 1;
    private long _batchStartTimestamp;

    public ChunkBatchSizeCalculator(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _batchStartTimestamp = time.GetTimestamp();
    }

    /// <summary>Marks the start of a batch, on <c>chunk_batch_start</c>.</summary>
    public void OnBatchStart() => _batchStartTimestamp = _time.GetTimestamp();

    /// <summary>Folds one finished batch into the estimate, on <c>chunk_batch_finished</c>. An empty batch carries no timing information and is ignored, matching vanilla.</summary>
    public void OnBatchFinished(int batchSize)
    {
        if (batchSize <= 0)
            return;

        double elapsedNanos = _time.GetElapsedTime(_batchStartTimestamp, _time.GetTimestamp()).TotalNanoseconds;
        double nanosPerChunk = elapsedNanos / batchSize;
        double clamped = Math.Clamp(
            nanosPerChunk,
            _aggregatedNanosPerChunk / ClampCoefficient,
            _aggregatedNanosPerChunk * ClampCoefficient);

        _aggregatedNanosPerChunk =
            ((_aggregatedNanosPerChunk * _oldSamplesWeight) + clamped) / (_oldSamplesWeight + 1);
        _oldSamplesWeight = Math.Min(MaxOldSamplesWeight, _oldSamplesWeight + 1);
    }

    /// <summary>The rate to report back to the server.</summary>
    public float DesiredChunksPerTick => (float)(NanosPerTickBudget / _aggregatedNanosPerChunk);
}
