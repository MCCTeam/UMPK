using Umpk.BedrockDataGen.Schema;

namespace Umpk.BedrockDataGen;

internal static class JoinToleranceValidator
{
    private const string ProfileId = "bedrock-1.26.45-2169";

    public static JoinToleratedPacket[] Validate(
        JoinToleranceManifest manifest,
        IReadOnlyDictionary<int, string> protocolIds,
        IReadOnlyCollection<RequiredPacket> required)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(protocolIds);
        ArgumentNullException.ThrowIfNull(required);
        if (!manifest.Profile.Equals(ProfileId, StringComparison.Ordinal))
            throw new SchemaException("The join tolerance manifest does not target the explicit Bedrock 1.26.45 protocol 2169 profile.");
        if (string.IsNullOrWhiteSpace(manifest.Evidence))
            throw new SchemaException("The join tolerance manifest has no evidence label.");
        if (manifest.Packets.Length == 0)
            throw new SchemaException("The join tolerance manifest must be nonempty.");

        var bound = new HashSet<int>();
        foreach (RequiredPacket packet in required)
        {
            if (packet.Binding is "verified" or "blocked")
                bound.Add(packet.Id);
        }

        var seenIds = new HashSet<int>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        int previousId = -1;
        foreach (JoinToleratedPacket packet in manifest.Packets)
        {
            if (packet.Id <= 0)
                throw new SchemaException($"The join tolerance entry {packet.Id} '{packet.Name}' is not a positive packet ID.");
            if (packet.Id <= previousId)
                throw new SchemaException("The join tolerance manifest must order packet IDs ascending with no duplicates.");
            previousId = packet.Id;
            if (!seenIds.Add(packet.Id))
                throw new SchemaException($"The join tolerance manifest contains duplicate packet ID {packet.Id}.");
            if (string.IsNullOrWhiteSpace(packet.Name) || !seenNames.Add(packet.Name))
                throw new SchemaException($"The join tolerance entry {packet.Id} has a missing or duplicate name.");
            if (!protocolIds.TryGetValue(packet.Id, out string? protocolName) || !protocolName.Equals(packet.Name, StringComparison.Ordinal))
                throw new SchemaException($"The join tolerance entry {packet.Id} '{packet.Name}' does not match the primary packet map.");
            if (bound.Contains(packet.Id))
                throw new SchemaException($"The join tolerance entry {packet.Id} '{packet.Name}' collides with a bound codec and must be decoded, not tolerated.");
        }

        return [.. manifest.Packets.OrderBy(static packet => packet.Id)];
    }
}
