using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java;

/// <summary>The result of a server list ping, decoded from the raw status JSON and paired with its measured latency. <see cref="Json"/> retains the response verbatim, because the fields servers actually put in there go well past the ones vanilla documents.</summary>
public sealed class ServerStatus
{
    /// <summary>The response body verbatim, unparsed.</summary>
    public required string Json { get; init; }

    /// <summary>The measured round-trip latency of the ping/pong exchange.</summary>
    public required TimeSpan Latency { get; init; }

    /// <summary>The reported <c>version.name</c>, or null when absent or malformed.</summary>
    public string? VersionName { get; init; }

    /// <summary>The reported <c>version.protocol</c>, or null when absent or malformed.</summary>
    public int? Protocol { get; init; }

    /// <summary>The reported <c>players.online</c>, or null when absent or malformed.</summary>
    public int? OnlinePlayers { get; init; }

    /// <summary>The reported <c>players.max</c>, or null when absent or malformed.</summary>
    public int? MaxPlayers { get; init; }

    /// <summary>The reported <c>players.sample</c> list. Empty when absent, empty, or malformed.</summary>
    public IReadOnlyList<ServerStatusPlayer> Sample { get; init; } = [];

    /// <summary>The reported <c>description</c> (MOTD), decoded to a component tree, or null when absent.</summary>
    public Component? Description { get; init; }

    /// <summary>The decoded <c>favicon</c> PNG bytes. Empty when absent or malformed.</summary>
    public ReadOnlyMemory<byte> Favicon { get; init; }

    /// <summary>The reported <c>enforcesSecureChat</c> flag. False when absent.</summary>
    public bool EnforcesSecureChat { get; init; }

    /// <summary>Parses the status JSON into a <see cref="ServerStatus"/>. Parsing is total: a malformed sub-object yields null (or an empty collection) for that field rather than failing the whole parse.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    public static ServerStatus Parse(string json, TimeSpan latency)
    {
        ArgumentNullException.ThrowIfNull(json);

        string? versionName = null;
        int? protocol = null;
        int? onlinePlayers = null;
        int? maxPlayers = null;
        IReadOnlyList<ServerStatusPlayer> sample = [];
        Component? description = null;
        ReadOnlyMemory<byte> favicon = ReadOnlyMemory<byte>.Empty;
        bool enforcesSecureChat = false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                // Every sub-object below is its own try/catch island: a fault decoding one field must not abort the others.
                try
                {
                    if (root.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.Object)
                    {
                        if (version.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
                            versionName = name.GetString();

                        protocol = ReadProtocol(version);
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    if (root.TryGetProperty("players", out JsonElement players) && players.ValueKind == JsonValueKind.Object)
                    {
                        if (players.TryGetProperty("online", out JsonElement online) && online.ValueKind == JsonValueKind.Number)
                            onlinePlayers = online.GetInt32();

                        if (players.TryGetProperty("max", out JsonElement max) && max.ValueKind == JsonValueKind.Number)
                            maxPlayers = max.GetInt32();

                        if (players.TryGetProperty("sample", out JsonElement sampleElement))
                            sample = ReadSample(sampleElement);

                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    if (root.TryGetProperty("description", out JsonElement descriptionElement))
                        description = ReadDescription(descriptionElement);

                }
                catch (Exception)
                {
                }

                try
                {
                    if (root.TryGetProperty("favicon", out JsonElement faviconElement) && faviconElement.ValueKind == JsonValueKind.String)
                        favicon = ReadFavicon(faviconElement.GetString());

                }
                catch (Exception)
                {
                }

                try
                {
                    if (root.TryGetProperty("enforcesSecureChat", out JsonElement secureChat))
                        enforcesSecureChat = secureChat.ValueKind == JsonValueKind.True;

                }
                catch (Exception)
                {
                }
            }
        }
        catch (JsonException)
        {
            // The top-level document itself is not valid JSON; every field stays at its default.
        }

        return new ServerStatus
        {
            Json = json,
            Latency = latency,
            VersionName = versionName,
            Protocol = protocol,
            OnlinePlayers = onlinePlayers,
            MaxPlayers = maxPlayers,
            Sample = sample,
            Description = description,
            Favicon = favicon,
            EnforcesSecureChat = enforcesSecureChat,
        };
    }

    private static int? ReadProtocol(JsonElement version)
    {
        if (!version.TryGetProperty("protocol", out JsonElement protocolElement))
            return null;

        if (protocolElement.ValueKind == JsonValueKind.Number && protocolElement.TryGetInt32(out int protocol))
            return protocol;

        // A stringified protocol number is accepted here as parse-in tolerance: DFU's JsonOps rejects a string primitive as a number outside compressed mode, so vanilla itself shows no version at all for this shape, but real proxies emit one and nothing about this reaches the wire.
        if (protocolElement.ValueKind == JsonValueKind.String
            && int.TryParse(protocolElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return parsed;

        return null;
    }

    private static IReadOnlyList<ServerStatusPlayer> ReadSample(JsonElement sampleElement)
    {
        if (sampleElement.ValueKind != JsonValueKind.Array)
            return [];

        var players = new List<ServerStatusPlayer>();
        foreach (JsonElement entry in sampleElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(idElement.GetString(), out Guid id)
                || !entry.TryGetProperty("name", out JsonElement nameElement)
                || nameElement.ValueKind != JsonValueKind.String)
            {
                // Vanilla decodes the whole list as one unit; a single malformed entry fails the list, not just that entry.
                return [];
            }

            players.Add(new ServerStatusPlayer(id, nameElement.GetString()!));
        }

        return players;
    }

    private static Component? ReadDescription(JsonElement descriptionElement)
    {
        if (descriptionElement.ValueKind == JsonValueKind.String)
        {
            string text = descriptionElement.GetString() ?? string.Empty;

            // A string description decodes to a literal TextComponent in vanilla which expands section-sign codes at draw time in StringDecomposer. A style-rendering consumer needs them expanded up front to reach the same rendered result.
            return text.Contains(LegacyText.Prefix, StringComparison.Ordinal)
                ? LegacyText.Parse(text)
                : Component.Text(text);
        }

        return ComponentJson.Parse(descriptionElement.GetRawText());
    }

    private const string FaviconPrefix = "data:image/png;base64,";

    private static ReadOnlyMemory<byte> ReadFavicon(string? value)
    {
        // The prefix is required, and embedded newlines are stripped before decoding (:43 replaceAll("\n", "")).
        if (value is null || !value.StartsWith(FaviconPrefix, StringComparison.Ordinal))
            return ReadOnlyMemory<byte>.Empty;

        string base64 = value[FaviconPrefix.Length..].Replace("\n", string.Empty, StringComparison.Ordinal);
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
    }
}

/// <summary>One entry of a status response's <c>players.sample</c> list.</summary>
public readonly record struct ServerStatusPlayer(Guid Id, string Name);

/// <summary>Options for <see cref="JavaStatus"/> queries.</summary>
public sealed record JavaStatusOptions
{
    /// <summary>Overall timeout for the exchange.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Logger for diagnostics.</summary>
    public ILogger Logger { get; init; } = NullLogger.Instance;
}
