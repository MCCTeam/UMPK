using System.Buffers;
using System.Net;
using System.Security.Cryptography;

namespace Umpk.Client;

/// <summary>Code-configured policy objects for a client session. Kept separate from the bindable <see cref="ClientOptions"/> because these hold delegates and cannot round-trip through configuration binders.</summary>
public sealed class ClientPolicies
{
    /// <summary>The policy that decides how to respond to server resource-pack pushes.</summary>
    public ResourcePackPolicy ResourcePack { get; set; } = ResourcePackPolicy.Decline;
}

/// <summary>Controls reconnect behavior after a session ends.</summary>
public sealed class ReconnectPolicy
{
    /// <summary>Maximum reconnect attempts. A negative value means unlimited.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>Delay before the first reconnect attempt.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Multiplier applied to the delay after each failed attempt (exponential backoff).</summary>
    public double BackoffFactor { get; init; } = 2.0;

    /// <summary>Upper bound on the reconnect delay.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Optional predicate deciding whether a given disconnect reason is retryable. When null every non-cancellation disconnect is treated as retryable.</summary>
    public Func<DisconnectInfo, bool>? ShouldRetry { get; init; }

    /// <summary>The delay for a given zero-based attempt index, honoring backoff and the cap.</summary>
    public TimeSpan DelayFor(int attempt)
    {
        double ms = InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, Math.Max(0, attempt));
        double capped = Math.Min(ms, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }

    /// <summary>True when the disconnect should trigger a reconnect attempt under the given policy. The case that matters is <see cref="DisconnectKind.Kick"/>: a transport fault ("the connection broke") and a kick ("the server decided to remove us") deserve different answers, because retrying a broken socket is what auto-reconnect is for, while retrying a kick is how a client ends up hammering a server that just told it to go away. <see cref="ShouldRetry"/> is where that distinction is honoured; a null policy means reconnect is disabled outright, and a local stop or a cancellation is never retried because local code asked for it.</summary>
    public static bool IsRetryable(ReconnectPolicy? policy, DisconnectInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (policy is null)
            return false;

        return DisconnectInfo.Classify(info.Reason, info.WasLocal) switch
        {
            DisconnectKind.LocalStop or DisconnectKind.Cancelled => false,
            _ => policy.ShouldRetry?.Invoke(info) ?? true,
        };
    }
}

/// <summary>How a server resource-pack push is answered.</summary>
public sealed class ResourcePackPolicy
{
    private static readonly HttpClient Downloader = new();
    private readonly Func<ResourcePackRequest, Func<ResourcePackResponse, ValueTask>, CancellationToken, ValueTask> _process;

    /// <summary>Builds a policy from a decision callback.</summary>
    public ResourcePackPolicy(Func<ResourcePackRequest, ResourcePackResponse> decide)
    {
        ArgumentNullException.ThrowIfNull(decide);
        Decide = decide;
        _process = (request, report, _) => report(decide(request));
    }

    private ResourcePackPolicy(
        Func<ResourcePackRequest, Func<ResourcePackResponse, ValueTask>, CancellationToken, ValueTask> process)
    {
        _process = process;
        Decide = static _ => ResourcePackResponse.Declined;
    }

    /// <summary>The decision callback invoked on the session loop for each push.</summary>
    public Func<ResourcePackRequest, ResourcePackResponse> Decide { get; }

    /// <summary>A legacy shortcut that reports success without downloading or applying a pack. It is never the default; callers selecting it explicitly own that claim.</summary>
    public static ResourcePackPolicy Accept { get; } = new(static _ => ResourcePackResponse.SuccessfullyLoaded);

    /// <summary>A policy that declines every pack.</summary>
    public static ResourcePackPolicy Decline { get; } = new(static _ => ResourcePackResponse.Declined);

    /// <summary>Builds an opt-in policy that can accept, download, SHA-1 verify and cache resource packs. The headless client does not apply visual assets, so the built-in processor reports Accepted and Downloaded, never SuccessfullyLoaded.</summary>
    public static ResourcePackPolicy Configure(ResourcePackDownloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxDownloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDownloadBytes must be positive.");
        if (options.Cache && !options.Download)
            throw new ArgumentException("Caching requires downloading to be enabled.", nameof(options));
        if (options.Cache && string.IsNullOrWhiteSpace(options.CacheDirectory))
            throw new ArgumentException("A cache directory is required when caching is enabled.", nameof(options));

        return new ResourcePackPolicy(
            (request, report, ct) => ProcessDownloadAsync(request, options, report, ct));
    }

    internal ValueTask ProcessAsync(
        ResourcePackRequest request, Func<ResourcePackResponse, ValueTask> report, CancellationToken ct) =>
        _process(request, report, ct);

    private static async ValueTask ProcessDownloadAsync(
        ResourcePackRequest request,
        ResourcePackDownloadOptions options,
        Func<ResourcePackResponse, ValueTask> report,
        CancellationToken ct)
    {
        if (!options.Accept)
        {
            await report(ResourcePackResponse.Declined).ConfigureAwait(false);
            return;
        }
        if (!options.Download)
        {
            await report(ResourcePackResponse.Accepted).ConfigureAwait(false);
            return;
        }
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
        {
            await report(ResourcePackResponse.InvalidUrl).ConfigureAwait(false);
            return;
        }

        await report(ResourcePackResponse.Accepted).ConfigureAwait(false);

        string directory = options.Cache
            ? Path.GetFullPath(options.CacheDirectory!)
            : Path.GetTempPath();
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".umpk-pack-{Guid.NewGuid():N}.tmp");

        try
        {
            using HttpResponseMessage response = await Downloader
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                await report(ResourcePackResponse.FailedDownload).ConfigureAwait(false);
                return;
            }
            if (response.Content.Headers.ContentLength is long length
                && length > options.MaxDownloadBytes)
            {
                await report(ResourcePackResponse.FailedDownload).ConfigureAwait(false);
                return;
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var destination = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
            long total = 0;
            try
            {
                while (true)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > options.MaxDownloadBytes)
                    {
                        await report(ResourcePackResponse.FailedDownload).ConfigureAwait(false);
                        return;
                    }
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await destination.FlushAsync(ct).ConfigureAwait(false);
            string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(request.Hash)
                && !string.Equals(request.Hash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                await report(ResourcePackResponse.FailedDownload).ConfigureAwait(false);
                return;
            }

            if (options.Cache)
            {
                string identity = request.Id == Guid.Empty ? "legacy" : request.Id.ToString("N");
                string cached = Path.Combine(directory, $"{identity}-{actualHash}.zip");
                File.Move(temporary, cached, overwrite: true);
            }

            await report(ResourcePackResponse.Downloaded).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            await report(ResourcePackResponse.FailedDownload).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of a bounded temporary download.
            }
        }
    }
}

/// <summary>Opt-in resource-pack acceptance, download and cache controls.</summary>
public sealed class ResourcePackDownloadOptions
{
    /// <summary>Whether requests are accepted at all. False answers Declined.</summary>
    public bool Accept { get; init; }

    /// <summary>Whether accepted packs are downloaded. False reports Accepted without downloading.</summary>
    public bool Download { get; init; }

    /// <summary>Whether a verified download is retained on disk.</summary>
    public bool Cache { get; init; }

    /// <summary>Directory for cached packs. Required when <see cref="Cache"/> is true.</summary>
    public string? CacheDirectory { get; init; }

    /// <summary>Maximum response body size. Defaults to 256 MiB.</summary>
    public long MaxDownloadBytes { get; init; } = 256L * 1024 * 1024;
}

/// <summary>The information a resource-pack policy sees for one push.</summary>
public readonly record struct ResourcePackRequest(Guid Id, string Url, string Hash, bool Required);

/// <summary>The serverbound action codes for a resource-pack response (the wire ordinal is sent verbatim).</summary>
public enum ResourcePackResponse
{
    /// <summary>Pack applied successfully.</summary>
    SuccessfullyLoaded = 0,

    /// <summary>User declined the pack.</summary>
    Declined = 1,

    /// <summary>Download failed.</summary>
    FailedDownload = 2,

    /// <summary>Pack accepted, download starting.</summary>
    Accepted = 3,

    /// <summary>Download completed and any requested cache publication succeeded.</summary>
    Downloaded = 4,

    /// <summary>Invalid URL.</summary>
    InvalidUrl = 5,

    /// <summary>Failed to reload after applying.</summary>
    FailedReload = 6,

    /// <summary>Pack discarded.</summary>
    Discarded = 7,
}
