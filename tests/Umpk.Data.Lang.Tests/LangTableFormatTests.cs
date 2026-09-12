using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Umpk.Data.Lang.Tests;

/// <summary>Validates the language tables embedded in <c>Umpk.Data.Lang</c> after pool packing and delta-index decoding. The test reads <c>JavaLanguage</c> directly because <c>ITranslationSource</c> deliberately exposes lookup only, not key enumeration.</summary>
public sealed partial class LangTableFormatTests
{
    // This is the same accepted placeholder grammar used by the generator. It remains local because the test project must not acquire a runtime dependency on the generator tool.
    [GeneratedRegex(@"\G%(?:(\d+)\$)?([A-Za-z%]|$)")]
    private static partial Regex FormatGroupPattern();

    // These entries use general string formatting or intentionally invalid placeholders, so they are outside the translatable-component placeholder grammar.
    private static readonly HashSet<string> ExemptKeys = new(StringComparer.Ordinal)
    {
        "commands.debug.stop",
        "commands.generic.double.tooBig",
        "commands.generic.double.tooSmall",
        "translation.test.invalid2",
    };

    private static readonly Lazy<(string Key, string Template)[]> Pool = new(DecodePool);

    public static TheoryData<int> Protocols()
    {
        TheoryData<int> data = [];
        foreach (int protocol in VanillaTranslations.Protocols.ToArray())
            data.Add(protocol);

        return data;
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public void EveryTemplate_ParsesUnderVanillaFormatPattern(int protocol)
    {
        foreach ((string key, string template) in DecodeEntries(protocol))
        {
            if (ExemptKeys.Contains(key))
                continue;

            Assert.True(
                FormatGroupsParse(template),
                $"proto {protocol}: '{key}' = '{template}' has an unparseable format specifier");
        }
    }

    [Fact]
    public void NoShippedTableIsEmpty()
    {
        foreach (int protocol in VanillaTranslations.Protocols.ToArray())
            Assert.True(VanillaTranslations.CountFor(protocol) > 0, $"protocol {protocol} has an empty table");

    }

    private static bool FormatGroupsParse(string template)
    {
        int i = 0;
        while (true)
        {
            int percent = template.IndexOf('%', i);
            if (percent < 0)
                return true;

            Match match = FormatGroupPattern().Match(template, percent);
            if (!match.Success)
                return false;

            i = percent + match.Length;
        }
    }

    /// <summary>Decodes protocol -&gt; its (key, template) pairs, resolved through the shared pool.</summary>
    private static List<(string Key, string Template)> DecodeEntries(int protocol)
    {
        ReadOnlySpan<byte> index = JavaLanguage.Index(protocol);
        List<(string, string)> result = [];
        if (index.IsEmpty)
            return result;

        (string Key, string Template)[] pool = Pool.Value;
        int pos = 0;
        int count = ReadVarInt(index, ref pos);
        int running = 0;
        for (int i = 0; i < count; i++)
        {
            running += ReadVarInt(index, ref pos);
            result.Add(pool[running]);
        }

        return result;
    }

    private static (string Key, string Template)[] DecodePool()
    {
        ReadOnlySpan<byte> pool = JavaLanguage.Pool;
        int pos = 0;
        int count = ReadVarInt(pool, ref pos);
        var entries = new (string Key, string Template)[count];
        for (int i = 0; i < count; i++)
        {
            int keyLen = ReadVarInt(pool, ref pos);
            string key = Encoding.UTF8.GetString(pool.Slice(pos, keyLen));
            pos += keyLen;
            int tmplLen = ReadVarInt(pool, ref pos);
            string template = Encoding.UTF8.GetString(pool.Slice(pos, tmplLen));
            pos += tmplLen;
            entries[i] = (key, template);
        }

        return entries;
    }

    private static int ReadVarInt(ReadOnlySpan<byte> data, ref int pos)
    {
        int result = 0;
        int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;

            shift += 7;
        }
    }
}
