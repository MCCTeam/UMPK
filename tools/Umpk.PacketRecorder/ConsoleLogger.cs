using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Umpk.PacketRecorder;

/// <summary>A minimal single-line <see cref="ILogger"/> that writes to the console, used by the recorder tool. The tool project is allowed to use <see cref="Console"/>; keeping this local avoids a new package reference on Microsoft.Extensions.Logging.Console.</summary>
internal sealed class ConsoleLogger : ILogger
{
    private readonly LogLevel _minimum;

    public ConsoleLogger(LogLevel minimum) => _minimum = minimum;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum && logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        ArgumentNullException.ThrowIfNull(formatter);
        string message = formatter(state, exception);
        TextWriter writer = logLevel >= LogLevel.Error ? Console.Error : Console.Out;
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[{DateTime.UtcNow:HH:mm:ss}] {logLevel,-11} {message}"));
        if (exception is not null)
            writer.WriteLine(exception);

    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
