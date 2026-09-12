namespace Umpk.Protocol.Java;

/// <summary>One resolved timeline step: registers the era codec into the builder at a wire id, told which dataset identifier resolved it. The identifier is threaded through rather than inferred because the packet type alone cannot answer it: a curated legacy alias resolves the same timeline and would otherwise be indistinguishable from the canonical name once the descriptor is built.</summary>
/// <param name="builder">The descriptor being built.</param>
/// <param name="wireId">The wire id the dataset registered the packet at.</param>
/// <param name="datasetId">The identifier the dataset spelled the packet with.</param>
internal delegate void TimelineBind(ProtocolDescriptorBuilder builder, int wireId, Identifier datasetId);

/// <summary>One packet's whole cross-version story: an ordered list of steps, each valid from a protocol number onward. Resolution for protocol P is the step with the greatest <c>fromProtocol &lt;= P</c>; a bind step registers the era codec, a marker step (or no step at all) leaves the packet not yet implemented. Because protocol numbers are chronological, this subsumes every era hook, era-key switch, and per-protocol refinement: one packet's timeline lives in exactly one place and adding a version only touches packets whose wire changed.</summary>
internal sealed class PacketTimeline
{
    // Steps kept sorted ascending by FromProtocol. A null Bind is an explicit "not yet implemented" marker step (an honest deferral that can override a bind for a middle era).
    private readonly List<Step> _steps = [];

    /// <summary>False for a timeline nothing has been added to, which resolves nothing on any protocol.</summary>
    internal bool HasSteps => _steps.Count > 0;

    /// <summary>Adds a step at <paramref name="fromProtocol"/>. A null <paramref name="bind"/> is a marker step.</summary>
    internal void Add(int fromProtocol, TimelineBind? bind) => Insert(new Step(fromProtocol, bind, null));

    /// <summary>Adds a marker step at <paramref name="fromProtocol"/> carrying the reason it is one.</summary>
    internal void AddMarker(int fromProtocol, MarkerReason reason, string why) =>
        Insert(new Step(fromProtocol, null, new MarkerDeclaration(fromProtocol, reason, why)));

    /// <summary>Resolves the step governing <paramref name="protocol"/>. Returns true with the bind action when the governing step is an implemented codec; false when it is a marker step or when no step covers the protocol (both leave the packet as a not-implemented marker). <paramref name="declaration"/> carries the governing marker step's reason, and is null for an undeclared marker.</summary>
    internal bool TryResolve(int protocol, out TimelineBind bind, out MarkerDeclaration? declaration)
    {
        Step? found = null;
        foreach (Step step in _steps)
            if (step.FromProtocol <= protocol)
                found = step;

            else
                break;

        bind = found?.Bind!;
        declaration = found?.Declaration;
        return bind is not null;
    }

    private void Insert(Step step)
    {
        int i = _steps.FindIndex(s => s.FromProtocol == step.FromProtocol);
        if (i >= 0)
            throw new InvalidOperationException(
                $"Duplicate timeline step at protocol {step.FromProtocol}.");

        int insert = _steps.FindIndex(s => s.FromProtocol > step.FromProtocol);
        if (insert < 0)
            _steps.Add(step);

        else
            _steps.Insert(insert, step);

    }

    private sealed record Step(int FromProtocol, TimelineBind? Bind, MarkerDeclaration? Declaration);
}
