using Umpk.Text;
using Xunit;

namespace Umpk.Commands.Tests;

public sealed class DispatchTests
{
    [Fact]
    public async Task Literal_command_executes()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("ping", n => n.Executes(_ => new ValueTask<int>(7))));

        var result = await service.ExecuteAsync("ping", new TestSource());

        Assert.True(result.Success);
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public async Task Argument_value_is_parsed_and_readable()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        int captured = 0;
        scope.Register(b => b.Literal("add", n =>
            n.ThenArgument("amount", Arguments.Integer(), a =>
                a.Executes(ctx =>
                {
                    captured = ctx.GetArgument<int>("amount");
                    return new ValueTask<int>(1);
                }))));

        var result = await service.ExecuteAsync("add 42", new TestSource());

        Assert.True(result.Success);
        Assert.Equal(42, captured);
    }

    [Fact]
    public async Task Nested_literals_dispatch_to_the_deepest_body()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        string reached = "";
        scope.Register(b => b.Literal("cfg", root =>
            root.ThenLiteral("set", set =>
                set.ThenLiteral("on", on => on.Executes(_ =>
                {
                    reached = "on";
                    return new ValueTask<int>(1);
                })))));

        var result = await service.ExecuteAsync("cfg set on", new TestSource());

        Assert.True(result.Success);
        Assert.Equal("on", reached);
    }

    [Fact]
    public async Task Unknown_command_fails_without_throwing()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("ping", n => n.Executes(_ => new ValueTask<int>(1))));

        var result = await service.ExecuteAsync("pong", new TestSource());

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Source_reaches_body_and_reply_flows()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("hi", n => n.Executes(async ctx =>
        {
            await ctx.Source.ReplyAsync(Component.Text("hello"));
            return 1;
        })));

        var source = new TestSource();
        var result = await service.ExecuteAsync("hi", source);

        Assert.True(result.Success);
        Assert.True(source.Replies.TryDequeue(out var reply));
        Assert.Equal("hello", ((TextContent)reply!.Content).Text);
    }

    [Fact]
    public async Task GetService_resolves_host_context()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        object? seen = null;
        var marker = new object();
        scope.Register(b => b.Literal("who", n => n.Executes(ctx =>
        {
            seen = ctx.Source.GetService<object>();
            return new ValueTask<int>(1);
        })));

        var source = new TestSource().AddService(marker);
        await service.ExecuteAsync("who", source);

        Assert.Same(marker, seen);
    }

    [Fact]
    public async Task A_synchronous_body_dispatches_and_returns_its_value()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("sync", n => n.Executes(_ => 5)));

        var result = await service.ExecuteAsync("sync", new TestSource());

        Assert.True(result.Success);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public async Task A_synchronous_and_an_asynchronous_body_coexist_in_one_tree()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b =>
        {
            b.Literal("sync", n => n.Executes(_ => 1));
            b.Literal("async", n => n.Executes(_ => new ValueTask<int>(2)));
        });

        var syncResult = await service.ExecuteAsync("sync", new TestSource());
        var asyncResult = await service.ExecuteAsync("async", new TestSource());

        Assert.True(syncResult.Success);
        Assert.Equal(1, syncResult.Value);
        Assert.True(asyncResult.Success);
        Assert.Equal(2, asyncResult.Value);
    }
}
