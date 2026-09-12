namespace Umpk.Protocol.Java.Transport;

/// <summary>Address and optional credentials for a forwarding proxy.</summary>
public sealed record ProxyOptions
{
    /// <summary>Proxy host.</summary>
    public required string Host { get; init; }

    /// <summary>Proxy port.</summary>
    public required ushort Port { get; init; }

    /// <summary>Optional username for authenticated proxies.</summary>
    public string? Username { get; init; }

    /// <summary>Optional password for authenticated proxies.</summary>
    public string? Password { get; init; }
}
