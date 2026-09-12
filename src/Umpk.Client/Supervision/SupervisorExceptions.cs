namespace Umpk.Client;

/// <summary>Thrown by <see cref="UmpkClientSupervisor.StartAsync"/> or <see cref="UmpkClientSupervisor.ReconnectAsync"/> when a session attempt's <see cref="UmpkClient.ConnectAsync"/> failed for any reason other than the server rejecting the login (see <see cref="LoginRejectedException"/> for that case) or an <see cref="OperationCanceledException"/>. Covers a refused socket, a DNS failure, a protocol violation, or a transport close that carried no server text.</summary>
public sealed class ConnectFailedException : SessionStartException
{
    /// <summary>Creates the exception.</summary>
    public ConnectFailedException(string message, ServerEndpoint endpoint, Exception? inner = null)
        : base(message, inner)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
    }

    /// <summary>The server this attempt was dialing.</summary>
    public ServerEndpoint Endpoint { get; }
}

/// <summary>Thrown by <see cref="UmpkClientSupervisor.StartAsync"/> or <see cref="UmpkClientSupervisor.ReconnectAsync"/> when the server closed the connection during login or configuration with its own reason: a whitelist refusal, a ban, or any other deliberate kick before play began. Never thrown for a kick that arrives after play has started; that ends the live session instead and is reported through <see cref="UmpkClientSupervisor.StatusChanged"/>.</summary>
public sealed class LoginRejectedException : SessionStartException
{
    /// <summary>Creates the exception, carrying the server's own decoded reason when it sent one.</summary>
    public LoginRejectedException(string message, Umpk.Text.Component? reason, Exception? inner = null)
        : base(message, inner)
        => Reason = reason;

    /// <summary>The server's own kick reason, or null when none was decoded.</summary>
    public Umpk.Text.Component? Reason { get; }
}
