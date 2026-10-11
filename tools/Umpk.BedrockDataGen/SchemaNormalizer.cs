using System.Text.Json;
using Umpk.BedrockDataGen.Schema;

namespace Umpk.BedrockDataGen;

internal static class SchemaNormalizer
{
    private static readonly HashSet<string> s_primitives = new(StringComparer.Ordinal)
    {
        "bool", "i8", "u8", "i16", "u16", "i32", "u32", "i64", "u64",
        "li8", "lu8", "li16", "lu16", "li32", "lu32", "li64", "lu64",
        "lf32", "lf64", "varint", "varlong", "void",
    };

    public static SchemaIrDocument Normalize(JsonElement root, string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        if (!root.TryGetProperty("types", out JsonElement types) || types.ValueKind != JsonValueKind.Object)
            throw new SchemaException("Protocol schema must contain a types object.");

        string[] names = types.EnumerateObject().Select(static property => property.Name).ToArray();
        var definedTypes = new HashSet<string>(names, StringComparer.Ordinal);
        var normalized = new SortedDictionary<string, SchemaNode>(StringComparer.Ordinal);
        foreach (JsonProperty type in types.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
            normalized.Add(type.Name, NormalizeNode(type.Value, $"types.{type.Name}", definedTypes, topLevel: true));

        return new SchemaIrDocument(profile, normalized);
    }

    private static SchemaNode NormalizeNode(JsonElement value, string path, HashSet<string> definedTypes, bool topLevel = false)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            string name = value.GetString() ?? throw new SchemaException($"{path} contains a null type name.");
            if (topLevel && name.Equals("native", StringComparison.Ordinal))
                return new SchemaNode { Kind = SchemaNodeKind.Native, TypeName = path[(path.LastIndexOf('.') + 1)..] };
            if (definedTypes.Contains(name))
                return new SchemaNode { Kind = SchemaNodeKind.Reference, TypeName = name };
            if (s_primitives.Contains(name))
                return new SchemaNode { Kind = SchemaNodeKind.Primitive, TypeName = name };
            return new SchemaNode { Kind = SchemaNodeKind.Reference, TypeName = name };
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
            throw new SchemaException($"{path} must be a type name or a two-element construct.");

        string construct = value[0].GetString() ?? throw new SchemaException($"{path} has no construct name.");
        JsonElement options = value[1];
        return construct switch
        {
            "container" => NormalizeContainer(options, path, definedTypes),
            "array" => NormalizeArray(options, path, definedTypes, incomplete: false),
            "maybeIncompleteArray" => NormalizeArray(options, path, definedTypes, incomplete: true),
            "mapper" => NormalizeMapper(options, path, definedTypes),
            "switch" => NormalizeSwitch(options, path, definedTypes),
            "option" => new SchemaNode
            {
                Kind = SchemaNodeKind.Option,
                Element = NormalizeNode(options, $"{path}.option", definedTypes),
            },
            "optionalOnRemaining" => NormalizeOptionalOnRemaining(options, path, definedTypes),
            "bitflags" => NormalizeFlags(options, path, definedTypes),
            "bitfield" => NormalizeBitField(options, path),
            "pstring" => NormalizeLengthPrefixed(options, path, SchemaNodeKind.String),
            "buffer" => NormalizeLengthPrefixed(options, path, SchemaNodeKind.Buffer),
            "encapsulated" => NormalizeEncapsulated(options, path, definedTypes),
            _ => throw new SchemaException($"{path} uses unsupported construct '{construct}'."),
        };
    }

    private static SchemaNode NormalizeContainer(JsonElement options, string path, HashSet<string> definedTypes)
    {
        if (options.ValueKind != JsonValueKind.Array)
            throw new SchemaException($"{path} container fields must be an array.");

        var fields = new List<SchemaField>();
        int index = 0;
        foreach (JsonElement field in options.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object || !field.TryGetProperty("type", out JsonElement fieldType))
                throw new SchemaException($"{path}.fields[{index}] must contain a type.");
            bool anonymous = field.TryGetProperty("anon", out JsonElement anon) && anon.ValueKind == JsonValueKind.True;
            string name = field.TryGetProperty("name", out JsonElement fieldName)
                ? fieldName.GetString() ?? string.Empty
                : anonymous ? $"anonymous_{index}" : throw new SchemaException($"{path}.fields[{index}] must contain a name.");
            fields.Add(new SchemaField(name, anonymous, NormalizeNode(fieldType, $"{path}.{name}", definedTypes)));
            index++;
        }

        return new SchemaNode { Kind = SchemaNodeKind.Container, Fields = [.. fields] };
    }

    private static SchemaNode NormalizeArray(JsonElement options, string path, HashSet<string> definedTypes, bool incomplete)
    {
        RequireObject(options, path);
        string? countType = options.TryGetProperty("countType", out JsonElement countTypeValue) && countTypeValue.ValueKind == JsonValueKind.String
            ? countTypeValue.GetString()
            : null;
        bool hasCount = options.TryGetProperty("count", out JsonElement countValue);
        int? fixedCount = hasCount && countValue.ValueKind == JsonValueKind.Number && countValue.TryGetInt32(out int count) ? count : null;
        string? countExpression = hasCount && countValue.ValueKind == JsonValueKind.String ? countValue.GetString() : null;
        if (countType is null && fixedCount is null && countExpression is null)
            throw new SchemaException($"{path} array must contain countType or count.");
        if (!options.TryGetProperty("type", out JsonElement element))
            throw new SchemaException($"{path} array must contain type.");
        return new SchemaNode
        {
            Kind = incomplete ? SchemaNodeKind.IncompleteArray : SchemaNodeKind.Array,
            CountType = countType,
            FixedCount = fixedCount,
            CountExpression = countExpression,
            Element = NormalizeNode(element, $"{path}.element", definedTypes),
        };
    }

    private static SchemaNode NormalizeMapper(JsonElement options, string path, HashSet<string> definedTypes)
    {
        RequireObject(options, path);
        if (!options.TryGetProperty("type", out JsonElement underlying))
            throw new SchemaException($"{path} mapper must contain type.");
        if (!options.TryGetProperty("mappings", out JsonElement mappings) || mappings.ValueKind != JsonValueKind.Object)
            throw new SchemaException($"{path} mapper must contain mappings.");
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty mapping in mappings.EnumerateObject())
            values.Add(mapping.Name, mapping.Value.GetString() ?? throw new SchemaException($"{path} mapping '{mapping.Name}' must be a string."));
        return new SchemaNode
        {
            Kind = SchemaNodeKind.Mapper,
            Element = NormalizeNode(underlying, $"{path}.underlying", definedTypes),
            Mappings = values,
        };
    }

    private static SchemaNode NormalizeSwitch(JsonElement options, string path, HashSet<string> definedTypes)
    {
        RequireObject(options, path);
        string compareTo = RequireString(options, "compareTo", path);
        if (!options.TryGetProperty("fields", out JsonElement fields) || fields.ValueKind != JsonValueKind.Object)
            throw new SchemaException($"{path} switch must contain fields.");
        var cases = new SortedDictionary<string, SchemaNode>(StringComparer.Ordinal);
        foreach (JsonProperty item in fields.EnumerateObject())
            cases.Add(item.Name, NormalizeNode(item.Value, $"{path}.cases.{item.Name}", definedTypes));
        SchemaNode? defaultNode = options.TryGetProperty("default", out JsonElement defaultValue)
            ? NormalizeNode(defaultValue, $"{path}.default", definedTypes)
            : null;
        return new SchemaNode
        {
            Kind = SchemaNodeKind.Switch,
            CompareTo = compareTo,
            Cases = cases,
            Default = defaultNode,
        };
    }

    private static SchemaNode NormalizeFlags(JsonElement options, string path, HashSet<string> definedTypes)
    {
        RequireObject(options, path);
        if (!options.TryGetProperty("type", out JsonElement underlying))
            throw new SchemaException($"{path} bitflags must contain type.");
        if (!options.TryGetProperty("flags", out JsonElement flags) || flags.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
            throw new SchemaException($"{path} bitflags must contain flags.");
        string[] names = flags.ValueKind == JsonValueKind.Array
            ? flags.EnumerateArray().Select(flag => flag.GetString() ?? throw new SchemaException($"{path} flag must be a string.")).ToArray()
            : flags.EnumerateObject().Select(static flag => flag.Name).Order(StringComparer.Ordinal).ToArray();
        var mappings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (flags.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty flag in flags.EnumerateObject())
                mappings.Add(flag.Name, flag.Value.GetRawText());
        }
        return new SchemaNode
        {
            Kind = SchemaNodeKind.Flags,
            Element = NormalizeNode(underlying, $"{path}.underlying", definedTypes),
            Flags = names,
            Mappings = mappings,
            Big = options.TryGetProperty("big", out JsonElement big) && big.ValueKind == JsonValueKind.True,
        };
    }

    private static SchemaNode NormalizeOptionalOnRemaining(JsonElement options, string path, HashSet<string> definedTypes)
    {
        RequireObject(options, path);
        if (!options.TryGetProperty("type", out JsonElement element))
            throw new SchemaException($"{path} optional-on-remaining type must contain type.");
        return new SchemaNode
        {
            Kind = SchemaNodeKind.OptionalOnRemaining,
            Element = NormalizeNode(element, $"{path}.element", definedTypes),
        };
    }

    private static SchemaNode NormalizeBitField(JsonElement options, string path)
    {
        if (options.ValueKind != JsonValueKind.Array)
            throw new SchemaException($"{path} bitfield entries must be an array.");
        string[] entries = options.EnumerateArray()
            .Select((entry, index) => entry.TryGetProperty("name", out JsonElement name)
                ? name.GetString() ?? $"field_{index}"
                : $"field_{index}")
            .ToArray();
        return new SchemaNode { Kind = SchemaNodeKind.BitField, Flags = entries };
    }

    private static SchemaNode NormalizeLengthPrefixed(JsonElement options, string path, SchemaNodeKind kind)
    {
        RequireObject(options, path);
        string? countType = options.TryGetProperty("countType", out JsonElement countTypeValue) && countTypeValue.ValueKind == JsonValueKind.String
            ? countTypeValue.GetString()
            : null;
        bool hasCount = options.TryGetProperty("count", out JsonElement countValue);
        int? fixedCount = hasCount && countValue.ValueKind == JsonValueKind.Number && countValue.TryGetInt32(out int count) ? count : null;
        string? countExpression = hasCount && countValue.ValueKind == JsonValueKind.String ? countValue.GetString() : null;
        if (countType is null && fixedCount is null && countExpression is null)
            throw new SchemaException($"{path} must contain countType or count.");
        return new SchemaNode
        {
            Kind = kind,
            CountType = countType,
            FixedCount = fixedCount,
            CountExpression = countExpression,
            Encoding = options.TryGetProperty("encoding", out JsonElement encoding) ? encoding.GetString() : null,
        };
    }

    private static SchemaNode NormalizeEncapsulated(JsonElement options, string path, HashSet<string> definedTypes)
    {
        RequireObject(options, path);
        if (!options.TryGetProperty("type", out JsonElement element))
            throw new SchemaException($"{path} encapsulated type must contain type.");
        return new SchemaNode
        {
            Kind = SchemaNodeKind.Encapsulated,
            CountType = RequireString(options, "lengthType", path),
            Element = NormalizeNode(element, $"{path}.element", definedTypes),
        };
    }

    private static void RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new SchemaException($"{path} options must be an object.");
    }

    private static string RequireString(JsonElement value, string propertyName, string path)
    {
        if (!value.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            throw new SchemaException($"{path} must contain string {propertyName}.");
        return property.GetString()!;
    }
}
