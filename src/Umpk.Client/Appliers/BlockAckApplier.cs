using Umpk.Client.Internal;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies the block-change acknowledgment packet (1.19+): feeds the acknowledged sequence into the <see cref="SequenceTracker"/> so dig/place futures complete on protocol acknowledgment. Feature-independent (dig/place do not require any state module), so it is always registered.</summary>
internal sealed class BlockAckApplier : IApplier
{
    public ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        if (packet is ClientboundBlockChangedAckPacket ack)
        {
            context.Sequences.Acknowledge(ack.Sequence);
            return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(false);
    }
}
