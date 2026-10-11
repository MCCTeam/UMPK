using System.Text.Json.Serialization;

namespace Umpk.BedrockDataGen.Schema;

internal enum SchemaNodeKind
{
    Primitive,
    Reference,
    Native,
    Container,
    Array,
    IncompleteArray,
    Mapper,
    Switch,
    Option,
    OptionalOnRemaining,
    Flags,
    BitField,
    String,
    Buffer,
    Encapsulated,
    LargeBitSet,
}

internal sealed record SchemaField(string Name, bool Anonymous, SchemaNode Node);

internal sealed record SchemaNode
{
    public required SchemaNodeKind Kind { get; init; }

    public string? TypeName { get; init; }

    public SchemaField[] Fields { get; init; } = [];

    public SchemaNode? Element { get; init; }

    public string? CountType { get; init; }

    public int? FixedCount { get; init; }

    public string? CountExpression { get; init; }

    public string? CompareTo { get; init; }

    public SortedDictionary<string, SchemaNode> Cases { get; init; } = new(StringComparer.Ordinal);

    public SchemaNode? Default { get; init; }

    public SortedDictionary<string, string> Mappings { get; init; } = new(StringComparer.Ordinal);

    public string[] Flags { get; init; } = [];

    public string? Encoding { get; init; }

    public bool Big { get; init; }

    public int? MaxBytes { get; init; }

    public int? FixedBitCount { get; init; }
}

internal sealed record SchemaIrDocument(
    string Profile,
    SortedDictionary<string, SchemaNode> Types);

internal sealed record RequiredPacket(
    string Name,
    int Id,
    string Direction,
    string Support,
    string Evidence,
    string LayoutHash,
    bool SchemaVerified,
    string Owner = "unassigned",
    string Binding = "unselected",
    string? Codec = null,
    string Capability = "none",
    string RegistryGeneration = "none",
    string? BlockReason = null);

internal sealed record ImportedInput(
    string Key,
    string SourcePath,
    string ImportedPath,
    string Sha256,
    string Role);

internal sealed record ImportManifest(
    string Schema,
    string Profile,
    string SourceRevision,
    string SourceLicense,
    string DataPathsSha256,
    ImportedInput[] Inputs);

internal sealed record GeneratedProfile(
    string Id,
    string Release,
    int ProtocolVersion,
    string Dialect,
    string DataProfile,
    string BehaviorProfile,
    int PredictionFlagLogicalBits,
    int PredictionFlagEncodedBytes,
    int PredictionFlagMaxBytes,
    string ImportManifestSha256,
    string SchemaSha256,
    ImportedInput[] Inputs,
    ProfileEvidence[] Evidence);

internal sealed record ProfileEvidence(
    string Id,
    string Status,
    string Disposition,
    string[] Sources);

internal sealed record SchemaDiff(string[] AddedTypes, string[] RemovedTypes, string[] ChangedTypes);

internal sealed record JoinToleratedPacket(int Id, string Name);

internal sealed record JoinToleranceManifest(
    string Schema,
    string Profile,
    string Evidence,
    JoinToleratedPacket[] Packets);

internal sealed record SchemaOverlay(
    string Id,
    string[] Profiles,
    string ProtocolSha256,
    string TypeName,
    string FieldPath,
    string ExpectedNodeSha256,
    string Operation,
    string Status,
    int? MaxBytes,
    int? FixedBitCount,
    string[] Evidence);

internal sealed class SchemaException(string message) : Exception(message);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SchemaIrDocument))]
[JsonSerializable(typeof(SchemaNode))]
[JsonSerializable(typeof(RequiredPacket[]))]
[JsonSerializable(typeof(ImportManifest))]
[JsonSerializable(typeof(GeneratedProfile))]
[JsonSerializable(typeof(SchemaOverlay))]
[JsonSerializable(typeof(SchemaOverlay[]))]
[JsonSerializable(typeof(SchemaDiff))]
[JsonSerializable(typeof(JoinToleranceManifest))]
internal sealed partial class BedrockDataJsonContext : JsonSerializerContext;
