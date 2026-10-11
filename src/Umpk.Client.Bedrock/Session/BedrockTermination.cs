namespace Umpk.Client.Bedrock.Session;

/// <summary>Names why a Bedrock session stopped.</summary>
internal enum BedrockSessionEndReason
{
    /// <summary>Marks a stop the local client requested through disconnect or dispose.</summary>
    LocalDisconnect,
    /// <summary>Marks a stop where the peer closed the transport without a disconnect message.</summary>
    PeerClosed,
    /// <summary>Marks a stop the server requested with a disconnect message.</summary>
    RemoteDisconnect,
    /// <summary>Marks a stop where lifetime cancellation ended the session.</summary>
    Cancelled,
    /// <summary>Marks a stop where the current join phase ran past its deadline.</summary>
    Timeout,
    /// <summary>Marks a stop where decoding or a bootstrap rule rejected the session.</summary>
    ProtocolFailure,
    /// <summary>Marks a stop where the transport itself failed.</summary>
    TransportFailure,
}

/// <summary>Names how far the effects of a session end reach.</summary>
internal enum BedrockSessionScope
{
    /// <summary>Describes an end that covers the connection itself.</summary>
    Session,
    /// <summary>Describes an end that covers the current world rather than the connection.</summary>
    World,
    /// <summary>Describes an end that covers the local player life rather than the world.</summary>
    PlayerLife,
}

/// <summary>Describes how a session ended with its cause, its scope, and optional detail.</summary>
/// <remarks>Reason tells what stopped the session while scope tells how much state the stop covers.</remarks>
internal sealed record BedrockTermination(
    BedrockSessionEndReason Reason,
    BedrockSessionScope Scope,
    string? Detail = null,
    Exception? Cause = null);
