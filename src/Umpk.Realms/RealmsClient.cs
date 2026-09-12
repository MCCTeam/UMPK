using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Umpk.Realms.Internal;

namespace Umpk.Realms;

/// <summary>Default <see cref="IRealmsClient"/> implementation over the injected HTTP seam. Requests carry the Realms session cookie built from the supplied credential; responses are decoded with the source- generated <see cref="RealmsJsonContext"/> (no reflection). Non-success statuses raise <see cref="RealmsServiceException"/>. The client is safe for sequential reuse across calls.</summary>
public sealed class RealmsClient : IRealmsClient
{
    private readonly RealmsHttpClient _http;
    private readonly Uri _baseUri;
    private readonly ILogger _logger;

    /// <summary>Creates a Realms client from the supplied options. The credential is required.</summary>
    public RealmsClient(RealmsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Credential);
        _baseUri = options.BaseUri;
        _logger = options.Logger;
        _http = new RealmsHttpClient(options.HttpHandlerFactory, options.Credential.ToCookieHeader());
    }

    /// <inheritdoc />
    public async Task<RealmsCompatibility> CheckClientCompatibleAsync(CancellationToken ct)
    {
        RealmsHttpResponse response = await _http.GetAsync(Endpoint("/mco/client/compatible"), ct).ConfigureAwait(false);
        EnsureSuccess("compatible", response);

        string verdict = StripQuotes(response.Body).Trim().ToUpperInvariant();
        RealmsCompatibility compatibility = verdict switch
        {
            "COMPATIBLE" => RealmsCompatibility.Compatible,
            "OUTDATED" => RealmsCompatibility.Outdated,
            "OTHER" => RealmsCompatibility.Other,
            _ => RealmsCompatibility.Unknown,
        };
        _logger.LogDebug("Realms client compatibility check returned {Compatibility}.", compatibility);
        return compatibility;
    }

    /// <inheritdoc />
    public async Task AgreeToTermsAsync(CancellationToken ct)
    {
        RealmsHttpResponse response = await _http.PostAsync(Endpoint("/mco/tos/agreed"), null, ct).ConfigureAwait(false);
        EnsureSuccess("tos", response);
        _logger.LogDebug("Realms terms of service acknowledged.");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RealmWorld>> ListWorldsAsync(CancellationToken ct)
    {
        RealmsHttpResponse response = await _http.GetAsync(Endpoint("/worlds"), ct).ConfigureAwait(false);
        EnsureSuccess("worlds", response);

        RealmsWorldsResponseDto? dto = Deserialize("worlds", response.Body, RealmsJsonContext.Default.RealmsWorldsResponseDto);
        if (dto?.Servers is not { Count: > 0 } servers)
            return [];

        var worlds = new List<RealmWorld>(servers.Count);
        foreach (RealmWorldDto server in servers)
            worlds.Add(MapWorld(server));

        _logger.LogDebug("Realms worlds list returned {Count} world(s).", worlds.Count);
        return worlds;
    }

    /// <inheritdoc />
    public async Task<RealmServerAddress> JoinWorldAsync(long worldId, CancellationToken ct)
    {
        string path = string.Create(CultureInfo.InvariantCulture, $"/worlds/v1/{worldId}/join/pc");
        RealmsHttpResponse response = await _http.GetAsync(Endpoint(path), ct).ConfigureAwait(false);
        EnsureSuccess("join", response);

        RealmsJoinResponseDto? dto = Deserialize("join", response.Body, RealmsJsonContext.Default.RealmsJoinResponseDto);
        if (string.IsNullOrEmpty(dto?.Address))
            throw new RealmsServiceException(
                "join", response.StatusCode, RealmsErrorKind.InvalidResponse,
                "The Realms join response did not contain a server address.");

        try
        {
            RealmServerAddress address = RealmServerAddress.Parse(dto.Address, dto.ResourcePackUrl, dto.ResourcePackHash);
            _logger.LogDebug("Realms world {WorldId} resolved to {Address}.", worldId, address);
            return address;
        }
        catch (FormatException ex)
        {
            throw new RealmsServiceException(
                "join", response.StatusCode, RealmsErrorKind.InvalidResponse,
                "The Realms join response contained a malformed server address.", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private Uri Endpoint(string path) => new(_baseUri, path);

    private static RealmWorld MapWorld(RealmWorldDto dto) => new(
        dto.Id,
        dto.Name ?? string.Empty,
        dto.Motd ?? string.Empty,
        dto.Owner ?? string.Empty,
        ParseUuid(dto.OwnerUuid),
        ParseState(dto.State),
        dto.WorldType ?? string.Empty,
        dto.Expired,
        dto.ExpiredTrial,
        dto.DaysLeft,
        dto.MaxPlayers,
        dto.ActiveSlot,
        dto.Member);

    private static RealmState ParseState(string? state) => state switch
    {
        "OPEN" => RealmState.Open,
        "CLOSED" => RealmState.Closed,
        "UNINITIALIZED" => RealmState.Uninitialized,
        _ => RealmState.Unknown,
    };

    private static Guid? ParseUuid(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        if (id.Contains('-', StringComparison.Ordinal))
            return Guid.TryParse(id, out Guid dashed) ? dashed : null;

        return Guid.TryParseExact(id, "N", out Guid undashed) ? undashed : null;
    }

    private static string StripQuotes(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }

    private static T? Deserialize<T>(string operation, string body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(string.IsNullOrEmpty(body) ? "{}" : body, typeInfo);
        }
        catch (JsonException ex)
        {
            throw new RealmsServiceException(
                operation, 0, RealmsErrorKind.InvalidResponse,
                "The Realms " + operation + " response was not valid JSON.", ex);
        }
    }

    private static void EnsureSuccess(string operation, RealmsHttpResponse response)
    {
        // Although 277 is in the 200-299 range, the Realms contract defines it as a retry signal.
        if (response.IsSuccess && response.StatusCode != 277)
            return;

        RealmsErrorClassification classification = RealmsErrorParser.Classify(response.StatusCode, response.Body);
        throw new RealmsServiceException(
            operation,
            response.StatusCode,
            classification.Kind,
            BuildFailureMessage(operation, response.StatusCode, classification))
        {
            ErrorCode = classification.ErrorCode,
            Reason = classification.Reason,
        };
    }

    private static string BuildFailureMessage(string operation, int statusCode, RealmsErrorClassification classification)
    {
        string message = "Realms " + operation + " request failed with HTTP status "
            + statusCode.ToString(CultureInfo.InvariantCulture) + " (" + classification.Kind + ").";
        return classification.Reason is { Length: > 0 } reason
            ? message + " Reason: " + reason + "."
            : message;
    }
}
