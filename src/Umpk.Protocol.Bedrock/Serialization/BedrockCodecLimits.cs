namespace Umpk.Protocol.Bedrock;

internal sealed record BedrockCodecLimits(
    int MaxStringBytes = 1 * 1024 * 1024,
    int MaxByteArrayBytes = 8 * 1024 * 1024,
    int MaxBatchBytes = 16 * 1024 * 1024,
    int MaxBatchMembers = 4096,
    int MaxDecompressedBytes = 16 * 1024 * 1024,
    int MaxCollectionElements = 1_000_000,
    int MaxNbtDepth = 512)
{
    internal static BedrockCodecLimits Default { get; } = new();

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStringBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxByteArrayBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBatchBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBatchMembers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDecompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCollectionElements);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxNbtDepth);
    }
}
