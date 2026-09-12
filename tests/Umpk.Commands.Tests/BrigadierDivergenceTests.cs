using Xunit;

namespace Umpk.Commands.Tests;

/// <summary>Explicit per-divergence coverage for the three documented UMPK-vs-Brigadier.NET behavior differences. Each fact isolates one divergence: case-insensitive prefix filtering of suggestions (<c>BrigadierSuggestionSink</c>), <c>CanUse</c>/<c>Requires</c> node filtering in dispatch and completion, and the synchronous <see cref="int"/> bridge over Brigadier's sync execute contract (<c>ExecutionBridge</c>), which must complete an async body whose <see cref="ValueTask{T}"/> is not synchronously done.</summary>
public sealed class BrigadierDivergenceTests
{
    [Fact]
    public async Task Divergence1_Suggestions_are_prefix_filtered_case_insensitively()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("kick", n =>
            n.ThenArgument("player", Arguments.Word(), a =>
                a.Suggests((_, sink) =>
                {
                    sink.Suggest("Alice");
                    sink.Suggest("Bob");
                    return ValueTask.CompletedTask;
                })
                 .Executes(_ => new ValueTask<int>(1)))));

        // Lowercase "a" token must match "Alice" (case-insensitive prefix) and exclude "Bob"; vanilla Brigadier.NET would return both because it does not prefix-filter provider output.
        var result = await service.CompleteAsync("kick a", 6, new TestSource());
        var texts = result.Suggestions.Select(s => s.Text).ToList();
        Assert.Contains("Alice", texts);
        Assert.DoesNotContain("Bob", texts);
    }

    [Fact]
    public async Task Divergence2_CanUse_filters_dispatch_and_completion()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("op", n =>
            n.Requires(s => s.GetService<AdminFlag>() is not null)
             .Executes(_ => new ValueTask<int>(1))));

        Assert.False((await service.ExecuteAsync("op", new TestSource())).Success);

        var admin = new TestSource().AddService(new AdminFlag());
        Assert.True((await service.ExecuteAsync("op", admin)).Success);

        var texts = (await service.CompleteAsync("", 0, new TestSource())).Suggestions.Select(s => s.Text);
        Assert.DoesNotContain("op", texts);
    }

    [Fact]
    public async Task Divergence3_Sync_bridge_completes_an_incomplete_valuetask_body()
    {
        var service = new CommandService<TestSource>();
        using var scope = service.CreateScope("t");
        scope.Register(b => b.Literal("slow", n => n.Executes(async _ =>
        {
            await Task.Yield(); // forces a non-synchronously-completed ValueTask through ExecutionBridge
            return 7;
        })));

        var result = await service.ExecuteAsync("slow", new TestSource());
        Assert.True(result.Success);
        Assert.Equal(7, result.Value);
    }

    private sealed class AdminFlag;
}
