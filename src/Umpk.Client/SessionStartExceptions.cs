namespace Umpk.Client;

/// <summary>Base type for exceptions thrown while establishing a session, before any packet has gone out. A closed hierarchy: only the leaves declared in this assembly derive from it.</summary>
public abstract class SessionStartException : Exception
{
    private protected SessionStartException(string message, Exception? inner)
        : base(message, inner)
    {
    }
}

/// <summary>Thrown by <see cref="UmpkClientBuilder.BuildForAsync"/> when nobody can supply a protocol version: <see cref="UmpkClientBuilder.UseVersion"/> was never called, and the status ping used for automatic detection failed, answered without a usable <c>version.protocol</c>, or reported a protocol UMPK has no data for.</summary>
public sealed class VersionResolutionException : SessionStartException
{
    /// <summary>Creates the exception, carrying the diagnostic from a failed <see cref="ServerVersionNegotiator"/> detection.</summary>
    public VersionResolutionException(
        string message, string host, ushort port, int? protocol, VersionNegotiationFailure failure,
        Exception? inner = null)
        : base(message, inner)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        Port = port;
        Protocol = protocol;
        Failure = failure;
    }

    /// <summary>The host that was pinged.</summary>
    public string Host { get; }

    /// <summary>The port that was pinged.</summary>
    public ushort Port { get; }

    /// <summary>The reported protocol number, when the ping itself succeeded but no matching version was found. Null when the ping failed before any protocol number could be read.</summary>
    public int? Protocol { get; }

    /// <summary>Why detection failed.</summary>
    public VersionNegotiationFailure Failure { get; }
}
