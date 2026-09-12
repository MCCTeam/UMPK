namespace Umpk;

/// <summary>A server address as the user expressed it: host name (or IP literal) plus port. SRV resolution and DNS happen later, behind <c>IServerAddressResolver</c> in the protocol package.</summary>
public sealed record ServerEndpoint(string Host, ushort Port = ServerEndpoint.DefaultJavaPort)
{
    /// <summary>The default Java Edition server port.</summary>
    public const ushort DefaultJavaPort = 25565;

    /// <summary>Parses <c>"host"</c>, <c>"host:port"</c>, <c>"[ipv6]"</c> or <c>"[ipv6]:port"</c>.</summary>
    /// <exception cref="FormatException">The input is not a valid endpoint.</exception>
    public static ServerEndpoint Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out var endpoint)
            ? endpoint
            : throw new FormatException($"Invalid server endpoint '{value}'.");
    }

    /// <summary>Attempts to parse an endpoint; see <see cref="Parse"/> for the accepted forms.</summary>
    public static bool TryParse(string? value, out ServerEndpoint endpoint)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        string host;
        string? portPart = null;

        if (value.StartsWith('['))
        {
            int close = value.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
                return false;

            host = value[1..close];
            string rest = value[(close + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(':'))
                    return false;

                portPart = rest[1..];
            }
        }
        else
        {
            int colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0 && value.IndexOf(':', colon + 1) >= 0)
            {
                // Bare IPv6 literal without brackets: treat the whole string as the host.
                host = value;
            }
            else if (colon >= 0)
            {
                host = value[..colon];
                portPart = value[(colon + 1)..];
            }
            else
                host = value;

        }

        if (host.Length == 0)
            return false;

        ushort port = DefaultJavaPort;
        if (portPart is not null && !ushort.TryParse(portPart, System.Globalization.CultureInfo.InvariantCulture, out port))
            return false;

        endpoint = new ServerEndpoint(host, port);
        return true;
    }

    public override string ToString() =>
        Host.Contains(':', StringComparison.Ordinal) ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}
