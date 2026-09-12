namespace Umpk.Client.Internal;

/// <summary>A state applier translates one family of decoded inbound packets into <see cref="ClientState"/> mutations and event publications. Appliers run on the session loop, in packet order.</summary>
internal interface IApplier
{
    /// <summary>Applies a decoded packet if this applier owns its type. Returns true when the packet was handled (so the dispatcher can stop), false to let other appliers try.</summary>
    ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct);
}
