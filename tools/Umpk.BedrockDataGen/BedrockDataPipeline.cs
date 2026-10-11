using System.Text;
using System.Text.Json;
using Umpk.BedrockDataGen.Schema;

namespace Umpk.BedrockDataGen;

internal static class BedrockDataPipeline
{
    private const string ProfileId = "bedrock-1.26.45-2169";

    public static void Import(string sourceRoot, string dataRoot)
    {
        ImportSpecification specification = Importer.CreateSpecification(sourceRoot, dataRoot);
        Importer.Import(sourceRoot, Path.Combine(dataRoot, "2169"), specification);
    }

    public static void Normalize(string dataRoot)
    {
        string profileRoot = Path.Combine(dataRoot, "2169");
        ImportManifest import = ReadJson(Path.Combine(profileRoot, "import-manifest.json"), BedrockDataJsonContext.Default.ImportManifest);
        using JsonDocument protocol = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(profileRoot, "imported", "protocol.json")));
        using JsonDocument support = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dataRoot, "support-manifest.json")));
        SchemaIrDocument schema = BuildSchema(dataRoot, profileRoot, protocol.RootElement);
        RequiredPacket[] required = RequiredPacketBuilder.Build(protocol.RootElement, support.RootElement, schema);
        SchemaValidator.Validate(schema, required);

        string schemaPath = Path.Combine(profileRoot, "schema.ir.json");
        Importer.WriteJson(schemaPath, schema, BedrockDataJsonContext.Default.SchemaIrDocument);
        Importer.WriteJson(Path.Combine(profileRoot, "required-packets.json"), required, BedrockDataJsonContext.Default.RequiredPacketArray);
        var generatedProfile = new GeneratedProfile(
            ProfileId,
            "1.26.45",
            2169,
            "base-2168",
            "minecraft-data-1.26.45-resolved",
            "base-2169",
            131,
            19,
            32,
            SchemaHash.ComputeFile(Path.Combine(profileRoot, "import-manifest.json")),
            SchemaHash.ComputeFile(schemaPath),
            import.Inputs,
            [
                new ProfileEvidence("D01", "corroborated", "Select the base 2168 layout for protocol 2169 and exclude the hotfix-4 SetScore dialect.", ["minecraft-data protocol 2168/2169 byte identity", "PowerNukkitX Bedrock_v2168 reuse", "Geyser Bedrock_v2169 registration"]),
                new ProfileEvidence("D02", "corroborated", "Retain the base optional RemoveScore objective marker and exclude the hotfix-4 second marker.", ["Cloudburst base and hotfix-4 SetScore serializers", "minecraft-data packet_set_score"]),
                new ProfileEvidence("D06", "resolved-layout-open-live-bytes", "Bind MovePlayer and SetEntityMotion with exact handwritten-vector field order, boolean teleport prefix, and unsigned 64-bit actor ID widths. The width follows Cloudburst v2168/v2169 serializers, gophertunnel ActorRuntimeID marshals, and the beyond-Int32 identity requirement; minecraft-data declares 32-bit varints whose bytes are identical for observed small IDs. No live BDS 19/40 bytes have been captured yet because idle players emit neither, so independent live-byte acceptance stays open for the movement phases. Candidate overlays stay recorded for future high-ID refinement.", ["handwritten exact round-trip vectors", "gophertunnel move_player/set_actor_motion marshals", "minecraft-data packet_move_player", "Cloudburst MovePlayerSerializer_v2168"]),
                new ProfileEvidence("D08", "encoding-schema-verified", "Use a bounded native large bitset and keep gameplay meanings unresolved.", ["minecraft-data varint128 placeholder", "Cloudburst 131 known entity flags"]),
            ]);
        Importer.WriteJson(Path.Combine(profileRoot, "profile.json"), generatedProfile, BedrockDataJsonContext.Default.GeneratedProfile);
    }

    public static void Verify(string dataRoot)
    {
        string profileRoot = Path.Combine(dataRoot, "2169");
        ImportManifest import = ReadJson(Path.Combine(profileRoot, "import-manifest.json"), BedrockDataJsonContext.Default.ImportManifest);
        GeneratedProfile profile = ReadJson(Path.Combine(profileRoot, "profile.json"), BedrockDataJsonContext.Default.GeneratedProfile);
        SchemaIrDocument storedSchema = ReadJson(Path.Combine(profileRoot, "schema.ir.json"), BedrockDataJsonContext.Default.SchemaIrDocument);
        RequiredPacket[] storedRequired = ReadJson(Path.Combine(profileRoot, "required-packets.json"), BedrockDataJsonContext.Default.RequiredPacketArray);

        if (!profile.Id.Equals(ProfileId, StringComparison.Ordinal) || profile.ProtocolVersion != 2169 || !profile.Dialect.Equals("base-2168", StringComparison.Ordinal) || !profile.BehaviorProfile.Equals("base-2169", StringComparison.Ordinal) || profile.PredictionFlagLogicalBits != 131 || profile.PredictionFlagEncodedBytes != 19 || profile.PredictionFlagMaxBytes < 19)
            throw new SchemaException("Generated profile identity does not match explicit Bedrock 1.26.45 protocol 2169.");
        if (import.Inputs.Length == 0)
            throw new SchemaException("Import manifest must contain frozen inputs.");
        foreach (ImportedInput input in import.Inputs)
        {
            string path = Path.Combine(profileRoot, input.ImportedPath);
            if (!File.Exists(path) || !SchemaHash.ComputeFile(path).Equals(input.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new SchemaException($"Frozen input '{input.Key}' is missing or stale.");
        }
        if (!SchemaHash.ComputeFile(Path.Combine(profileRoot, "import-manifest.json")).Equals(profile.ImportManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new SchemaException("Generated profile references a stale import manifest.");
        if (!SchemaHash.ComputeFile(Path.Combine(profileRoot, "schema.ir.json")).Equals(profile.SchemaSha256, StringComparison.OrdinalIgnoreCase))
            throw new SchemaException("Generated profile references a stale normalized schema.");

        using JsonDocument protocol = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(profileRoot, "imported", "protocol.json")));
        using JsonDocument support = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dataRoot, "support-manifest.json")));
        SchemaIrDocument rebuiltSchema = BuildSchema(dataRoot, profileRoot, protocol.RootElement);
        RequiredPacket[] rebuiltRequired = RequiredPacketBuilder.Build(protocol.RootElement, support.RootElement, rebuiltSchema);
        string storedSchemaJson = JsonSerializer.Serialize(storedSchema, BedrockDataJsonContext.Default.SchemaIrDocument);
        string rebuiltSchemaJson = JsonSerializer.Serialize(rebuiltSchema, BedrockDataJsonContext.Default.SchemaIrDocument);
        if (!storedSchemaJson.Equals(rebuiltSchemaJson, StringComparison.Ordinal))
            throw new SchemaException("Normalized schema is stale relative to frozen inputs and overlays.");
        string storedRequiredJson = JsonSerializer.Serialize(storedRequired, BedrockDataJsonContext.Default.RequiredPacketArray);
        string rebuiltRequiredJson = JsonSerializer.Serialize(rebuiltRequired, BedrockDataJsonContext.Default.RequiredPacketArray);
        if (!storedRequiredJson.Equals(rebuiltRequiredJson, StringComparison.Ordinal))
            throw new SchemaException("Required packet manifest is stale relative to the normalized schema.");
        SchemaValidator.Validate(storedSchema, storedRequired);
        JoinToleranceValidator.Validate(
            ReadJson(Path.Combine(profileRoot, "join-tolerance.json"), BedrockDataJsonContext.Default.JoinToleranceManifest),
            RequiredPacketBuilder.ReadPacketIds(protocol.RootElement),
            storedRequired);
    }

    public static void Generate(string dataRoot, string protocolOutput, string dataOutput)
    {
        Verify(dataRoot);
        string profileRoot = Path.Combine(dataRoot, "2169");
        GeneratedProfile profile = ReadJson(Path.Combine(profileRoot, "profile.json"), BedrockDataJsonContext.Default.GeneratedProfile);
        RequiredPacket[] required = ReadJson(Path.Combine(profileRoot, "required-packets.json"), BedrockDataJsonContext.Default.RequiredPacketArray);
        Emitter.Emit(profile, required, protocolOutput, dataOutput);
        using JsonDocument protocol = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(profileRoot, "imported", "protocol.json")));
        JoinToleratedPacket[] tolerances = JoinToleranceValidator.Validate(
            ReadJson(Path.Combine(profileRoot, "join-tolerance.json"), BedrockDataJsonContext.Default.JoinToleranceManifest),
            RequiredPacketBuilder.ReadPacketIds(protocol.RootElement),
            required);
        Emitter.EmitJoinTolerance(tolerances, protocolOutput);
        BlockRegistryEmitter.EmitFromDataRoot(dataRoot, dataOutput);
    }

    public static void VerifyGenerated(string dataRoot, string protocolOutput, string dataOutput)
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"umpk-bedrock-verify-{Guid.NewGuid():N}");
        try
        {
            string expectedProtocol = Path.Combine(temporaryRoot, "protocol");
            string expectedData = Path.Combine(temporaryRoot, "data");
            Generate(dataRoot, expectedProtocol, expectedData);
            CompareTrees(expectedProtocol, protocolOutput);
            CompareTrees(expectedData, dataOutput);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    public static void GenerateSkin(string dataRoot, string skinOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(skinOutputPath);
        Verify(dataRoot);
        string stevePath = Path.Combine(dataRoot, "2169", "imported", "steve.json");
        if (!File.Exists(stevePath))
            throw new SchemaException("The frozen steve.json input is missing.");
        SkinEmitter.Emit(stevePath, skinOutputPath, SchemaHash.ComputeFile(stevePath));
    }

    public static void VerifySkinGenerated(string dataRoot, string skinOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skinOutputPath);
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"umpk-bedrock-skin-{Guid.NewGuid():N}.g.cs");
        try
        {
            GenerateSkin(dataRoot, temporaryPath);
            if (!File.Exists(skinOutputPath))
                throw new SchemaException($"Generated skin '{skinOutputPath}' does not exist.");
            if (!File.ReadAllBytes(temporaryPath).AsSpan().SequenceEqual(File.ReadAllBytes(skinOutputPath)))
                throw new SchemaException($"Generated skin '{skinOutputPath}' is stale.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static SchemaDiff Diff(string baseProtocolPath, string targetProtocolPath)
    {
        using JsonDocument baseProtocol = JsonDocument.Parse(File.ReadAllBytes(baseProtocolPath));
        using JsonDocument targetProtocol = JsonDocument.Parse(File.ReadAllBytes(targetProtocolPath));
        SchemaIrDocument baseSchema = SchemaNormalizer.Normalize(baseProtocol.RootElement, "base");
        SchemaIrDocument targetSchema = SchemaNormalizer.Normalize(targetProtocol.RootElement, "target");
        string[] added = targetSchema.Types.Keys.Except(baseSchema.Types.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] removed = baseSchema.Types.Keys.Except(targetSchema.Types.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] changed = baseSchema.Types.Keys.Intersect(targetSchema.Types.Keys, StringComparer.Ordinal)
            .Where(name => !SchemaHash.Compute(baseSchema.Types[name]).Equals(SchemaHash.Compute(targetSchema.Types[name]), StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new SchemaDiff(added, removed, changed);
    }

    private static SchemaIrDocument BuildSchema(string dataRoot, string profileRoot, JsonElement protocol)
    {
        SchemaIrDocument schema = SchemaNormalizer.Normalize(protocol, ProfileId);
        string protocolHash = SchemaHash.ComputeFile(Path.Combine(profileRoot, "imported", "protocol.json"));
        SchemaOverlay[] overlays = Directory.Exists(Path.Combine(dataRoot, "overlays"))
            ? Directory.GetFiles(Path.Combine(dataRoot, "overlays"), "*.json", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(path => ReadJson(path, BedrockDataJsonContext.Default.SchemaOverlay))
                .ToArray()
            : [];
        OverlayProcessor.Apply(schema, overlays, protocolHash);
        return schema;
    }

    private static T ReadJson<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.Deserialize(File.ReadAllBytes(path), typeInfo)
            ?? throw new SchemaException($"'{path}' did not contain a JSON value.");
    }

    private static void CompareTrees(string expectedRoot, string actualRoot)
    {
        if (!Directory.Exists(actualRoot))
            throw new SchemaException($"Generated output '{actualRoot}' does not exist.");
        string[] expectedFiles = Directory.GetFiles(expectedRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(expectedRoot, path)).Order(StringComparer.Ordinal).ToArray();
        string[] actualFiles = Directory.GetFiles(actualRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(actualRoot, path)).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles, StringComparer.Ordinal))
            throw new SchemaException($"Generated output '{actualRoot}' contains missing or extra files.");
        foreach (string relativePath in expectedFiles)
        {
            if (!File.ReadAllBytes(Path.Combine(expectedRoot, relativePath)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(actualRoot, relativePath))))
                throw new SchemaException($"Generated file '{relativePath}' is stale.");
        }
    }
}
