using Umpk.BedrockDataGen.Schema;

namespace Umpk.BedrockDataGen;

internal static class OverlayProcessor
{
    public static void Apply(SchemaIrDocument schema, IReadOnlyList<SchemaOverlay> overlays, string protocolSha256)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(overlays);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolSha256);

        SchemaOverlay[] ordered = [.. overlays.OrderBy(static item => item.Id, StringComparer.Ordinal)];
        foreach (SchemaOverlay overlay in ordered)
        {
            if (overlay.Profiles.Length != 1 || !overlay.Profiles[0].Equals(schema.Profile, StringComparison.Ordinal))
                throw new SchemaException($"Overlay '{overlay.Id}' must target exactly one profile matching '{schema.Profile}'.");
            if (!overlay.ProtocolSha256.Equals(protocolSha256, StringComparison.OrdinalIgnoreCase))
                throw new SchemaException($"Overlay '{overlay.Id}' protocol hash precondition failed.");
            if (!schema.Types.TryGetValue(overlay.TypeName, out SchemaNode? root))
                throw new SchemaException($"Overlay '{overlay.Id}' targets unknown type '{overlay.TypeName}'.");

            SchemaNode target = FindField(root, overlay.FieldPath, overlay.Id);
            string actualHash = SchemaHash.Compute(target);
            if (!actualHash.Equals(overlay.ExpectedNodeSha256, StringComparison.OrdinalIgnoreCase))
                throw new SchemaException($"Overlay '{overlay.Id}' node hash precondition failed; actual hash is {actualHash}.");
            if (overlay.Status is not ("candidate" or "verified"))
                throw new SchemaException($"Overlay '{overlay.Id}' has unsupported status '{overlay.Status}'.");
        }

        foreach (SchemaOverlay overlay in ordered.Where(static item => item.Status.Equals("verified", StringComparison.Ordinal)))
        {
            SchemaNode root = schema.Types[overlay.TypeName];
            SchemaNode target = FindField(root, overlay.FieldPath, overlay.Id);
            if (!overlay.Operation.Equals("replace-reference", StringComparison.Ordinal))
                throw new SchemaException($"Overlay '{overlay.Id}' has unsupported operation '{overlay.Operation}'.");
            if (target.Kind != SchemaNodeKind.Reference)
                throw new SchemaException($"Overlay '{overlay.Id}' replacement requires a reference node.");
            if (overlay.MaxBytes is null or < 19 or > 64)
                throw new SchemaException($"Overlay '{overlay.Id}' large bitset budget must be between 19 and 64 bytes.");
            if (overlay.FixedBitCount != 131)
                throw new SchemaException($"Overlay '{overlay.Id}' large bitset must preserve the 131-bit profile width.");

            ReplaceField(root, overlay.FieldPath, new SchemaNode
            {
                Kind = SchemaNodeKind.LargeBitSet,
                TypeName = "bounded-native-large-bitset",
                MaxBytes = overlay.MaxBytes,
                FixedBitCount = overlay.FixedBitCount,
            }, overlay.Id);
        }
    }

    private static SchemaNode FindField(SchemaNode root, string fieldPath, string overlayId)
    {
        SchemaNode current = root;
        foreach (string segment in SplitPath(fieldPath, overlayId))
        {
            SchemaField? field = current.Fields.FirstOrDefault(item => item.Name.Equals(segment, StringComparison.Ordinal));
            current = field?.Node ?? throw new SchemaException($"Overlay '{overlayId}' cannot find field path '{fieldPath}'.");
        }
        return current;
    }

    private static void ReplaceField(SchemaNode root, string fieldPath, SchemaNode replacement, string overlayId)
    {
        string[] segments = SplitPath(fieldPath, overlayId);
        SchemaNode current = root;
        for (int index = 0; index < segments.Length - 1; index++)
        {
            SchemaField? field = current.Fields.FirstOrDefault(item => item.Name.Equals(segments[index], StringComparison.Ordinal));
            current = field?.Node ?? throw new SchemaException($"Overlay '{overlayId}' cannot find field path '{fieldPath}'.");
        }
        int targetIndex = Array.FindIndex(current.Fields, item => item.Name.Equals(segments[^1], StringComparison.Ordinal));
        if (targetIndex < 0)
            throw new SchemaException($"Overlay '{overlayId}' cannot find field path '{fieldPath}'.");
        SchemaField target = current.Fields[targetIndex];
        current.Fields[targetIndex] = target with { Node = replacement };
    }

    private static string[] SplitPath(string fieldPath, string overlayId)
    {
        string[] segments = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            throw new SchemaException($"Overlay '{overlayId}' field path must not be empty.");
        return segments;
    }
}
