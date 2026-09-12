using Xunit;

namespace Umpk.Commands.Tests;

public sealed class ConcurrencyTests
{
    [Fact]
    public async Task Concurrent_registration_and_dispatch_stay_consistent()
    {
        var service = new CommandService<TestSource>();

        // A stable baseline command so dispatch always has something to find.
        using var baseScope = service.CreateScope("base");
        baseScope.Register(b => b.Literal("ping", n => n.Executes(_ => new ValueTask<int>(1))));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var failures = 0;

        // One task churns scopes (register + dispose) while others dispatch concurrently.
        var churn = Task.Run(async () =>
        {
            var round = 0;
            while (!cts.IsCancellationRequested)
            {
                var scope = service.CreateScope($"churn-{round}");
                scope.Register(b => b.Literal($"cmd{round % 8}", n => n.Executes(_ => new ValueTask<int>(round))));
                await Task.Yield();
                scope.Dispose();
                round++;
            }
        });

        var dispatchers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                // "ping" is always registered; it must never throw and must always succeed.
                var result = await service.ExecuteAsync("ping", new TestSource());
                if (!result.Success)
                    Interlocked.Increment(ref failures);

                // Completion runs against a shifting tree; it must never throw.
                await service.CompleteAsync("cmd", 3, new TestSource());
            }
        })).ToArray();

        await Task.WhenAll(dispatchers.Append(churn));

        Assert.Equal(0, failures);
    }

    [Fact]
    public async Task Parallel_scopes_register_without_loss()
    {
        var service = new CommandService<TestSource>();
        var scopes = new List<ICommandRegistrationScope<TestSource>>();
        try
        {
            var created = Enumerable.Range(0, 32).AsParallel().Select(i =>
            {
                var scope = service.CreateScope($"s{i}");
                scope.Register(b => b.Literal($"n{i}", n => n.Executes(_ => new ValueTask<int>(i))));
                return scope;
            }).ToList();

            scopes.AddRange(created);

            for (var i = 0; i < 32; i++)
            {
                var result = await service.ExecuteAsync($"n{i}", new TestSource());
                Assert.True(result.Success);
                Assert.Equal(i, result.Value);
            }
        }
        finally
        {
            foreach (var scope in scopes)
                scope.Dispose();

        }
    }
}
