namespace Umpk.Protocol.Java.Codecs;

/// <summary>Per-connection dynamic codec state, including the has-infinite-materials and creative gates that a few decoders consult. This is mutated only by decode-side hooks a descriptor declares on specific packets, running in wire order on the read loop; session code never writes it directly. The default empty implementation covers 47/770/776, which declare no state-mutating hooks.</summary>
public interface IConnectionCodecState
{
    /// <summary>An empty state carrying no dynamic flags.</summary>
    public static IConnectionCodecState Empty { get; } = new EmptyState();

    private sealed class EmptyState : IConnectionCodecState;
}
