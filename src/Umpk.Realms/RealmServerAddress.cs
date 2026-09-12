using System.Globalization;

namespace Umpk.Realms;

/// <summary>The connection endpoint for a Realms world. Connect to <see cref="Host"/>:<see cref="Port"/> as for any Java server. The optional resource-pack fields are surfaced when the join payload carries them.</summary>
/// <param name="Host">The server host (IP or DNS name).</param>
/// <param name="Port">The server port.</param>
/// <param name="ResourcePackUrl">The world resource-pack URL, when the join payload carries one.</param>
/// <param name="ResourcePackHash">The world resource-pack SHA-1 hash, when the join payload carries one.</param>
public sealed record RealmServerAddress(
    string Host,
    int Port,
    string? ResourcePackUrl = null,
    string? ResourcePackHash = null)
{
    /// <summary>The default Java Edition port used when a join address omits an explicit port.</summary>
    public const int DefaultPort = 25565;

    /// <summary>Parses a Realms <c>address</c> string of the form <c>host:port</c> (or bare <c>host</c>, which defaults to <see cref="DefaultPort"/>). Throws <see cref="FormatException"/> for empty input or a non-numeric port.</summary>
    public static RealmServerAddress Parse(string address, string? resourcePackUrl = null, string? resourcePackHash = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);

        int separator = address.LastIndexOf(':');
        if (separator < 0)
            return new RealmServerAddress(address, DefaultPort, resourcePackUrl, resourcePackHash);

        string host = address[..separator];
        string portText = address[(separator + 1)..];
        if (host.Length == 0)
            throw new FormatException("Realms join address has an empty host: '" + address + "'.");

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port))
            throw new FormatException("Realms join address has a non-numeric port: '" + address + "'.");

        return new RealmServerAddress(host, port, resourcePackUrl, resourcePackHash);
    }

    /// <summary>Renders the endpoint as <c>host:port</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");
}
