using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Umpk.IntegrationTests;

/// <summary>A minimal in-memory <see cref="ILoggerFactory"/> for diagnosing live legs: it keeps the most recent log entries (warning/error level and above by default) in a ring buffer that a failing leg can dump.</summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly int _capacity;
    private readonly LogLevel _minLevel;

    public CapturingLoggerFactory(LogLevel minLevel = LogLevel.Debug, int capacity = 200)
    {
        _minLevel = minLevel;
        _capacity = capacity;
    }

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public string Dump(int last = 40)
    {
        string[] all = _entries.ToArray();
        int start = Math.Max(0, all.Length - last);
        return string.Join("\n", all[start..]);
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private void Add(string line)
    {
        _entries.Enqueue(line);
        while (_entries.Count > _capacity && _entries.TryDequeue(out _))
        {
        }
    }

    private sealed class Logger(CapturingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= owner._minLevel;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            string msg = formatter(state, exception);
            string ex = exception is null ? string.Empty : $" | {exception.GetType().Name}: {exception.Message}";
            owner.Add($"[{logLevel}] {category}: {msg}{ex}");
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
