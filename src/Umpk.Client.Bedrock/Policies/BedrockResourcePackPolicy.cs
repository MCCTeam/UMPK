using System.Security.Cryptography;

namespace Umpk.Client.Bedrock.Policies;

internal sealed record BedrockResourcePackPolicyOptions(
    bool AcceptPacks,
    bool DownloadPacks,
    bool CachePacks,
    bool VerifyHashes,
    int MaximumPackCount,
    long MaximumPackBytes,
    long MaximumAggregateBytes,
    int MaximumChunkCount,
    TimeSpan InactivityTimeout,
    TimeSpan TotalTimeout)
{
    internal static BedrockResourcePackPolicyOptions DeclineDownloads { get; } = new(
        false,
        false,
        false,
        false,
        0,
        0,
        0,
        0,
        TimeSpan.Zero,
        TimeSpan.Zero);

    internal void Validate()
    {
        if (DownloadPacks && !AcceptPacks)
            throw new ArgumentException("Pack downloads require pack acceptance.");
        if (CachePacks && !DownloadPacks)
            throw new ArgumentException("Pack caching requires pack downloads.");
        if (VerifyHashes && !DownloadPacks)
            throw new ArgumentException("Pack hash verification requires pack downloads.");
        if (DownloadPacks && (MaximumPackCount <= 0 || MaximumPackBytes <= 0 || MaximumAggregateBytes <= 0 || MaximumChunkCount <= 0 || InactivityTimeout <= TimeSpan.Zero || TotalTimeout <= TimeSpan.Zero))
            throw new ArgumentException("Pack download limits must be positive when downloads are enabled.");
        if (MaximumPackBytes > int.MaxValue)
            throw new ArgumentException("The per-pack byte limit cannot exceed the managed buffer limit.");
        if (MaximumAggregateBytes < MaximumPackBytes)
            throw new ArgumentException("The aggregate pack limit cannot be smaller than the per-pack limit.");
        if (TotalTimeout < InactivityTimeout)
            throw new ArgumentException("The total pack timeout cannot be shorter than the inactivity timeout.");
    }
}

internal sealed record BedrockResourcePackDescriptor
{
    internal BedrockResourcePackDescriptor(string packId, long size, byte[] expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        if (packId.Length > 128 || packId.Contains('/') || packId.Contains('\\') || packId.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("The pack identifier contains an unsafe path component.", nameof(packId));
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentNullException.ThrowIfNull(expectedSha256);
        if (expectedSha256.Length is not (0 or 32))
            throw new ArgumentException("A resource-pack SHA-256 value must be empty or contain 32 bytes.", nameof(expectedSha256));
        PackId = packId;
        Size = size;
        ExpectedSha256 = expectedSha256.ToArray();
    }

    internal string PackId { get; }

    internal long Size { get; }

    internal ReadOnlyMemory<byte> ExpectedSha256 { get; }
}

internal sealed record BedrockResourcePackOffer(bool Required, IReadOnlyList<BedrockResourcePackDescriptor> Packs);

internal enum BedrockResourcePackDecisionKind
{
    Complete,
    RefuseOptionalContent,
    UnsupportedRequiredContent,
    Download,
    LimitsExceeded,
}

internal readonly record struct BedrockResourcePackDecision(
    BedrockResourcePackDecisionKind Kind,
    string? Reason = null,
    bool CacheAfterVerification = false,
    bool VerifyHash = false);

internal interface IBedrockResourcePackPolicy
{
    ValueTask<BedrockResourcePackDecision> DecideAsync(BedrockResourcePackOffer offer, CancellationToken cancellationToken);
}

internal sealed class BedrockResourcePackPolicy : IBedrockResourcePackPolicy
{
    private readonly BedrockResourcePackPolicyOptions _options;

    internal BedrockResourcePackPolicy(BedrockResourcePackPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public ValueTask<BedrockResourcePackDecision> DecideAsync(BedrockResourcePackOffer offer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(offer.Packs);
        if (offer.Packs.Count == 0)
            return ValueTask.FromResult(new BedrockResourcePackDecision(BedrockResourcePackDecisionKind.Complete));
        if (!_options.AcceptPacks || !_options.DownloadPacks)
        {
            BedrockResourcePackDecisionKind kind = offer.Required
                ? BedrockResourcePackDecisionKind.UnsupportedRequiredContent
                : BedrockResourcePackDecisionKind.RefuseOptionalContent;
            return ValueTask.FromResult(new BedrockResourcePackDecision(kind, "Resource-pack downloads are disabled."));
        }

        if (offer.Packs.Count > _options.MaximumPackCount || offer.Packs.Any(pack => pack.Size > _options.MaximumPackBytes))
            return ValueTask.FromResult(new BedrockResourcePackDecision(BedrockResourcePackDecisionKind.LimitsExceeded, "The resource-pack offer exceeds configured limits."));
        long aggregate = 0;
        foreach (BedrockResourcePackDescriptor pack in offer.Packs)
        {
            aggregate = checked(aggregate + pack.Size);
            if (_options.VerifyHashes && pack.ExpectedSha256.Length != 32)
                return ValueTask.FromResult(new BedrockResourcePackDecision(BedrockResourcePackDecisionKind.LimitsExceeded, "A required resource-pack hash is missing."));
        }
        if (aggregate > _options.MaximumAggregateBytes)
            return ValueTask.FromResult(new BedrockResourcePackDecision(BedrockResourcePackDecisionKind.LimitsExceeded, "The aggregate resource-pack offer exceeds its configured limit."));

        return ValueTask.FromResult(new BedrockResourcePackDecision(
            BedrockResourcePackDecisionKind.Download,
            CacheAfterVerification: _options.CachePacks,
            VerifyHash: _options.VerifyHashes));
    }
}

internal enum BedrockResourcePackFailureCode
{
    InvalidChunk,
    DuplicateChunk,
    LimitsExceeded,
    SizeMismatch,
    HashMismatch,
    Incomplete,
    Timeout,
}

internal sealed class BedrockResourcePackException(BedrockResourcePackFailureCode code, string message) : Exception(message)
{
    internal BedrockResourcePackFailureCode Code { get; } = code;
}

internal sealed class BedrockResourcePackDownloadSession : IDisposable
{
    private readonly BedrockResourcePackDescriptor _descriptor;
    private readonly BedrockResourcePackPolicyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SortedDictionary<int, byte[]> _chunks = [];
    private readonly long _startedAt;
    private long _lastActivityAt;
    private long _receivedBytes;
    private bool _completed;
    private bool _disposed;

    internal BedrockResourcePackDownloadSession(BedrockResourcePackDescriptor descriptor, BedrockResourcePackPolicyOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!options.DownloadPacks)
            throw new ArgumentException("The resource-pack policy does not allow downloads.", nameof(options));
        if (descriptor.Size > options.MaximumPackBytes)
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.LimitsExceeded, "The pack exceeds the per-pack byte limit.");
        _descriptor = descriptor;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _startedAt = _timeProvider.GetTimestamp();
        _lastActivityAt = _startedAt;
    }

    internal void AddChunk(int index, ReadOnlySpan<byte> content)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckTimeouts();
        if (_completed)
            throw new InvalidOperationException("The resource-pack download is complete.");
        if (index < 0 || index >= _options.MaximumChunkCount || content.IsEmpty)
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.InvalidChunk, "The resource-pack chunk is invalid.");
        if (_chunks.ContainsKey(index))
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.DuplicateChunk, "The resource-pack chunk was already received.");
        long newLength = checked(_receivedBytes + content.Length);
        if (newLength > _descriptor.Size || newLength > _options.MaximumPackBytes)
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.LimitsExceeded, "The resource-pack content exceeds its declared or configured length.");
        _chunks.Add(index, content.ToArray());
        _receivedBytes = newLength;
        _lastActivityAt = _timeProvider.GetTimestamp();
    }

    internal byte[] Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckTimeouts();
        if (_completed)
            throw new InvalidOperationException("The resource-pack download is complete.");
        if (_receivedBytes != _descriptor.Size)
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.SizeMismatch, "The resource-pack content does not match its declared length.");
        if (_chunks.Count == 0 || _chunks.Keys.Where((value, index) => value != index).Any())
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.Incomplete, "The resource-pack chunk sequence is incomplete.");
        byte[] content = new byte[checked((int)_receivedBytes)];
        int offset = 0;
        foreach (byte[] chunk in _chunks.Values)
        {
            chunk.CopyTo(content, offset);
            offset += chunk.Length;
        }
        if (_options.VerifyHashes && !CryptographicOperations.FixedTimeEquals(SHA256.HashData(content), _descriptor.ExpectedSha256.Span))
        {
            CryptographicOperations.ZeroMemory(content);
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.HashMismatch, "The resource-pack SHA-256 value does not match its offer.");
        }
        _completed = true;
        return content;
    }

    private void CheckTimeouts()
    {
        long now = _timeProvider.GetTimestamp();
        if (_timeProvider.GetElapsedTime(_startedAt, now) > _options.TotalTimeout || _timeProvider.GetElapsedTime(_lastActivityAt, now) > _options.InactivityTimeout)
            throw new BedrockResourcePackException(BedrockResourcePackFailureCode.Timeout, "The resource-pack download timed out.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (byte[] chunk in _chunks.Values)
            CryptographicOperations.ZeroMemory(chunk);
        _chunks.Clear();
    }
}
