using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Transport;

namespace Umpk.Transport.RakNet;

/// <summary>Dials RakNet sessions and adapts them to the owned-message connection seam.</summary>
/// <remarks>The factory owns dialing only; each returned <see cref="IMessageConnection"/> owns its session socket, retry timers, and terminal outcome, and the corrected fork behind it enforces generic wire and reliability invariants without any Bedrock knowledge.</remarks>
public sealed class RakNetConnectionFactory
{
    private readonly RakNetConnectionOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates a factory using the given bounds and logger source, falling back to defaults and a null logger factory when omitted.</summary>
    /// <param name="options">The queue, message, and MTU bounds applied to every dialed session, or <see langword="null"/> for the defaults.</param>
    /// <param name="loggerFactory">The factory used for fork diagnostics and adapter logging, or <see langword="null"/> to discard logs.</param>
    public RakNetConnectionFactory(
        RakNetConnectionOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? new RakNetConnectionOptions();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>Dials one RakNet session to <paramref name="endpoint"/> and returns it as an owned-message connection.</summary>
    /// <param name="endpoint">The server endpoint, which must be IPv4 since direct dialing does not yet support other address families.</param>
    /// <param name="cancellationToken">Cancels the dial at any handshake stage; a cancelled dial owns and releases every socket, timer, and retry it started.</param>
    /// <returns>The established session as an owned-message connection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException"><paramref name="endpoint"/> is not an IPv4 endpoint.</exception>
    /// <exception cref="OperationCanceledException">The dial was cancelled before the session was established.</exception>
    public async ValueTask<IMessageConnection> ConnectAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.AddressFamily != AddressFamily.InterNetwork)
            throw new NotSupportedException("direct RakNet connections currently support IPv4 endpoints only");

        ILogger<RakNetMessageConnection> logger = _loggerFactory.CreateLogger<RakNetMessageConnection>();
        var dialer = new global::RakNet.Dialer
        {
            MaxMTU = _options.MaximumMtu,
            Limits = _options.ToLimits(),
            ErrorLog = message => RakNetLog.ForkDiagnostic(logger, message)
        };

        try
        {
            global::RakNet.Conn connection = await dialer.DialAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new RakNetMessageConnection(connection, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RakNetLog.DialFailed(logger, endpoint, ex);
            throw;
        }
    }
}
