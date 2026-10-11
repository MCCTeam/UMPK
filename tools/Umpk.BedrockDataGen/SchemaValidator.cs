using System.Globalization;
using System.Text.Json;
using Umpk.BedrockDataGen.Schema;

namespace Umpk.BedrockDataGen;

internal static class SchemaValidator
{
    private static readonly HashSet<string> s_knownNativeTypes = new(StringComparer.Ordinal)
    {
        "MapInfo", "bitflags", "byterot", "encapsulated", "enum_size_based_on_values_len",
        "lnbt", "maybeIncompleteArray", "nbt", "nbtLoop", "optionalOnRemaining", "restBuffer",
        "uuid", "varint64", "zigzag32", "zigzag64",
    };

    public static void Validate(SchemaIrDocument schema, IReadOnlyList<RequiredPacket> requiredPackets)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(requiredPackets);
        if (requiredPackets.Count == 0)
            throw new SchemaException("Required packet manifest must be nonempty.");

        var ids = new HashSet<int>();
        foreach (RequiredPacket packet in requiredPackets)
        {
            if (!ids.Add(packet.Id))
                throw new SchemaException($"Required packet manifest contains duplicate packet ID {packet.Id}.");
            ConcreteCodecBindings.Validate(packet);
            if (packet.Direction.Equals("unresolved", StringComparison.Ordinal) && packet.SchemaVerified)
                throw new SchemaException($"Packet '{packet.Name}' cannot bind while its direction is unresolved.");
            string rootName = $"packet_{packet.Name}";
            if (!schema.Types.ContainsKey(rootName))
                throw new SchemaException($"Required packet '{packet.Name}' has no schema type '{rootName}'.");
            ValidateClosure(schema, rootName);
        }
    }

    private static void ValidateClosure(SchemaIrDocument schema, string rootName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(rootName);
        while (pending.TryPop(out string? typeName))
        {
            if (!visited.Add(typeName))
                continue;
            if (!schema.Types.TryGetValue(typeName, out SchemaNode? node))
                throw new SchemaException($"Required packet closure references unresolved type '{typeName}'.");
            CollectReferences(node, pending);
        }
    }

    private static void CollectReferences(SchemaNode node, Stack<string> pending)
    {
        if (node.Kind == SchemaNodeKind.Native && (node.TypeName is null || !s_knownNativeTypes.Contains(node.TypeName)))
            throw new SchemaException($"Required packet closure contains unknown native type '{node.TypeName ?? "<null>"}'.");
        if (node.Kind == SchemaNodeKind.LargeBitSet && (node.MaxBytes is null or < 19 or > 64 || node.FixedBitCount != 131))
            throw new SchemaException("Required packet closure contains an invalid bounded large bitset.");
        if (node.Kind == SchemaNodeKind.Reference)
        {
            if (string.IsNullOrWhiteSpace(node.TypeName))
                throw new SchemaException("Schema reference has no type name.");
            pending.Push(node.TypeName);
        }

        if (node.Element is not null)
            CollectReferences(node.Element, pending);
        foreach (SchemaField field in node.Fields)
            CollectReferences(field.Node, pending);
        foreach (SchemaNode item in node.Cases.Values)
            CollectReferences(item, pending);
        if (node.Default is not null)
            CollectReferences(node.Default, pending);
    }
}

internal static class RequiredPacketBuilder
{
    public static SortedDictionary<int, string> ReadPacketIds(JsonElement protocolRoot)
    {
        JsonElement mappings = protocolRoot
            .GetProperty("types")
            .GetProperty("mcpe_packet")[1][0]
            .GetProperty("type")[1]
            .GetProperty("mappings");
        var packetIds = new SortedDictionary<int, string>();
        foreach (JsonProperty mapping in mappings.EnumerateObject())
        {
            if (!int.TryParse(mapping.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                throw new SchemaException($"Packet ID '{mapping.Name}' is not an unsigned decimal integer.");
            string name = mapping.Value.GetString() ?? throw new SchemaException($"Packet ID {id} has no name.");
            if (!packetIds.TryAdd(id, name))
                throw new SchemaException($"Packet map contains duplicate packet ID {id}.");
        }

        return packetIds;
    }

    public static RequiredPacket[] Build(JsonElement protocolRoot, JsonElement supportRoot, SchemaIrDocument schema)
    {
        SortedDictionary<int, string> allIds = ReadPacketIds(protocolRoot);
        Dictionary<string, int> idsByName = allIds.ToDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);
        JsonElement seeds = supportRoot.GetProperty("seedPackets");
        var result = new List<RequiredPacket>();
        foreach (JsonElement seed in seeds.EnumerateArray())
        {
            string name = seed.GetProperty("name").GetString() ?? throw new SchemaException("Seed packet name must not be null.");
            if (!idsByName.TryGetValue(name, out int id))
                throw new SchemaException($"Required packet '{name}' is missing from the primary packet map.");
            string direction = seed.GetProperty("direction").GetString() ?? throw new SchemaException($"Packet '{name}' has no direction.");
            if (direction is not ("serverbound" or "clientbound" or "bidirectional" or "unresolved"))
                throw new SchemaException($"Packet '{name}' has unsupported direction '{direction}'.");
            string support = seed.GetProperty("status").GetString() ?? throw new SchemaException($"Packet '{name}' has no status.");
            string owner = seed.GetProperty("owner").GetString() ?? throw new SchemaException($"Packet '{name}' has no owner.");
            string binding = ReadOptionalString(seed, "binding") ?? "unselected";
            string? codec = ReadOptionalString(seed, "codec");
            string capability = ReadOptionalString(seed, "capability") ?? "none";
            string registryGeneration = ReadOptionalString(seed, "registryGeneration") ?? "none";
            string? blockReason = ReadOptionalString(seed, "blockReason");
            string evidence = ReadOptionalString(seed, "evidence") ?? name switch
            {
                "move_player" => "candidate-unverified",
                "client_movement_prediction_sync" => "direction-unverified",
                _ => "primary-schema",
            };
            string rootName = $"packet_{name}";
            string layoutHash = schema.Types.TryGetValue(rootName, out SchemaNode? node) ? SchemaHash.Compute(node) : string.Empty;
            // hand-vectors-triple-source binds layout from exact handwritten round-trip vectors plus three-source agreement (D-036). It verifies the layout binding only; independent live-byte acceptance stays open and is tracked in the decision log, not in this flag.
            bool schemaVerified = evidence is "primary-schema" or "live-bytes-2169" or "hand-vectors-triple-source" && !direction.Equals("unresolved", StringComparison.Ordinal);
            result.Add(new RequiredPacket(name, id, direction, support, evidence, layoutHash, schemaVerified, owner, binding, codec, capability, registryGeneration, blockReason));
        }

        if (result.Count == 0)
            throw new SchemaException("Required packet manifest must be nonempty.");
        return [.. result.OrderBy(static packet => packet.Id)];
    }

    private static string? ReadOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new SchemaException($"Property '{name}' must be a string.");
        return value.GetString();
    }
}
