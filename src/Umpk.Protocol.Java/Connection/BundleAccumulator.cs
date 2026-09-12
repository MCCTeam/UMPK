namespace Umpk.Protocol.Java;

/// <summary>Accumulates decoded packets between two <c>minecraft:bundle_delimiter</c> frames (1.19.4+) and emits a <see cref="PacketBundle"/> atomically when the closing delimiter arrives. Driven by <see cref="JavaConnection"/>'s delivery path (one instance per bound connection) strictly in wire order, or by a session-layer loop. The wire limit is 4096 packets.</summary>
public sealed class BundleAccumulator
{
    /// <summary>Maximum packets a single bundle may hold.</summary>
    public const int BundleSizeLimit = 4096;

    private readonly Identifier _delimiterId;

    private readonly bool _hasDelimiterId;

    private List<object>? _current;

    private List<InboundFrame>? _currentFrames;

    /// <summary>Creates an accumulator keyed on the bundle-delimiter identity for the version.</summary>
    public BundleAccumulator(Identifier delimiterId)
    {
        _delimiterId = delimiterId;
        _hasDelimiterId = true;
    }

    /// <summary>Creates an accumulator whose delimiter detection is external: the caller passes an explicit <c>isDelimiter</c> to <see cref="Offer(object, bool, out PacketBundle?)"/>. Used by <see cref="JavaConnection"/>, which identifies the delimiter through its codec binding.</summary>
    public BundleAccumulator()
    {
    }

    /// <summary>True while a bundle is open (between delimiters).</summary>
    public bool IsAccumulating => _current is not null;

    /// <summary>Offers a decoded packet. When the packet is the bundle delimiter, this toggles bundle state: the first delimiter opens a bundle (returns <see cref="BundleFeed.Opened"/>), the second closes it and yields the assembled <see cref="PacketBundle"/> (<see cref="BundleFeed.Closed"/>). Any non-delimiter packet while a bundle is open is buffered (<see cref="BundleFeed.Buffered"/>); outside a bundle it passes through (<see cref="BundleFeed.PassThrough"/>).</summary>
    public BundleFeed Offer(object packet, out PacketBundle? bundle)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (!_hasDelimiterId)
            throw new InvalidOperationException(
                "This accumulator was created without a delimiter id; use Offer(packet, isDelimiter, out bundle).");

        bool isDelimiter = packet is IPacket p && p.Type.Id == _delimiterId;
        return Offer(packet, isDelimiter, out bundle);
    }

    /// <summary>
    /// Offers a decoded packet with an externally determined delimiter flag. Semantics match <see cref="Offer(object, out PacketBundle?)"/>: the first delimiter opens a bundle, the second closes it and yields the assembled <see cref="PacketBundle"/>, interior packets are buffered, and packets outside a bundle pass through. Used when the delimiter is identified by the caller (for example a <see cref="JavaConnection"/> via its codec binding) rather than by identity here.
    /// <para>The assembled bundle's <see cref="PacketBundle.Frames"/> entries are <c>default</c>, because this overload is never told which frame produced each packet. Callers that have the frame should use <see cref="Offer(object, bool, in InboundFrame, out PacketBundle?)"/> so bundled packets keep their real wire id and byte count.</para>
    /// </summary>
    public BundleFeed Offer(object packet, bool isDelimiter, out PacketBundle? bundle)
        => Offer(packet, isDelimiter, default, out bundle);

    /// <summary>Offers a decoded packet together with the frame it was decoded from. Bundle semantics are identical to <see cref="Offer(object, bool, out PacketBundle?)"/>; the difference is that the assembled <see cref="PacketBundle"/> carries each packet's frame in <see cref="PacketBundle.Frames"/> at the same index. A bundle crosses the connection as a single item with no frame of its own, so this is the only place the per-packet wire id and byte count can be preserved.</summary>
    public BundleFeed Offer(object packet, bool isDelimiter, in InboundFrame frame, out PacketBundle? bundle)
    {
        ArgumentNullException.ThrowIfNull(packet);
        bundle = null;

        if (isDelimiter)
        {
            if (_current is null)
            {
                _current = [];
                _currentFrames = [];
                return BundleFeed.Opened;
            }

            bundle = new PacketBundle(_current, _currentFrames!);
            _current = null;
            _currentFrames = null;
            return BundleFeed.Closed;
        }

        if (_current is null)
            return BundleFeed.PassThrough;

        if (_current.Count >= BundleSizeLimit)
            throw new ProtocolViolationException($"Bundle exceeded the {BundleSizeLimit}-packet limit.");

        _current.Add(packet);
        _currentFrames!.Add(frame);
        return BundleFeed.Buffered;
    }
}

/// <summary>The outcome of feeding a packet to a <see cref="BundleAccumulator"/>.</summary>
public enum BundleFeed
{
    /// <summary>The packet is not part of a bundle; deliver it normally.</summary>
    PassThrough,

    /// <summary>An opening delimiter started a bundle; withhold delivery until it closes.</summary>
    Opened,

    /// <summary>A non-delimiter packet was buffered into the open bundle.</summary>
    Buffered,

    /// <summary>A closing delimiter completed the bundle; deliver the assembled <see cref="PacketBundle"/>.</summary>
    Closed,
}
