using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Umpk.Protocol.Java.Signing;

/// <summary>Reproduces stable component JSON for an arbitrary component, starting from the raw JSON the component arrived as on the wire rather than from a component model. This is what the 1.19.1/1.19.2 (v2) signed body folds in when the server decorated a chat message.</summary>
/// <remarks>
/// <para>The wire JSON and signed JSON encode the same element tree. Parsing the wire document is therefore lossless and avoids building a separate component model.</para>
/// <para>The stable form sorts every object's keys by ordinal UTF-16 order while preserving array order and JSON value types. Both forms use the same non-HTML-safe escaping rules.</para>
/// <para>Object keys use ordinal UTF-16 code-unit order, which is <see cref="string.CompareOrdinal(string, string)"/>.</para>
/// <para>Numbers are re-emitted from their original wire lexeme, avoiding changes from numeric parsing and formatting.</para>
/// </remarks>
internal static class VanillaStableJson
{
    /// <summary>The maximum nesting this will parse. Chat components nest through <c>extra</c>, <c>with</c> and hover contents; a server-supplied document deeper than this is refused rather than parsed, so a hostile frame cannot drive unbounded recursion on the read loop.</summary>
    private const int MaxDepth = 64;

    /// <summary>Canonicalises one wire-form component JSON document into the bytes vanilla would hash. Returns null when the document cannot be read, in which case the caller must fall back to treating the message as unverifiable rather than hashing a guess.</summary>
    /// <remarks>This never throws for server-supplied input, and that is a hard requirement rather than a nicety: it runs on the inbound signature path, whose only caller catches <see cref="System.Security.Cryptography.CryptographicException"/>, so anything else would escape into the connection's read loop. <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/> accepts a lone surrogate escape such as <c>{"a":"\ud800"}</c> and only fails later, from <see cref="JsonElement.GetString"/>, with an <see cref="InvalidOperationException"/>, so the catch has to cover the read as well as the parse.</remarks>
    public static string? TryCanonicalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var options = new JsonDocumentOptions
            {
                MaxDepth = MaxDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            };
            using JsonDocument document = JsonDocument.Parse(json, options);

            // A bare JSON string is a legal component and canonicalizes to {"text":"just text"}. Every other shape is written back as parsed.
            if (document.RootElement.ValueKind == JsonValueKind.String)
                return EncodeLiteral(document.RootElement.GetString() ?? string.Empty);

            var builder = new StringBuilder(json.Length + 16);
            Write(builder, document.RootElement);
            return builder.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // A lone surrogate escape parses and then fails on read. Same answer: no canonical form.
            return null;
        }
        catch (ArgumentException)
        {
            // Defensive: JsonDocument.Parse validates its own arguments, and a malformed document should never reach this arm, but nothing on this path may throw into the read loop.
            return null;
        }
    }

    /// <summary>Produces the stable shape for a bare literal with no style and no siblings: <c>{"text": text}</c>.</summary>
    public static string EncodeLiteral(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var json = new StringBuilder(text.Length + 16);
        json.Append("{\"text\":");
        AppendString(json, text);
        json.Append('}');
        return json.ToString();
    }

    private static void Write(StringBuilder builder, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(builder, element);
                break;

            case JsonValueKind.Array:
                builder.Append('[');
                bool firstItem = true;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (!firstItem)
                        builder.Append(',');

                    firstItem = false;
                    Write(builder, item);
                }

                builder.Append(']');
                break;

            case JsonValueKind.String:
                AppendString(builder, element.GetString() ?? string.Empty);
                break;

            case JsonValueKind.Number:
                // Preserve the number's wire lexeme verbatim.
                builder.Append(element.GetRawText());
                break;

            case JsonValueKind.True:
                builder.Append("true");
                break;

            case JsonValueKind.False:
                builder.Append("false");
                break;

            default:
                builder.Append("null");
                break;
        }
    }

    private static void WriteObject(StringBuilder builder, JsonElement element)
    {
        // A repeated key replaces the earlier value while retaining one object entry.
        var members = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
            members[property.Name] = property.Value;

        builder.Append('{');
        bool first = true;
        foreach (KeyValuePair<string, JsonElement> member in members)
        {
            if (!first)
                builder.Append(',');

            first = false;
            AppendString(builder, member.Key);
            builder.Append(':');
            Write(builder, member.Value);
        }

        builder.Append('}');
    }

    /// <summary>Writes one JSON string with non-HTML-safe stable escaping: the two JSON-mandatory escapes, the five short control forms, lowercase <c>\u00xx</c> for the remaining C0 controls, and <c>U+2028</c> / <c>U+2029</c>. Everything else, including U+007F, the C1 block, the Private Use Area and every astral code point, is written verbatim.</summary>
    /// <remarks><c>SignedChatStableJsonConformanceTests</c> pins every well-formed BMP scalar plus astral samples through <see cref="EncodeLiteral"/>.</remarks>
    public static void AppendString(StringBuilder json, string value)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(value);
        json.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\b': json.Append("\\b"); break;
                case '\t': json.Append("\\t"); break;
                case '\n': json.Append("\\n"); break;
                case '\f': json.Append("\\f"); break;
                case '\r': json.Append("\\r"); break;

                // Escape the two Unicode line separators because they are unsafe in JavaScript literals.
                case '\u2028': json.Append("\\u2028"); break;
                case '\u2029': json.Append("\\u2029"); break;
                default:
                    if (c < 0x20)
                    {
                        // Stable escapes use lowercase hex digits. U+007F stays verbatim.
                        json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                        json.Append(c);

                    break;
            }
        }

        json.Append('"');
    }
}
