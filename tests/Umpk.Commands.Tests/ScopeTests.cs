using Xunit;

namespace Umpk.Commands.Tests;

public sealed class ScopeTests
{
    [Fact]
    public async Task Disposing_one_scope_leaves_the_other_intact()
    {
        var service = new CommandService<TestSource>();
        var scopeA = service.CreateScope("a");
        var scopeB = service.CreateScope("b");

        scopeA.Register(b => b.Literal("alpha", n => n.Executes(_ => new ValueTask<int>(1))));
        scopeB.Register(b => b.Literal("bravo", n => n.Executes(_ => new ValueTask<int>(2))));

        Assert.True((await service.ExecuteAsync("alpha", new TestSource())).Success);
        Assert.True((await service.ExecuteAsync("bravo", new TestSource())).Success);

        scopeA.Dispose();

        // The disposed scope's command is gone.
        var goneResult = await service.ExecuteAsync("alpha", new TestSource());
        Assert.False(goneResult.Success);

        // The surviving scope still dispatches.
        var survivorResult = await service.ExecuteAsync("bravo", new TestSource());
        Assert.True(survivorResult.Success);
        Assert.Equal(2, survivorResult.Value);

        scopeB.Dispose();
    }

    [Fact]
    public async Task Disposing_a_scope_preserves_another_scopes_redirect()
    {
        // Rebuilding the merged tree when one scope is disposed must preserve the surviving scope's alias/redirect wiring, not just its plain literals.
        var service = new CommandService<TestSource>();
        var scopeA = service.CreateScope("a");
        var scopeB = service.CreateScope("b");

        scopeA.Register(b => b.Literal("alpha", n => n.Executes(_ => new ValueTask<int>(1))));
        scopeB.Register(b =>
        {
            LiteralCommandBuilder<TestSource>? target = null;
            b.Literal("primary", primary =>
            {
                target = (LiteralCommandBuilder<TestSource>)primary;
                primary.ThenLiteral("run", run => run.Executes(_ => new ValueTask<int>(9)));
            });
            b.Literal("alias", alias => alias.RedirectTo(target!));
        });

        Assert.Equal(9, (await service.ExecuteAsync("alias run", new TestSource())).Value);

        // Disposing the unrelated scope forces a tree rebuild; scope B's redirect must still resolve.
        scopeA.Dispose();

        var afterRebuild = await service.ExecuteAsync("alias run", new TestSource());
        Assert.True(afterRebuild.Success);
        Assert.Equal(9, afterRebuild.Value);

        scopeB.Dispose();
    }

    [Fact]
    public async Task Registration_is_live_immediately()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("late");

        Assert.False((await service.ExecuteAsync("later", new TestSource())).Success);

        scope.Register(b => b.Literal("later", n => n.Executes(_ => new ValueTask<int>(1))));

        Assert.True((await service.ExecuteAsync("later", new TestSource())).Success);
    }

    [Fact]
    public async Task Disposing_all_scopes_empties_the_tree()
    {
        var service = new CommandService<TestSource>();
        var scope = service.CreateScope("only");
        scope.Register(b => b.Literal("solo", n => n.Executes(_ => new ValueTask<int>(1))));

        scope.Dispose();

        Assert.False((await service.ExecuteAsync("solo", new TestSource())).Success);
    }

    [Fact]
    public void Double_dispose_is_safe()
    {
        var service = new CommandService<TestSource>();
        var scope = service.CreateScope("x");
        scope.Register(b => b.Literal("y", n => n.Executes(_ => new ValueTask<int>(1))));

        scope.Dispose();
        scope.Dispose();
    }

    [Fact]
    public void Register_after_dispose_throws()
    {
        var service = new CommandService<TestSource>();
        var scope = service.CreateScope("x");
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            scope.Register(b => b.Literal("z", n => n.Executes(_ => new ValueTask<int>(1)))));
    }

    [Fact]
    public void Scope_exposes_owner_id()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("owner-42");
        Assert.Equal("owner-42", scope.OwnerId);
    }
}
