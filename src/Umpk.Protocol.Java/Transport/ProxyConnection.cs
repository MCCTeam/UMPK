using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using QuickProxyNet;

namespace Umpk.Protocol.Java.Transport;

internal static class ProxyConnection
{
    public static async ValueTask<IDuplexPipe> ConnectAsync(
        ProxyType type,
        ProxyOptions proxy,
        ServerEndpoint endpoint,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        NetworkCredential? credentials = string.IsNullOrEmpty(proxy.Username)
            ? null
            : new NetworkCredential(proxy.Username, proxy.Password ?? string.Empty);
        IProxyClient client = ProxyClientFactory.Instance.Create(type, proxy.Host, proxy.Port, credentials);

        try
        {
            string targetHost = type == ProxyType.Http && endpoint.Host.Contains(':', StringComparison.Ordinal)
                ? $"[{endpoint.Host}]"
                : endpoint.Host;
            Stream stream = await client.ConnectAsync(targetHost, endpoint.Port, ct).ConfigureAwait(false);
            return new OwnedStreamDuplexPipe(stream);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (ProxyProtocolException ex) when (
            ex.ErrorCode == ProxyErrorCode.ConnectionFailed && ex.InnerException is SocketException socketException)
        {
            ExceptionDispatchInfo.Capture(socketException).Throw();
            throw;
        }
        catch (EndOfStreamException ex)
        {
            throw new ConnectionClosedException(CloseReason.SocketEof, "Proxy closed the connection during negotiation.", ex);
        }
        catch (IOException ex)
        {
            throw new ConnectionClosedException(CloseReason.SocketEof, "Proxy I/O failed during negotiation.", ex);
        }
        catch (ProxyProtocolException ex)
        {
            throw new ConnectionClosedException(CloseReason.ProtocolViolation, ex.Message, ex);
        }
    }
}
