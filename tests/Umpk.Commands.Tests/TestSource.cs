using System.Collections.Concurrent;
using Umpk.Commands;
using Umpk.Text;

namespace Umpk.Commands.Tests;

/// <summary>A minimal <see cref="ICommandSource"/> for tests: records replies and resolves a small service bag.</summary>
internal sealed class TestSource : ICommandSource
{
    private readonly Dictionary<Type, object> _services = new();

    public ConcurrentQueue<Component> Replies { get; } = new();

    public ValueTask ReplyAsync(Component message, CancellationToken cancellationToken = default)
    {
        Replies.Enqueue(message);
        return ValueTask.CompletedTask;
    }

    public T? GetService<T>() where T : class =>
        _services.TryGetValue(typeof(T), out var svc) ? (T)svc : null;

    public TestSource AddService<T>(T instance) where T : class
    {
        _services[typeof(T)] = instance;
        return this;
    }
}
