namespace Umpk.Protocol.Java;

/// <summary>The bridge between generated per-version descriptor code and the hand-written era codecs. The generated code knows only <c>(phase, flow, wire id, packet identifier)</c>; this facade resolves that tuple against the declarative binding table, picking the era codec whose timeline step governs the descriptor's protocol number, or registering a not-implemented marker for packets whose codec is not implemented at that protocol. Keeping codec-family knowledge in the binding families means the generated descriptors stay trivial and the codec surface stays reviewable C#.</summary>
/// <remarks>Resolution is by <see cref="ProtocolDescriptorBuilder.Protocol"/> alone: protocol numbers are totally ordered and chronological, so a per-packet timeline over protocol numbers fully determines the wire form. The dataset also labels each packet with a codec key, but that is a per-version name, not an era: across 109 to 110 all 117 identifiers change key, across 762 to 763 all 176, and across 775 to 776 only 7 of the 255 they share. It predicts nothing a timeline step does not already encode, so registration does not use it.</remarks>
public static partial class PacketRegistrar
{
    /// <summary>Registers one packet into the builder. Resolves the era codec for the builder's protocol from the packet's timeline, or registers a not-implemented marker when no timeline step covers it. Also applies the phase gates (terminal transitions, compression/encryption enable points).</summary>
    public static void Register(
        ProtocolDescriptorBuilder builder,
        ProtocolPhase phase,
        PacketFlow flow,
        int wireId,
        string identifier)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identifier);

        var id = Identifier.Parse(identifier);
        MarkerDeclaration? declared = null;
        bool bound = false;
        if (PacketBindings.Table.TryGetValue((phase, flow, id), out TimelineBinding binding)
            && binding.Covers(builder.Protocol))
        {
            if (binding.Timeline.TryResolve(builder.Protocol, out TimelineBind bind, out declared))
            {
                // The dataset's own identifier travels with the bind so the built descriptor records which key resolved it. Without that, a legacy alias renders under the canonical name and an alias that fired cannot be told from one that never did.
                bind(builder, wireId, id);
                VerifyResolvedSlot(builder.LastRegistered, phase, flow, id);
                bound = true;
            }
        }
        else if (PacketBindings.Absences.TryGetValue((phase, flow, id), out PacketTimeline? absence))
            absence.TryResolve(builder.Protocol, out _, out declared);

        if (!bound)
            builder.RegisterMarker(wireId, new MarkerPacketType(phase, flow, id), declared);

        ProtocolGates.Apply(builder, phase, flow, wireId, id);
    }

    /// <summary>Checks the slot a timeline just filled against the dataset row that asked for it. The row names the phase and flow the descriptor was walking; the slot is filed under the bound type's own. They can only diverge if a timeline step carries a type from elsewhere, and the packet would then be missing from the registry the frame path reads rather than wrong in it.</summary>
    internal static void VerifyResolvedSlot(BoundPacketCodec? slot, ProtocolPhase phase, PacketFlow flow, Identifier id)
    {
        if (slot is not null && slot.Type.Phase == phase && slot.Type.Flow == flow)
            return;

        throw new InvalidOperationException(
            slot is null
                ? $"({phase}, {flow}, {id}) resolved a timeline that registered nothing."
                : $"({phase}, {flow}, {id}) resolved {slot.Type.Id}, which is filed under ({slot.Type.Phase}, {slot.Type.Flow}).");
    }
}
