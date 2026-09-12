namespace Umpk.TestKit.Corpus;

/// <summary>The travel direction of a recorded frame, mirroring <c>Umpk.Protocol.Java.PacketFlow</c> but kept as an independent TestKit enum so the corpus format does not bind to a protocol enum's numeric layout.</summary>
public enum CorpusDirection : byte
{
    /// <summary>Server to client (clientbound).</summary>
    Clientbound = 0,

    /// <summary>Client to server (serverbound).</summary>
    Serverbound = 1,
}
