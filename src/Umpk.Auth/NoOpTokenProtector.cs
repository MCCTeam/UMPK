using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Umpk.Auth;

/// <summary>A pass-through <see cref="ITokenProtector"/> that does not encrypt at rest. It logs a single warning the first time it is used so the plaintext fallback is never silent. The file store still applies owner-only file permissions where the OS supports them.</summary>
public sealed class NoOpTokenProtector : ITokenProtector
{
    private readonly ILogger _logger;
    private int _warned;

    public NoOpTokenProtector(ILogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <inheritdoc />
    public ValueTask<byte[]> ProtectAsync(byte[] plaintext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            _logger.LogWarning(
                "Token store is writing credentials without at-rest encryption; relying on file permissions only.");

        return ValueTask.FromResult(plaintext);
    }

    /// <inheritdoc />
    public ValueTask<byte[]> UnprotectAsync(byte[] protectedData, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(protectedData);
    }
}
