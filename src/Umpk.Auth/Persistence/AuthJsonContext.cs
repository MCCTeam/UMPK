using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umpk.Auth.Persistence;

/// <summary>The source-generated <see cref="JsonSerializerContext"/> that serializes exactly the library's own persisted types. No reflection-based JSON path is used; the default file store binds to this context and refuses unknown types. This keeps the store AOT-safe with zero runtime codegen.</summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PersistedSession))]
[JsonSerializable(typeof(PersistedCertificates))]
[JsonSerializable(typeof(PersistedRefreshToken))]
internal sealed partial class AuthJsonContext : JsonSerializerContext
{
}
