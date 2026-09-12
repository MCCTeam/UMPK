using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The session-loop watchdog contract is exercised. A handler that exceeds the configured threshold produces a warning through the logging pipeline; a fast handler stays silent. These tests construct the bus with a nonzero watchdog interval so both outcomes are observable.</summary>
public sealed class WatchdogTests
{
    [Fact]
    public async Task Watchdog_Logs_Warning_When_Handler_Exceeds_Threshold()
    {
        var logger = new CapturingLogger();
        var bus = new EventBus(logger, TimeSpan.FromMilliseconds(15), 8);
        bus.Subscribe<Tick>(_ => Thread.Sleep(120)); // a stalled handler

        await bus.PublishAsync(new Tick());

        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("watchdog") && e.Message.Contains(nameof(Tick)));
    }

    [Fact]
    public async Task Watchdog_Silent_When_Handler_Is_Fast()
    {
        var logger = new CapturingLogger();
        var bus = new EventBus(logger, TimeSpan.FromMilliseconds(500), 8);
        bus.Subscribe<Tick>(_ => { });

        await bus.PublishAsync(new Tick());

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private sealed record Tick : IClientEvent;

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
