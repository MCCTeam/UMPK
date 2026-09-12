namespace Umpk.Protocol.Java;

/// <summary>A written-down decision not to bind a packet from a protocol onward: which class of deliberate it is and why, carried by the timeline step and recorded on the built descriptor.</summary>
/// <remarks>A marker with no declaration is not an error, it is the backlog: the great majority of markers are absences, an identifier the dataset registers and no binding file mentions at all. The difference between the two is the whole point of declaring one, because an absence and a forgotten registration line look identical from every other angle.</remarks>
/// <param name="FromProtocol">The first protocol the declaration governs.</param>
/// <param name="Reason">Which class of deliberate this is.</param>
/// <param name="Why">The prose justification, held to the same bar as the allowlist entry's.</param>
internal sealed record MarkerDeclaration(int FromProtocol, MarkerReason Reason, string Why);
