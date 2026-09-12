namespace Umpk.Protocol.Java;

/// <summary>A delivered inbound item from <see cref="JavaConnection.ReceiveAsync"/>. In the common case a codec binding is attached and <see cref="Packet"/> holds the decoded packet object; without one the item is frame-only and <see cref="Frame"/> carries the raw bytes. When the connection assembles a bundle (1.19.4+, packets between two <c>bundle_delimiter</c> frames) it delivers a single item whose <see cref="Bundle"/> is set and whose <see cref="Packet"/>/<see cref="Frame"/> are empty, so the whole bundle crosses the channel atomically. The transport core is agnostic to the concrete packet type, so <see cref="Packet"/> is typed as <see cref="object"/>; The higher-level receive surface casts it to <c>IPacket</c>.</summary>
public readonly struct InboundItem
{
    internal InboundItem(object? packet, InboundFrame frame)
    {
        Packet = packet;
        Frame = frame;
        Bundle = null;
    }

    internal InboundItem(PacketBundle? bundle)
    {
        Packet = null;
        Frame = default;
        Bundle = bundle;
    }

    /// <summary>The decoded packet, or null when no binding decoded this frame or this item is a bundle.</summary>
    public object? Packet { get; }

    /// <summary>The raw frame (wire id + pooled payload); default for a bundle item.</summary>
    public InboundFrame Frame { get; }

    /// <summary>The atomically-delivered bundle (1.19.4+), or null for an ordinary single-frame item.</summary>
    public PacketBundle? Bundle { get; }
}

/// <summary>A predicate selecting which frames a decode filter decodes in frame mode. Frames whose wire id matches are decoded (when a binding is attached) in addition to being delivered raw; the rest cross verbatim. The proxy uses this to decode only subscribed types.</summary>
public sealed class PacketDecodeFilter
{
    private readonly Func<ProtocolPhase, PacketFlow, int, bool> _predicate;

    /// <summary>Creates a filter from a predicate over (phase, flow, wireId).</summary>
    public PacketDecodeFilter(Func<ProtocolPhase, PacketFlow, int, bool> predicate)
        => _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));

    /// <summary>A filter that decodes nothing (pure passthrough).</summary>
    public static PacketDecodeFilter None { get; } = new((_, _, _) => false);

    /// <summary>A filter that decodes every frame.</summary>
    public static PacketDecodeFilter All { get; } = new((_, _, _) => true);

    /// <summary>Evaluates the filter for a frame.</summary>
    public bool ShouldDecode(ProtocolPhase phase, PacketFlow flow, int wireId)
        => _predicate(phase, flow, wireId);
}
