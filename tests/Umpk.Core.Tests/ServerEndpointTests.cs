using Xunit;

namespace Umpk.Tests;

public class ServerEndpointTests
{
    [Theory]
    [InlineData("example.com", "example.com", 25565)]
    [InlineData("example.com:25566", "example.com", 25566)]
    [InlineData("192.168.1.10:1234", "192.168.1.10", 1234)]
    [InlineData("[::1]", "::1", 25565)]
    [InlineData("[2001:db8::1]:19132", "2001:db8::1", 19132)]
    [InlineData("2001:db8::1", "2001:db8::1", 25565)]
    [InlineData("  example.com  ", "example.com", 25565)]
    public void Parse_AcceptsValidForms(string input, string host, int port)
    {
        var endpoint = ServerEndpoint.Parse(input);
        Assert.Equal(host, endpoint.Host);
        Assert.Equal((ushort)port, endpoint.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(":25565")]
    [InlineData("host:notaport")]
    [InlineData("host:70000")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    public void Parse_RejectsInvalidForms(string input)
    {
        Assert.Throws<FormatException>(() => ServerEndpoint.Parse(input));
        Assert.False(ServerEndpoint.TryParse(input, out _));
    }

    [Fact]
    public void ToString_BracketsIpv6()
    {
        Assert.Equal("example.com:25565", new ServerEndpoint("example.com").ToString());
        Assert.Equal("[::1]:25565", new ServerEndpoint("::1").ToString());
    }
}
