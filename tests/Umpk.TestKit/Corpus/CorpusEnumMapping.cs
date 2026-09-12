using Umpk.Protocol.Java;

namespace Umpk.TestKit.Corpus;

/// <summary>Maps between the protocol-layer enums and the stable on-disk corpus enums.</summary>
public static class CorpusEnumMapping
{
    /// <summary>Maps a protocol <see cref="PacketFlow"/> to a <see cref="CorpusDirection"/>.</summary>
    public static CorpusDirection ToDirection(PacketFlow flow) =>
        flow == PacketFlow.Serverbound ? CorpusDirection.Serverbound : CorpusDirection.Clientbound;

    /// <summary>Maps a <see cref="CorpusDirection"/> back to a protocol <see cref="PacketFlow"/>.</summary>
    public static PacketFlow ToFlow(CorpusDirection direction) =>
        direction == CorpusDirection.Serverbound ? PacketFlow.Serverbound : PacketFlow.Clientbound;

    /// <summary>Maps a protocol <see cref="ProtocolPhase"/> to a <see cref="CorpusPhase"/>.</summary>
    public static CorpusPhase ToPhase(ProtocolPhase phase) => phase switch
    {
        ProtocolPhase.Handshake => CorpusPhase.Handshake,
        ProtocolPhase.Status => CorpusPhase.Status,
        ProtocolPhase.Login => CorpusPhase.Login,
        ProtocolPhase.Configuration => CorpusPhase.Configuration,
        ProtocolPhase.Play => CorpusPhase.Play,
        _ => CorpusPhase.Handshake,
    };

    /// <summary>Maps a <see cref="CorpusPhase"/> back to a protocol <see cref="ProtocolPhase"/>.</summary>
    public static ProtocolPhase ToProtocolPhase(CorpusPhase phase) => phase switch
    {
        CorpusPhase.Handshake => ProtocolPhase.Handshake,
        CorpusPhase.Status => ProtocolPhase.Status,
        CorpusPhase.Login => ProtocolPhase.Login,
        CorpusPhase.Configuration => ProtocolPhase.Configuration,
        CorpusPhase.Play => ProtocolPhase.Play,
        _ => ProtocolPhase.Handshake,
    };
}
