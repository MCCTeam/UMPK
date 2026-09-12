using System.IO.Pipelines;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Establishes a duplex byte pipe to a server endpoint. The seam for proxies, Tor, in-memory tests, and custom sockets. Implementations perform whatever tunneling or handshaking they need and hand back a connected <see cref="IDuplexPipe"/>; <see cref="JavaConnection"/> wraps it.</summary>
public interface IConnectionFactory
{
    /// <summary>Connects to <paramref name="endpoint"/>, returning a connected duplex pipe.</summary>
    ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct);
}
