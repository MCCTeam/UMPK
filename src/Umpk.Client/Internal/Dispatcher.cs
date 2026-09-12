namespace Umpk.Client.Internal;

/// <summary>Routes a decoded packet through the ordered applier chain. The first applier that owns the packet type handles it; unowned packets fall through harmlessly (a disabled feature simply never registered its applier).</summary>
internal sealed class Dispatcher(IReadOnlyList<IApplier> appliers)
{
    private readonly IReadOnlyList<IApplier> _appliers = appliers;

    public async ValueTask DispatchAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        foreach (IApplier applier in _appliers)
            if (await applier.TryApplyAsync(packet, context, ct).ConfigureAwait(false))
                return;

    }
}
