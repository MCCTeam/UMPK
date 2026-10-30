namespace Umpk.Protocol.Java;

/// <summary>A declared unmodeled compact component that prevented one frame from decoding. This ordered disposition is not a decoded packet; its original frame remains available for diagnostics.</summary>
public sealed record InboundDecodeLoss(
    ProtocolPhase Phase,
    PacketFlow Flow,
    InboundFrame Frame,
    int ComponentWireId,
    Identifier ComponentId,
    InventoryUpdateLossScope? InventoryScope)
{
    /// <summary>The insertion position before an ordinary packet in a bundle, from zero through its packet count. Null for a loss delivered outside a bundle. Multiple losses at the same position retain frame order.</summary>
    public int? BeforePacketIndex { get; init; }
}
