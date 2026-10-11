using Umpk.Client.Plugins;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class TickRegistryTests
{
    [Fact]
    public void DelaysFireOnceInRegistrationOrderAndSkipCancelledCallbacks()
    {
        var registry = new TickRegistry();
        var calls = new List<int>();
        registry.AddDelay(2, () => calls.Add(1));
        IDisposable cancelled = registry.AddDelay(2, () => calls.Add(2));
        registry.AddDelay(2, () => calls.Add(3));
        cancelled.Dispose();
        Action<Exception> onError = static ex => throw new InvalidOperationException("Unexpected callback failure.", ex);

        registry.Tick(onError);
        Assert.Empty(calls);
        registry.Tick(onError);
        Assert.Equal([1, 3], calls);
        registry.Tick(onError);
        Assert.Equal([1, 3], calls);
    }

    [Fact]
    public void RecurringCallbacksCanCancelAndScheduleWithoutChangingThisTicksSnapshot()
    {
        var registry = new TickRegistry();
        var calls = new List<int>();
        IDisposable? second = null;
        using IDisposable first = registry.Add(() =>
        {
            calls.Add(1);
            second!.Dispose();
            registry.AddDelay(0, () => calls.Add(3));
        });
        second = registry.Add(() => calls.Add(2));
        Action<Exception> onError = static ex => throw new InvalidOperationException("Unexpected callback failure.", ex);

        registry.Tick(onError);
        Assert.Equal([1], calls);
        first.Dispose();
        registry.Tick(onError);
        Assert.Equal([1, 3], calls);
    }

    [Fact]
    public void AFailingDueCallbackDoesNotSkipOtherDueCallbacks()
    {
        var registry = new TickRegistry();
        var failure = new InvalidOperationException("expected");
        var errors = new List<Exception>();
        int successful = 0;
        registry.AddDelay(0, () => throw failure);
        registry.AddDelay(0, () => successful++);

        registry.Tick(errors.Add);
        registry.Tick(errors.Add);

        Assert.Equal(1, successful);
        Assert.Same(failure, Assert.Single(errors));
    }
}
