using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Umpk.DataGen.Tests;

/// <summary>The vanilla per-protocol lang dataset: <c>Validator.ValidateLang</c> and <c>Emitter.EmitLang</c>. Uses the same synthetic baseline every other DataGen test does; see <c>DatasetBuilder.LangEntries</c> for the fixture's shape (2100 shared filler entries plus, on the modern protocol only, the seven chat-type keys).</summary>
public sealed class LangDatasetTests
{
    [Fact]
    public void Validator_RejectsALangTableWithAnEmptyKey()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/lang.json", root =>
        {
            root["entries"]!.AsObject().Add("", "an empty key");
        });
        AssertHasProblemContaining(builder, "empty key");
    }

    [Fact]
    public void Validator_RejectsALangTableMissingProvenance()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/lang.json", root => root.Remove("_provenance"));
        AssertHasProblemContaining(builder, "_provenance.kind");
    }

    [Fact]
    public void Validator_RejectsAnUnparseableFormatSpecifier()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/lang.json", root =>
        {
            root["entries"]!["test.entry.0000"] = "a stray percent with nothing after it, then %  x";
        });
        AssertHasProblemContaining(builder, "unparseable format specifier");
    }

    [Fact]
    public void Validator_RejectsAModernTableMissingAChatTypeKey()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("770/lang.json", root =>
        {
            root["entries"]!.AsObject().Remove("chat.type.text");
        });
        AssertHasProblemContaining(builder, "missing built-in chat-type key 'chat.type.text'");
    }

    /// <summary>The baseline's 47 and 770 lang fixtures share the same 2100 filler (key, template) pairs (only 770 adds the seven chat-type keys on top), so the shared pairs must appear in the pool exactly once rather than once per protocol.</summary>
    [Fact]
    public void Emitter_PoolsIdenticalPairsAcrossProtocols()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.EmitLang();

        List<(string Key, string Template)> pool = DecodePool(files);

        // 2100 shared filler pairs + 7 chat-type pairs unique to 770 = 2107 distinct pairs. Had the pool NOT deduplicated across protocols this would instead be 2100 + 2107 = 4207.
        Assert.Equal(2107, pool.Count);
        Assert.Equal(pool.Count, pool.Distinct().Count());
    }

    /// <summary>Making protocol 47's lang table byte-identical to 770's (same key set, same templates) must collapse both protocols' index tables onto the SAME emitted blob member.</summary>
    [Fact]
    public void Emitter_DedupsProtocolsWithIdenticalTables()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        builder.Mutate("47/lang.json", root =>
        {
            JsonObject entries = root["entries"]!.AsObject();
            entries["chat.type.text"] = "<%s> %s";
            entries["chat.type.announcement"] = "[%s] %s";
            entries["chat.type.emote"] = "* %s %s";
            entries["chat.type.team.text"] = "%s <%s> %s";
            entries["chat.type.team.sent"] = "-> %s <%s> %s";
            entries["commands.message.display.incoming"] = "%s whispers to you: %s";
            entries["commands.message.display.outgoing"] = "You whisper to %s: %s";
        });

        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.EmitLang();
        string javaLanguage = files["JavaLanguage.g.cs"].Text;

        string member47 = ExtractIndexMember(javaLanguage, 47);
        string member770 = ExtractIndexMember(javaLanguage, 770);
        Assert.Equal(member770, member47);
        Assert.Equal("LanguageIndexV47", member47);
    }

    [Fact]
    public void Emitter_UsesSemanticLanguageTableNames()
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        var emitter = new Emitter(DatasetLoader.Load(builder.Root));
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.EmitLang();

        Assert.Contains("Pool => LangTables.LanguagePoolShared;", files["JavaLanguage.g.cs"].Text, StringComparison.Ordinal);
        Assert.Contains("47 => LangTables.LanguageIndexV47,", files["JavaLanguage.g.cs"].Text, StringComparison.Ordinal);
        Assert.DoesNotMatch("_[0-9a-f]{16}\\b", files["LangTables.g.cs"].Text);
    }

    /// <summary>Decoding a protocol's own pool references against the shared pool must reconstruct exactly the (key, template) set <c>DatasetLoader</c> read for that protocol: no pair dropped, none from another protocol's table picked up by a wrong index.</summary>
    [Theory]
    [InlineData(47)]
    [InlineData(770)]
    public void Emitter_RoundTripsAProtocolTable(int protocol)
    {
        using DatasetBuilder builder = DatasetBuilder.ValidBaseline();
        Dataset dataset = DatasetLoader.Load(builder.Root);
        var emitter = new Emitter(dataset);
        IReadOnlyDictionary<string, Emitter.EmittedFile> files = emitter.EmitLang();

        List<(string Key, string Template)> pool = DecodePool(files);
        string idxMember = ExtractIndexMember(files["JavaLanguage.g.cs"].Text, protocol);
        byte[] idxBlob = ExtractBlob(files["LangTables.g.cs"].Text, idxMember);

        Dictionary<string, string> decoded = new(StringComparer.Ordinal);
        foreach (int index in DecodeAscendingIndices(idxBlob))
        {
            (string key, string template) = pool[index];
            decoded[key] = template;
        }

        IReadOnlyDictionary<string, string> expected = dataset.ByProtocol[protocol].Lang;
        Assert.Equal(expected.Count, decoded.Count);
        foreach ((string key, string template) in expected)
        {
            Assert.True(decoded.TryGetValue(key, out string? actual), $"pool round-trip is missing key '{key}'");
            Assert.Equal(template, actual);
        }
    }

    private static void AssertHasProblemContaining(DatasetBuilder builder, string fragment)
    {
        Dataset dataset = DatasetLoader.Load(builder.Root);
        IReadOnlyList<string> problems = Validator.Validate(dataset);
        Assert.Contains(problems, p => p.Contains(fragment, StringComparison.Ordinal));
    }

    private static List<(string Key, string Template)> DecodePool(IReadOnlyDictionary<string, Emitter.EmittedFile> files)
    {
        string javaLanguage = files["JavaLanguage.g.cs"].Text;
        string poolMember = ExtractMember(javaLanguage, "Pool => LangTables.");
        byte[] poolBlob = ExtractBlob(files["LangTables.g.cs"].Text, poolMember);

        int pos = 0;
        int count = ReadVarInt(poolBlob, ref pos);
        List<(string, string)> pool = new(count);
        for (int i = 0; i < count; i++)
        {
            int keyLen = ReadVarInt(poolBlob, ref pos);
            string key = Encoding.UTF8.GetString(poolBlob, pos, keyLen);
            pos += keyLen;
            int tmplLen = ReadVarInt(poolBlob, ref pos);
            string template = Encoding.UTF8.GetString(poolBlob, pos, tmplLen);
            pos += tmplLen;
            pool.Add((key, template));
        }
        return pool;
    }

    private static List<int> DecodeAscendingIndices(byte[] blob)
    {
        int pos = 0;
        int count = ReadVarInt(blob, ref pos);
        List<int> indices = new(count);
        int running = 0;
        for (int i = 0; i < count; i++)
        {
            running += ReadVarInt(blob, ref pos);
            indices.Add(running);
        }
        return indices;
    }

    private static int ReadVarInt(byte[] data, ref int pos)
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

    private static string ExtractMember(string text, string marker)
    {
        foreach (string line in text.Split('\n'))
        {
            int idx = line.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
                return line[(idx + marker.Length)..].TrimEnd(';', ' ', '\r');

        }
        throw new InvalidOperationException($"no '{marker}' member found");
    }

    private static string ExtractIndexMember(string javaLanguage, int protocol)
    {
        string marker = $"        {protocol} => LangTables.";
        foreach (string line in javaLanguage.Split('\n'))
            if (line.StartsWith(marker, StringComparison.Ordinal))
                return line[marker.Length..].TrimEnd(',', ' ', '\r');

        throw new InvalidOperationException($"no Index(...) arm for protocol {protocol}");
    }

    private static byte[] ExtractBlob(string langTables, string member)
    {
        foreach (string line in langTables.Split('\n'))
        {
            int idx = line.IndexOf($"{member} => new byte[] {{ ", StringComparison.Ordinal);
            if (idx < 0)
                continue;

            string body = line[(idx + $"{member} => new byte[] {{ ".Length)..];
            body = body[..body.IndexOf('}', StringComparison.Ordinal)].Trim();
            return [.. body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Convert.ToByte(t[2..], 16))];
        }
        throw new InvalidOperationException($"no blob member '{member}' in LangTables");
    }
}
