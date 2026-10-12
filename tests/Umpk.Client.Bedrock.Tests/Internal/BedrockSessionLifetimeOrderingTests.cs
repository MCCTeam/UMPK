using System.Collections.Concurrent;
using Umpk.Client.Bedrock.Session;
using Xunit;

namespace Umpk.Client.Bedrock.Internal;

public sealed class BedrockSessionLifetimeOrderingTests
{
    [Fact]
    public async Task WinnerPreparesBeforeCancellationAndCompletesAfterTheExistingCallback()
    {
        using var lifetime = new BedrockSessionLifetime();
        var winner = new BedrockTermination(BedrockSessionEndReason.RemoteDisconnect, BedrockSessionScope.Session, "winner");
        var order = new List<string>();
        using CancellationTokenRegistration token = lifetime.Token.Register(() => order.Add("token"));
        Assert.True(lifetime.TryComplete(winner, beforeCompletion: () =>
        {
            Assert.True(lifetime.Token.IsCancellationRequested);
            Assert.False(lifetime.Termination.IsCompleted);
            order.Add("beforeCompletion");
        }, beforeCancellation: () =>
        {
            Assert.False(lifetime.Token.IsCancellationRequested);
            Assert.False(lifetime.Termination.IsCompleted);
            order.Add("beforeCancellation");
        }));
        Assert.Equal(new[] { "beforeCancellation", "token", "beforeCompletion" }, order);
        Assert.Same(winner, await lifetime.Termination.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(lifetime.Ready.IsCanceled);
    }

    [Fact]
    public async Task ReentrantLosingProposalCannotRunCallbacksOrReplaceTheWinner()
    {
        using var lifetime = new BedrockSessionLifetime();
        var winner = new BedrockTermination(BedrockSessionEndReason.RemoteDisconnect, BedrockSessionScope.Session, "remote");
        var loser = new BedrockTermination(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session, "dispose");
        bool winnerCallback = false;
        bool losingCallback = false;
        bool? reentrantWon = null;
        Assert.True(lifetime.TryComplete(winner, beforeCancellation: () =>
        {
            winnerCallback = true;
            reentrantWon = lifetime.TryComplete(loser, beforeCompletion: () => losingCallback = true, beforeCancellation: () => losingCallback = true);
            Assert.False(lifetime.Token.IsCancellationRequested);
        }));
        Assert.True(winnerCallback);
        Assert.False(reentrantWon);
        Assert.False(losingCallback);
        Assert.Same(winner, await lifetime.Termination.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ConcurrentCallbacksAndPublicationBelongOnlyToTheCasWinner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var lifetime = new BedrockSessionLifetime();
        var first = new BedrockTermination(BedrockSessionEndReason.RemoteDisconnect, BedrockSessionScope.Session, "remote");
        var second = new BedrockTermination(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session, "dispose");
        var beforeCancellation = new ConcurrentQueue<BedrockTermination>();
        var beforeCompletion = new ConcurrentQueue<BedrockTermination>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> a = Task.Run(async () =>
        {
            await start.Task.WaitAsync(timeout.Token);
            return lifetime.TryComplete(first, () => beforeCompletion.Enqueue(first), () => beforeCancellation.Enqueue(first));
        }, timeout.Token);
        Task<bool> b = Task.Run(async () =>
        {
            await start.Task.WaitAsync(timeout.Token);
            return lifetime.TryComplete(second, () => beforeCompletion.Enqueue(second), () => beforeCancellation.Enqueue(second));
        }, timeout.Token);
        start.TrySetResult();
        bool[] results = await Task.WhenAll(a, b).WaitAsync(timeout.Token);
        Assert.Equal(1, results.Count(static won => won));
        BedrockTermination winner = await lifetime.Termination.WaitAsync(timeout.Token);
        Assert.Same(winner, Assert.Single(beforeCancellation));
        Assert.Same(winner, Assert.Single(beforeCompletion));
        Assert.Same(results[0] ? first : second, winner);
    }

    [Fact]
    public async Task CompletedLoserDoesNotChangeAnyWinningCallbackState()
    {
        using var lifetime = new BedrockSessionLifetime();
        var winner = new BedrockTermination(BedrockSessionEndReason.RemoteDisconnect, BedrockSessionScope.Session, "first");
        int winningCalls = 0;
        int losingCalls = 0;
        Assert.True(lifetime.TryComplete(winner, beforeCancellation: () => winningCalls++));
        Assert.False(lifetime.TryComplete(new(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session), beforeCompletion: () => losingCalls++, beforeCancellation: () => losingCalls++));
        Assert.Equal(1, winningCalls);
        Assert.Equal(0, losingCalls);
        Assert.Same(winner, await lifetime.Termination.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ExistingSecondPositionalCallbackRetainsItsAfterCancellationOrder()
    {
        using var lifetime = new BedrockSessionLifetime();
        var winner = new BedrockTermination(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session);
        bool existingCallbackRan = false;
        Assert.True(lifetime.TryComplete(winner, () =>
        {
            Assert.True(lifetime.Token.IsCancellationRequested);
            Assert.False(lifetime.Termination.IsCompleted);
            existingCallbackRan = true;
        }));
        Assert.True(existingCallbackRan);
        Assert.Same(winner, await lifetime.Termination.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task WinningPreparationCanRetireAChildBeforeLinkedCancellationWithoutCancellingItTwice()
    {
        using var lifetime = new BedrockSessionLifetime();
        using CancellationTokenSource child = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var winner = new BedrockTermination(BedrockSessionEndReason.RemoteDisconnect, BedrockSessionScope.Session, "remote");
        BedrockTermination? recordedCause = null;
        bool eligible = true;
        bool? observedEligibility = null;
        BedrockTermination? observedCause = null;
        int childCallbacks = 0;
        using CancellationTokenRegistration registration = child.Token.Register(() =>
        {
            childCallbacks++;
            observedEligibility = eligible;
            observedCause = recordedCause;
        });
        Assert.True(lifetime.TryComplete(winner, beforeCancellation: () =>
        {
            recordedCause = winner;
            eligible = false;
            Assert.False(child.IsCancellationRequested);
        }));
        Assert.True(child.IsCancellationRequested);
        Assert.Equal(1, childCallbacks);
        Assert.False(observedEligibility);
        Assert.Same(winner, observedCause);
        Assert.Same(winner, await lifetime.Termination.WaitAsync(TimeSpan.FromSeconds(1)));
    }
}
