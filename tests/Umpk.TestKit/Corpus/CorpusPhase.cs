namespace Umpk.TestKit.Corpus;

/// <summary>The protocol phase a recorded frame was observed in, mirroring <c>Umpk.Protocol.Java.ProtocolPhase</c> as a stable on-disk enum.</summary>
public enum CorpusPhase : byte
{
    /// <summary>Initial handshake phase.</summary>
    Handshake = 0,

    /// <summary>Server list ping exchange.</summary>
    Status = 1,

    /// <summary>Authentication/compression negotiation.</summary>
    Login = 2,

    /// <summary>Registry sync/feature negotiation (1.20.2+).</summary>
    Configuration = 3,

    /// <summary>In-game play.</summary>
    Play = 4,
}
