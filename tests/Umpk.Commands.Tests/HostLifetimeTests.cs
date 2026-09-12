using Xunit;

namespace Umpk.Commands.Tests;

/// <summary>Pins the two load-bearing contract points a host embedding <see cref="CommandService{TSource}"/> relies on: the service is host-ownable with no client/session object anywhere in the picture, and scope disposal only ever removes the disposed scope's own contribution to a merged tree, never another scope's.</summary>
public sealed class HostLifetimeTests
{
    [Fact]
    public async Task A_service_built_by_a_host_dispatches_with_no_client()
    {
        // The public parameterless constructor is the whole contract: a host new()s this up directly, with no UmpkClient, session, or connection anywhere in the picture.
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("host");
        scope.Register(b => b.Literal("status", n => n.Executes(_ => new ValueTask<int>(1))));

        var result = await service.ExecuteAsync("status", new TestSource());

        Assert.True(result.Success);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public async Task A_session_scope_disposal_leaves_the_host_scope_dispatchable()
    {
        // A host-owned service outlives any one session: the host registers its own commands in a long-lived scope, then opens and closes a session-lived scope around it.
        var service = new CommandService<TestSource>();
        using var hostScope = service.CreateScope("host");
        hostScope.Register(b => b.Literal("help", n => n.Executes(_ => new ValueTask<int>(1))));

        var sessionScope = service.CreateScope("session-1");
        sessionScope.Register(b => b.Literal("whoami", n => n.Executes(_ => new ValueTask<int>(2))));

        Assert.True((await service.ExecuteAsync("whoami", new TestSource())).Success);

        sessionScope.Dispose();

        var afterSessionEnds = await service.ExecuteAsync("help", new TestSource());
        Assert.True(afterSessionEnds.Success);
        Assert.Equal(1, afterSessionEnds.Value);

        var goneWithSession = await service.ExecuteAsync("whoami", new TestSource());
        Assert.False(goneWithSession.Success);
    }

    [Fact]
    public async Task Two_scopes_registering_the_same_root_literal_merge_their_children()
    {
        var service = new CommandService<TestSource>();
        var scopeA = service.CreateScope("a");
        var scopeB = service.CreateScope("b");

        scopeA.Register(b => b.Literal("cfg", cfg => cfg.ThenLiteral("a", n => n.Executes(_ => new ValueTask<int>(1)))));
        scopeB.Register(b => b.Literal("cfg", cfg => cfg.ThenLiteral("b", n => n.Executes(_ => new ValueTask<int>(2)))));

        var fromA = await service.ExecuteAsync("cfg a", new TestSource());
        var fromB = await service.ExecuteAsync("cfg b", new TestSource());

        Assert.True(fromA.Success);
        Assert.Equal(1, fromA.Value);
        Assert.True(fromB.Success);
        Assert.Equal(2, fromB.Value);

        scopeA.Dispose();
        scopeB.Dispose();
    }

    [Fact]
    public async Task A_merged_literal_keeps_the_body_of_the_registration_that_declared_one()
    {
        var service = new CommandService<TestSource>();
        var scopeA = service.CreateScope("a");
        var scopeB = service.CreateScope("b");

        // Only scope A's registration of "cfg" declares a body; scope B's declares a child, no body.
        scopeA.Register(b => b.Literal("cfg", cfg => cfg.Executes(_ => new ValueTask<int>(9))));
        scopeB.Register(b => b.Literal("cfg", cfg => cfg.ThenLiteral("child", n => n.Executes(_ => new ValueTask<int>(2)))));

        var bare = await service.ExecuteAsync("cfg", new TestSource());
        var withChild = await service.ExecuteAsync("cfg child", new TestSource());

        Assert.True(bare.Success);
        Assert.Equal(9, bare.Value);
        Assert.True(withChild.Success);
        Assert.Equal(2, withChild.Value);

        scopeA.Dispose();
        scopeB.Dispose();
    }
}
