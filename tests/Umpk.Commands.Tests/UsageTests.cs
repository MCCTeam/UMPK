using Xunit;

namespace Umpk.Commands.Tests;

public sealed class UsageTests
{
    [Fact]
    public void Smart_usage_lists_every_root_command_in_its_collapsed_form()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b =>
        {
            b.Literal("alpha", n => n.Executes(_ => new ValueTask<int>(1)));
            b.Literal("beta", n => n.Executes(_ => new ValueTask<int>(1)));
        });

        var usage = service.GetUsage(new TestSource());

        Assert.Contains(usage, u => u is { Name: "alpha", Usage: "alpha" });
        Assert.Contains(usage, u => u is { Name: "beta", Usage: "beta" });
    }

    [Fact]
    public void Smart_usage_omits_a_command_the_source_may_not_use()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b =>
        {
            b.Literal("visible", n => n.Executes(_ => new ValueTask<int>(1)));
            b.Literal("hidden", n => n.Requires(_ => false).Executes(_ => new ValueTask<int>(1)));
        });

        var usage = service.GetUsage(new TestSource());

        Assert.Contains(usage, u => u.Name == "visible");
        Assert.DoesNotContain(usage, u => u.Name == "hidden");
    }

    [Fact]
    public void Smart_usage_is_ordered_by_command_name()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b =>
        {
            b.Literal("zulu", n => n.Executes(_ => new ValueTask<int>(1)));
            b.Literal("mike", n => n.Executes(_ => new ValueTask<int>(1)));
            b.Literal("alpha", n => n.Executes(_ => new ValueTask<int>(1)));
        });

        var usage = service.GetUsage(new TestSource());

        Assert.Equal(["alpha", "mike", "zulu"], usage.Select(u => u.Name));
    }

    [Fact]
    public void All_usage_expands_every_branch_of_one_command()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("cfg", cfg =>
        {
            cfg.ThenLiteral("on", n => n.Executes(_ => new ValueTask<int>(1)));
            cfg.ThenLiteral("off", n => n.Executes(_ => new ValueTask<int>(1)));
        }));

        var usage = service.GetUsage("cfg", new TestSource());

        Assert.Equal(["off", "on"], usage.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void All_usage_renders_a_redirect_as_an_arrow_to_its_target()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b =>
        {
            LiteralCommandBuilder<TestSource>? target = null;
            b.Literal("primary", primary => target = (LiteralCommandBuilder<TestSource>)primary);
            b.Literal("cfg", cfg => cfg.ThenLiteral("go", go => go.RedirectTo(target!)));
        });

        var usage = service.GetUsage("cfg", new TestSource());

        Assert.Contains("go -> primary", usage);
    }

    [Fact]
    public void All_usage_of_an_unknown_command_is_empty()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("known", n => n.Executes(_ => new ValueTask<int>(1))));

        var usage = service.GetUsage("unknown", new TestSource());

        Assert.Empty(usage);
    }

    [Fact]
    public void All_usage_omits_a_branch_the_source_may_not_use()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("cfg", cfg =>
        {
            cfg.ThenLiteral("on", n => n.Executes(_ => new ValueTask<int>(1)));
            cfg.ThenLiteral("secret", n => n.Requires(_ => false).Executes(_ => new ValueTask<int>(1)));
        }));

        var usage = service.GetUsage("cfg", new TestSource());

        Assert.Contains("on", usage);
        Assert.DoesNotContain("secret", usage);
    }

    [Fact]
    public void Usage_reflects_a_scope_disposal()
    {
        var service = new CommandService<TestSource>();
        var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("temp", n => n.Executes(_ => new ValueTask<int>(1))));

        Assert.Contains(service.GetUsage(new TestSource()), u => u.Name == "temp");
        Assert.NotEmpty(service.GetUsage("temp", new TestSource()));

        scope.Dispose();

        Assert.DoesNotContain(service.GetUsage(new TestSource()), u => u.Name == "temp");
        Assert.Empty(service.GetUsage("temp", new TestSource()));
    }
}
