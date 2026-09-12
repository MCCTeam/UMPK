namespace Umpk.Protocol.Java.Transport;

/// <summary>Resolves a user-facing endpoint into the endpoint to actually dial, following Minecraft's <c>_minecraft._tcp.&lt;host&gt;</c> SRV convention. IP literals and hosts without an SRV record resolve to themselves. UMPK ships a small internal DNS-over-UDP resolver so the protocol-only consumer needs no extra dependency.</summary>
public interface IServerAddressResolver
{
    /// <summary>Returns the endpoint to connect to. When an SRV record is found its target host and port replace the input; otherwise the input endpoint is returned unchanged.</summary>
    ValueTask<ServerEndpoint> ResolveAsync(ServerEndpoint endpoint, CancellationToken ct);
}
