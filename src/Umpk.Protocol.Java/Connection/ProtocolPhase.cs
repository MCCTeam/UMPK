namespace Umpk.Protocol.Java;

/// <summary>The five Java protocol phases. Phase selects which packet-id table is bound and is tracked per connection; transitions are driven by terminal packets. The numeric values are not wire values; the handshake's "next state" mapping lives in the login/status helpers.</summary>
public enum ProtocolPhase : byte
{
    /// <summary>Initial phase; the client sends a handshake selecting the next phase.</summary>
    Handshake,

    /// <summary>Server list ping exchange.</summary>
    Status,

    /// <summary>Authentication, encryption, and compression negotiation.</summary>
    Login,

    /// <summary>Registry sync and feature negotiation (1.20.2+).</summary>
    Configuration,

    /// <summary>In-game play.</summary>
    Play,
}
