using System.Text;
using System.Text.Json;
using Umpk.BedrockDataGen.Schema;

namespace Umpk.BedrockDataGen;

internal sealed record ImportSource(
    string Key,
    string SourcePath,
    string ImportedPath,
    string Sha256,
    string Role);

internal sealed record ImportSpecification(
    string Profile,
    string SourceRevision,
    string SourceLicense,
    string DataPathsSha256,
    ImportSource[] Sources);

internal static class DataPathResolver
{
    public static SortedDictionary<string, string> Resolve(JsonElement dataPaths, string release, IReadOnlyList<string> keys)
    {
        if (!dataPaths.TryGetProperty("bedrock", out JsonElement bedrock) ||
            !bedrock.TryGetProperty(release, out JsonElement version) ||
            version.ValueKind != JsonValueKind.Object)
        {
            throw new SchemaException($"dataPaths has no exact Bedrock release '{release}'.");
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (!version.TryGetProperty(key, out JsonElement path) || path.ValueKind != JsonValueKind.String)
                throw new SchemaException($"dataPaths release '{release}' has no exact '{key}' entry.");
            result.Add(key, path.GetString()!);
        }
        return result;
    }
}

internal static class Importer
{
    private static readonly (string Key, string Extension, string Role)[] s_inputs =
    [
        ("protocol", ".json", "primary-packet-schema"),
        ("version", ".json", "release-identity"),
        ("proto", ".yml", "annotated-packet-evidence"),
        ("types", ".yml", "annotated-type-evidence"),
        ("items", ".json", "resolved-category-input"),
        ("blocks", ".json", "resolved-category-input"),
        ("blockStates", ".json", "resolved-category-input"),
        ("blockCollisionShapes", ".json", "resolved-category-input"),
        ("entities", ".json", "resolved-category-input"),
        ("biomes", ".json", "resolved-category-input"),
        ("recipes", ".json", "resolved-category-input"),
        ("windows", ".json", "resolved-category-input"),
        ("steve", ".json", "resolved-category-input"),
    ];

    public static ImportSpecification CreateSpecification(string sourceRoot, string dataRoot)
    {
        string dataPathsPath = Path.Combine(sourceRoot, "data", "dataPaths.json");
        string sourceManifestPath = Path.Combine(dataRoot, "source-manifest.json");
        using JsonDocument dataPaths = JsonDocument.Parse(File.ReadAllBytes(dataPathsPath));
        using JsonDocument sourceManifest = JsonDocument.Parse(File.ReadAllBytes(sourceManifestPath));
        JsonElement target = sourceManifest.RootElement.GetProperty("target");
        string dataset = target.GetProperty("dataset").GetString() ?? throw new SchemaException("Source manifest target dataset is missing.");
        string profile = target.GetProperty("profile").GetString() ?? throw new SchemaException("Source manifest target profile is missing.");
        JsonElement minecraftData = sourceManifest.RootElement.GetProperty("sources").EnumerateArray()
            .Single(source => source.GetProperty("name").GetString()!.Equals("minecraft-data", StringComparison.Ordinal));
        string revision = minecraftData.GetProperty("revision").GetString() ?? throw new SchemaException("minecraft-data revision is missing.");
        string sourceLicense = minecraftData.GetProperty("license").GetString() ?? throw new SchemaException("minecraft-data license boundary is missing.");
        JsonElement pinnedFiles = minecraftData.GetProperty("files");
        string dataPathsRelative = "data/dataPaths.json";
        string dataPathsHash = RequirePinnedHash(pinnedFiles, dataPathsRelative);
        string actualDataPathsHash = SchemaHash.ComputeFile(dataPathsPath);
        if (!actualDataPathsHash.Equals(dataPathsHash, StringComparison.OrdinalIgnoreCase))
            throw new SchemaException("dataPaths hash does not match source-manifest.json.");

        string[] keys = s_inputs.Select(static input => input.Key).ToArray();
        SortedDictionary<string, string> resolved = DataPathResolver.Resolve(dataPaths.RootElement, dataset, keys);
        var sources = new List<ImportSource>(s_inputs.Length + 1)
        {
            new("dataPaths", dataPathsRelative, "dataPaths.json", dataPathsHash, "resolution-map"),
        };
        foreach ((string key, string extension, string role) in s_inputs)
        {
            string sourcePath = $"data/{resolved[key]}/{key}{extension}";
            sources.Add(new ImportSource(key, sourcePath, $"{key}{extension}", RequirePinnedHash(pinnedFiles, sourcePath), role));
        }
        return new ImportSpecification(profile, revision, sourceLicense, dataPathsHash, [.. sources]);
    }

    public static ImportManifest Import(string sourceRoot, string destinationPath, ImportSpecification specification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(specification);
        string fullSourceRoot = Path.GetFullPath(sourceRoot);
        var verified = new List<(ImportSource Source, string FullPath)>();
        foreach (ImportSource source in specification.Sources.OrderBy(static source => source.Key, StringComparer.Ordinal))
        {
            string fullPath = Path.GetFullPath(Path.Combine(fullSourceRoot, source.SourcePath));
            if (!fullPath.StartsWith(fullSourceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new SchemaException($"Import source '{source.SourcePath}' escapes the source root.");
            if (!File.Exists(fullPath))
                throw new SchemaException($"Import source '{source.SourcePath}' does not exist.");
            string actualHash = SchemaHash.ComputeFile(fullPath);
            if (!actualHash.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new SchemaException($"Import source '{source.SourcePath}' hash does not match its pin.");
            verified.Add((source, fullPath));
        }

        string temporaryPath = $"{destinationPath}.importing-{Guid.NewGuid():N}";
        try
        {
            string importedRoot = Path.Combine(temporaryPath, "imported");
            Directory.CreateDirectory(importedRoot);
            var imported = new List<ImportedInput>();
            foreach ((ImportSource source, string fullPath) in verified)
            {
                string destination = Path.Combine(importedRoot, source.ImportedPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(fullPath, destination, overwrite: false);
                imported.Add(new ImportedInput(source.Key, source.SourcePath, $"imported/{source.ImportedPath}", source.Sha256, source.Role));
            }
            var manifest = new ImportManifest(
                "bedrock-import-manifest/1",
                specification.Profile,
                specification.SourceRevision,
                specification.SourceLicense,
                specification.DataPathsSha256,
                [.. imported.OrderBy(static input => input.Key, StringComparer.Ordinal)]);
            WriteJson(Path.Combine(temporaryPath, "import-manifest.json"), manifest, BedrockDataJsonContext.Default.ImportManifest);
            if (Directory.Exists(destinationPath))
                Directory.Delete(destinationPath, recursive: true);
            Directory.Move(temporaryPath, destinationPath);
            return manifest;
        }
        finally
        {
            if (Directory.Exists(temporaryPath))
                Directory.Delete(temporaryPath, recursive: true);
        }
    }

    internal static void WriteJson<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(value, typeInfo).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string RequirePinnedHash(JsonElement files, string path)
    {
        if (!files.TryGetProperty(path, out JsonElement hash) || hash.ValueKind != JsonValueKind.String)
            throw new SchemaException($"Source manifest has no hash pin for '{path}'.");
        return hash.GetString()!;
    }
}
