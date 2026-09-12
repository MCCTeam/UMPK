using System.Text;
using Umpk.Protocol.Java;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests;

/// <summary><see cref="ServerStatus.Parse"/> decodes the server list ping's raw JSON body into structured status fields. Parsing is total: every optional sub-object is lenient, so a malformed sub-object yields null or an empty collection for that field rather than failing the whole parse.</summary>
public sealed class ServerStatusParseTests
{
    private static readonly Guid SampleGuid1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SampleGuid2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(5);

    [Fact]
    public void Parse_ModernResponse_ReadsVersionPlayersDescriptionAndFavicon()
    {
        byte[] faviconBytes = [0x89, 0x50, 0x4E, 0x47];
        string faviconBase64 = Convert.ToBase64String(faviconBytes);
        string json = $$"""
            {"version":{"name":"1.21.5","protocol":770},"players":{"max":20,"online":3,"sample":[{"id":"{{SampleGuid1}}","name":"Alice"},{"id":"{{SampleGuid2}}","name":"Bob"}]},"description":{"text":"Hello, ","extra":[{"text":"World!"}]},"favicon":"data:image/png;base64,{{faviconBase64}}","enforcesSecureChat":true}
            """;

        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.Equal("1.21.5", status.VersionName);
        Assert.Equal(770, status.Protocol);
        Assert.Equal(3, status.OnlinePlayers);
        Assert.Equal(20, status.MaxPlayers);
        Assert.Equal(2, status.Sample.Count);
        Assert.Contains(status.Sample, p => p.Id == SampleGuid1 && p.Name == "Alice");
        Assert.Contains(status.Sample, p => p.Id == SampleGuid2 && p.Name == "Bob");
        Assert.NotNull(status.Description);
        Assert.Equal("Hello, World!", status.Description!.ToPlainText());
        Assert.True(faviconBytes.AsSpan().SequenceEqual(status.Favicon.Span));
        Assert.True(status.EnforcesSecureChat);
        Assert.Equal(json, status.Json);
    }

    [Fact]
    public void Parse_DescriptionAsBareString_BecomesATextComponent()
    {
        // A bare string description decodes to a literal TextComponent.
        const string json = """{"description":"A Minecraft Server"}""";

        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.NotNull(status.Description);
        Assert.Equal("A Minecraft Server", status.Description!.ToPlainText());
    }

    [Fact]
    public void Parse_DescriptionWithSectionCodes_ExpandsThemIntoStyles()
    {
        // Rendering-fidelity choice: vanilla decodes a string description to a literal TextComponent and expands section codes at draw time in StringDecomposer; a style-rendering consumer needs them expanded to reach the same rendered result. Json retains the raw form.
        const string json = """{"description":"§aGreen §cRed"}""";

        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.NotNull(status.Description);
        Component description = status.Description!;
        Assert.Equal(2, description.Children.Count);
        Assert.Equal(TextColor.Green, description.Children[0].Style.Color);
        Assert.Equal(TextColor.Red, description.Children[1].Style.Color);
        Assert.DoesNotContain("§", description.ToPlainText(), StringComparison.Ordinal);
        Assert.Equal(json, status.Json);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"players":"nonsense"}""")]
    [InlineData("""{"version":{"name":"x"}}""")]
    public void Parse_MalformedSubObjects_YieldNullsNotAThrow(string json)
    {
        // A malformed optional sub-object yields no value
        // for that field, and never fails the parse for the rest of the document.
        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.Null(status.Protocol);
        Assert.Null(status.OnlinePlayers);
        Assert.Equal(json, status.Json);
    }

    [Fact]
    public void Parse_StringifiedProtocol_IsAccepted()
    {
        // A string primitive is not a number in this JSON form, so the client shows no version at all; real proxies emit one; parse-in tolerance only, nothing reaches the wire.
        const string json = """{"version":{"name":"x","protocol":"770"}}""";

        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.Equal(770, status.Protocol);
    }

    [Theory]
    [InlineData("""{"favicon":"AQIDBA=="}""")]
    [InlineData("""{"favicon":"data:image/png;base64,not-valid-base64!!"}""")]
    public void Parse_MalformedFavicon_IsIgnored(string json)
    {
        // A missing prefix or an undecodable body leaves no favicon.
        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.True(status.Favicon.IsEmpty);
    }

    [Fact]
    public void Parse_FaviconWithEmbeddedNewlines_Decodes()
    {
        // Embedded newlines are removed before the favicon body is decoded.
        byte[] faviconBytes = [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05];
        string base64 = Convert.ToBase64String(faviconBytes);
        // "\\n" (backslash + n), not a raw newline byte: this text is JSON source, so the mangled base64 must carry the JSON escape sequence for a newline within the string literal.
        string mangled = string.Join("\\n", Chunk(base64, 4));
        string json = $$"""{"favicon":"data:image/png;base64,{{mangled}}"}""";

        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.True(faviconBytes.AsSpan().SequenceEqual(status.Favicon.Span));
    }

    [Fact]
    public void Parse_SampleWithAMalformedUuid_YieldsAnEmptySample()
    {
        // A failed list decode falls back to the default (empty), not a partially-filled list.
        string json = $$$"""
            {"players":{"max":20,"online":2,"sample":[{"id":"not-a-guid","name":"A"},{"id":"{{{SampleGuid1}}}","name":"B"}]}}
            """;

        ServerStatus status = ServerStatus.Parse(json, Latency);

        Assert.Empty(status.Sample);
    }

    private static IEnumerable<string> Chunk(string value, int size)
    {
        for (int i = 0; i < value.Length; i += size)
            yield return value.Substring(i, Math.Min(size, value.Length - i));

    }
}
