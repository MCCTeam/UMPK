using Xunit;

namespace Umpk.Commands.Tests;

public sealed class RedirectTests
{
    [Fact]
    public async Task Redirect_reuses_the_target_subtree()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");

        scope.Register(b =>
        {
            LiteralCommandBuilder<TestSource>? target = null;
            b.Literal("primary", primary =>
            {
                target = (LiteralCommandBuilder<TestSource>)primary;
                primary.ThenLiteral("run", run => run.Executes(_ => new ValueTask<int>(9)));
            });
            b.Literal("alias", alias => alias.RedirectTo(target!));
        });

        // The alias redirects into primary's subtree, so "alias run" reaches primary's body.
        var result = await service.ExecuteAsync("alias run", new TestSource());
        Assert.True(result.Success);
        Assert.Equal(9, result.Value);
    }
}
