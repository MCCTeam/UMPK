using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Umpk.BedrockDataGen.Schema;

internal static class SchemaHash
{
    public static string Compute(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        string json = JsonSerializer.Serialize(node, BedrockDataJsonContext.Default.SchemaNode);
        return Compute(Encoding.UTF8.GetBytes(json));
    }

    public static string ComputeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string Compute(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
