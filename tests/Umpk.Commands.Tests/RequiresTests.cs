using Xunit;

namespace Umpk.Commands.Tests;

public sealed class RequiresTests
{
    [Fact]
    public async Task Requires_predicate_blocks_disallowed_source()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("op", n =>
            n.Requires(s => s.GetService<AdminFlag>() is not null)
             .Executes(_ => new ValueTask<int>(1))));

        var deniedResult = await service.ExecuteAsync("op", new TestSource());
        Assert.False(deniedResult.Success);

        var allowed = new TestSource().AddService(new AdminFlag());
        var allowedResult = await service.ExecuteAsync("op", allowed);
        Assert.True(allowedResult.Success);
    }

    [Fact]
    public async Task Requires_hides_node_from_completion()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("secret", n =>
            n.Requires(s => s.GetService<AdminFlag>() is not null)
             .Executes(_ => new ValueTask<int>(1))));
        scope.Register(b => b.Literal("public", n => n.Executes(_ => new ValueTask<int>(1))));

        var completions = await service.CompleteAsync("", 0, new TestSource());
        var texts = completions.Suggestions.Select(s => s.Text).ToList();

        Assert.Contains("public", texts);
        Assert.DoesNotContain("secret", texts);
    }

    private sealed class AdminFlag;
}
