using Umpk.Events;
using Xunit;

namespace Umpk.Tests.Events;

public class SubscriptionListTests
{
    [Fact]
    public void Invoke_CallsHandlersInSubscriptionOrder()
    {
        var list = new SubscriptionList<int>();
        var order = new List<string>();
        list.Subscribe(_ => order.Add("first"));
        list.Subscribe(_ => order.Add("second"));
        list.Subscribe(_ => order.Add("third"));

        list.Invoke(42);

        Assert.Equal(["first", "second", "third"], order);
    }

    [Fact]
    public void Dispose_Unsubscribes_AndIsIdempotent()
    {
        var list = new SubscriptionList<int>();
        int calls = 0;
        var token = list.Subscribe(_ => calls++);
        Assert.Equal(1, list.Count);

        list.Invoke(1);
        token.Dispose();
        token.Dispose();
        list.Invoke(2);

        Assert.Equal(1, calls);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void Invoke_IsolatesThrowingHandlers()
    {
        var faults = new List<(Exception Exception, Delegate Handler)>();
        var list = new SubscriptionList<int>((ex, handler) => faults.Add((ex, handler)));
        int reached = 0;
        list.Subscribe(_ => throw new InvalidOperationException("boom"));
        list.Subscribe(_ => reached++);

        list.Invoke(1);

        Assert.Equal(1, reached);
        var fault = Assert.Single(faults);
        Assert.IsType<InvalidOperationException>(fault.Exception);
    }

    [Fact]
    public void Invoke_WithoutErrorSink_SwallowsAfterIsolation()
    {
        var list = new SubscriptionList<int>();
        int reached = 0;
        list.Subscribe(_ => throw new InvalidOperationException());
        list.Subscribe(_ => reached++);

        list.Invoke(1);

        Assert.Equal(1, reached);
    }

    [Fact]
    public void MutationDuringInvoke_TakesEffectNextInvoke()
    {
        var list = new SubscriptionList<int>();
        int lateCalls = 0;
        list.Subscribe(_ => list.Subscribe(_ => lateCalls++));

        list.Invoke(1);
        Assert.Equal(0, lateCalls);

        list.Invoke(2);
        Assert.Equal(1, lateCalls);
    }
}
