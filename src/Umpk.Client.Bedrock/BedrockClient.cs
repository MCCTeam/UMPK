using System.Net;
using System.Net.Sockets;
using Umpk.Client.Bedrock.Session;
using Umpk.Protocol.Bedrock;
using Umpk.Transport;
using Umpk.Transport.RakNet;

namespace Umpk.Client.Bedrock;

internal sealed class BedrockClient : IAsyncDisposable
{
    private readonly BedrockClientSettings _settings;
    private readonly Func<IPEndPoint, CancellationToken, ValueTask<IMessageConnection>> _dialAsync;
    private bool _disposed;

    internal BedrockClient(
        BedrockClientSettings settings,
        Func<IPEndPoint, CancellationToken, ValueTask<IMessageConnection>>? dialAsync = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _settings = settings;
        _dialAsync = dialAsync ?? DefaultDialAsync;
    }

    internal async ValueTask<BedrockClientSession> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IPEndPoint endpoint = await ResolveIPv4EndpointAsync(_settings.Host, _settings.Port, cancellationToken).ConfigureAwait(false);

        IMessageConnection transport;
        try
        {
            using var dialTimeout = new CancellationTokenSource(_settings.Timeouts.Dial, _settings.TimeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, dialTimeout.Token);
            transport = await _dialAsync(endpoint, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new BedrockSessionException(new BedrockTermination(
                BedrockSessionEndReason.Timeout,
                BedrockSessionScope.Session,
                "The RakNet dial timed out.",
                exception));
        }

        var key = Authentication.BedrockConnectionKey.Create();
        try
        {
            var routing = new BedrockCodecContext(
                _settings.Protocol,
                BedrockPacketDirection.Clientbound,
                default,
                default);
            var connection = new BedrockConnection(
                transport,
                new BedrockSessionCodecContext(routing, BedrockRegistrySnapshot.Empty, BedrockCodecLimits.Default));
            return new BedrockClientSession(_settings, transport, connection, key);
        }
        catch
        {
            key.Dispose();
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static ValueTask<IMessageConnection> DefaultDialAsync(IPEndPoint endpoint, CancellationToken cancellationToken) =>
        new RakNetConnectionFactory().ConnectAsync(endpoint, cancellationToken);

    private static async ValueTask<IPEndPoint> ResolveIPv4EndpointAsync(string host, ushort port, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            if (literal.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("Bedrock join supports direct IPv4 endpoints only.");
            return new IPEndPoint(literal, port);
        }

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        IPAddress? ipv4 = Array.Find(addresses, static address => address.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 is null)
            throw new NotSupportedException("Bedrock join supports direct IPv4 endpoints only.");
        return new IPEndPoint(ipv4, port);
    }
}
