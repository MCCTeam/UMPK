using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Umpk.Auth.Tests.Fakes;

/// <summary>An <see cref="ILogger"/> that records every formatted message and every exception's text so tests can assert that secrets never reach the logging boundary.</summary>
public sealed class CapturingLogger : ILogger
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    /// <summary>The concatenation of every logged message plus exception text, for substring assertions.</summary>
    public string AllText => string.Join("\n", _lines);

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        _lines.Enqueue(message);
        if (exception is not null)
            _lines.Enqueue(exception.ToString());

    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
