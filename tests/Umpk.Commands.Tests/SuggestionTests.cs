using Xunit;

namespace Umpk.Commands.Tests;

public sealed class SuggestionTests
{
    [Fact]
    public async Task Literal_names_are_suggested_at_root()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("teleport", n => n.Executes(_ => new ValueTask<int>(1))));
        scope.Register(b => b.Literal("tell", n => n.Executes(_ => new ValueTask<int>(1))));

        var result = await service.CompleteAsync("te", 2, new TestSource());
        var texts = result.Suggestions.Select(s => s.Text).ToList();

        Assert.Contains("teleport", texts);
        Assert.Contains("tell", texts);
    }

    [Fact]
    public async Task Custom_provider_queries_live_source_state()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("kick", n =>
            n.ThenArgument("player", Arguments.Word(), a =>
                a.Suggests((ctx, sink) =>
                {
                    var roster = ctx.Source.GetService<Roster>();
                    if (roster is not null)
                        foreach (var name in roster.Names)
                            sink.Suggest(name);

                    return ValueTask.CompletedTask;
                })
                 .Executes(_ => new ValueTask<int>(1)))));

        var source = new TestSource().AddService(new Roster("alice", "bob"));
        var result = await service.CompleteAsync("kick a", 6, source);
        var texts = result.Suggestions.Select(s => s.Text).ToList();

        Assert.Contains("alice", texts);
        Assert.DoesNotContain("bob", texts);
    }

    [Fact]
    public async Task Suggestion_tooltip_round_trips()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("give", n =>
            n.ThenArgument("item", Arguments.Word(), a =>
                a.Suggests((_, sink) =>
                {
                    sink.Suggest("apple", "a fruit");
                    return ValueTask.CompletedTask;
                })
                 .Executes(_ => new ValueTask<int>(1)))));

        var result = await service.CompleteAsync("give a", 6, new TestSource());
        var apple = Assert.Single(result.Suggestions, s => s.Text == "apple");
        Assert.Equal("a fruit", apple.Tooltip);
    }

    [Fact]
    public async Task Bool_argument_suggests_true_and_false()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("test");
        scope.Register(b => b.Literal("flag", n =>
            n.ThenArgument("value", Arguments.Bool(), a =>
                a.Executes(_ => new ValueTask<int>(1)))));

        var result = await service.CompleteAsync("flag ", 5, new TestSource());
        var texts = result.Suggestions.Select(s => s.Text).ToList();

        Assert.Contains("true", texts);
        Assert.Contains("false", texts);
    }

    private sealed class Roster(params string[] names)
    {
        public IReadOnlyList<string> Names { get; } = names;
    }
}
