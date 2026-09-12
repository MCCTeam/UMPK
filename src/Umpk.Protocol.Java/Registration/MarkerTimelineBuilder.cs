namespace Umpk.Protocol.Java;

/// <summary>Fluent surface for an identifier the dataset registers and this library deliberately does not bind. It is a timeline of marker steps and nothing else, because there is no packet record to bind: the wire form was never modelled, so there is no <c>PacketType</c> to hang a <see cref="PacketTimeline"/> on and no codec a step could name.</summary>
internal sealed class MarkerTimelineBuilder
{
    private readonly PacketTimeline _timeline;

    internal MarkerTimelineBuilder(PacketTimeline timeline) => _timeline = timeline;

    /// <summary>Declares the absence from <paramref name="fromProtocol"/> onward, and why it is one.</summary>
    /// <param name="fromProtocol">The first protocol this absence governs.</param>
    /// <param name="reason">Which <see cref="MarkerReason"/> the allowlist entry must carry.</param>
    /// <param name="why">The prose justification, held to the bar the allowlist entry is held to.</param>
    public MarkerTimelineBuilder MarkerFrom(int fromProtocol, MarkerReason reason, string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        _timeline.AddMarker(fromProtocol, reason, why);
        return this;
    }
}
