using Xunit;

namespace Umpk.Commands.Tests;

public sealed class ArgumentTypeTests
{
    private static async Task<(bool Success, T? Value)> RunAsync<T>(string input, IArgumentType<T> type)
        where T : notnull
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        T? captured = default;
        scope.Register(b => b.Literal("cmd", n =>
            n.ThenArgument("arg", type, a =>
                a.Executes(ctx =>
                {
                    captured = ctx.GetArgument<T>("arg");
                    return new ValueTask<int>(1);
                }))));

        var result = await service.ExecuteAsync("cmd " + input, new TestSource());
        return (result.Success, captured);
    }

    [Fact]
    public async Task Word_reads_a_single_token()
    {
        var (ok, value) = await RunAsync("hello", Arguments.Word());
        Assert.True(ok);
        Assert.Equal("hello", value);
    }

    [Fact]
    public async Task QuotableString_reads_a_quoted_phrase()
    {
        var (ok, value) = await RunAsync("\"hello world\"", Arguments.QuotableString());
        Assert.True(ok);
        Assert.Equal("hello world", value);
    }

    [Fact]
    public async Task GreedyString_consumes_the_remainder()
    {
        var (ok, value) = await RunAsync("the rest of the line", Arguments.GreedyString());
        Assert.True(ok);
        Assert.Equal("the rest of the line", value);
    }

    [Fact]
    public async Task Integer_in_range_parses()
    {
        var (ok, value) = await RunAsync("5", Arguments.Integer(0, 10));
        Assert.True(ok);
        Assert.Equal(5, value);
    }

    [Fact]
    public async Task Integer_out_of_range_fails()
    {
        var (ok, _) = await RunAsync("50", Arguments.Integer(0, 10));
        Assert.False(ok);
    }

    [Fact]
    public async Task Integer_non_numeric_fails()
    {
        var (ok, _) = await RunAsync("abc", Arguments.Integer());
        Assert.False(ok);
    }

    [Fact]
    public async Task Long_parses_large_value()
    {
        var (ok, value) = await RunAsync("9000000000", Arguments.Long());
        Assert.True(ok);
        Assert.Equal(9000000000L, value);
    }

    [Fact]
    public async Task Double_parses_invariant_decimal()
    {
        var (ok, value) = await RunAsync("3.5", Arguments.Double(0, 10));
        Assert.True(ok);
        Assert.Equal(3.5d, value);
    }

    [Fact]
    public async Task Float_out_of_range_fails()
    {
        var (ok, _) = await RunAsync("100.0", Arguments.Float(0f, 10f));
        Assert.False(ok);
    }

    [Fact]
    public async Task Bool_parses_true()
    {
        var (ok, value) = await RunAsync("true", Arguments.Bool());
        Assert.True(ok);
        Assert.True(value);
    }

    [Fact]
    public async Task Bool_invalid_fails()
    {
        var (ok, _) = await RunAsync("maybe", Arguments.Bool());
        Assert.False(ok);
    }

    [Fact]
    public async Task Identifier_with_namespace_parses()
    {
        var (ok, value) = await RunAsync("foo:bar", Arguments.Identifier());
        Assert.True(ok);
        Assert.Equal("foo", value.Namespace);
        Assert.Equal("bar", value.Path);
    }

    [Fact]
    public async Task Identifier_defaults_namespace()
    {
        var (ok, value) = await RunAsync("stone", Arguments.Identifier());
        Assert.True(ok);
        Assert.Equal("minecraft", value.Namespace);
        Assert.Equal("stone", value.Path);
    }

    [Fact]
    public async Task Identifier_invalid_fails()
    {
        var (ok, _) = await RunAsync("Foo:Bar", Arguments.Identifier());
        Assert.False(ok);
    }
}
