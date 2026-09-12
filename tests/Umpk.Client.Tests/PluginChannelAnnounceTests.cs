using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Umpk.Client.Internal;
using Umpk.Client.Plugins;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The <c>minecraft:register</c>/<c>minecraft:unregister</c> announce is started from synchronous <c>Register</c>/<c>Unregister</c> calls, so it cannot be awaited by the caller. Failures must therefore be reported; otherwise an unsent registration is indistinguishable from a channel the server ignored.</summary>
public sealed class PluginChannelAnnounceTests
{
    private static readonly Identifier Channel = new("umpk", "test_channel");

    [Fact]
    public async Task AFailingRegisterAnnounce_IsReported()
    {
        (PluginChannelManager channels, ConcurrentQueue<string> logs) = Manager();

        // The announce gate: a registration made before play is deferred to OnPlayStarted, so a test that wants the immediate announce has to open the gate first, exactly as ConnectAsync does.
        channels.OnPlayStarted(CancellationToken.None);
        using PluginChannelRegistration registration =
            channels.Register(ProtocolPhase.Play, Channel, _ => { }, CancellationToken.None);

        await WaitForAsync(() => logs.Any(e => e.Contains("register announce for umpk:test_channel failed", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AFailingUnregisterAnnounce_IsReported()
    {
        (PluginChannelManager channels, ConcurrentQueue<string> logs) = Manager();

        channels.OnPlayStarted(CancellationToken.None);
        PluginChannelRegistration registration =
            channels.Register(ProtocolPhase.Play, Channel, _ => { }, CancellationToken.None);
        registration.Dispose();

        await WaitForAsync(() => logs.Any(e => e.Contains("unregister announce for umpk:test_channel failed", StringComparison.Ordinal)));
    }

    private static (PluginChannelManager Channels, ConcurrentQueue<string> Logs) Manager()
    {
        JavaVersion version = JavaVersions.V1_21_11;
        var logs = new ConcurrentQueue<string>();
        var manager = new PluginChannelManager(
            new ThrowingSink(),
            new WireIndex(version),
            version.Version.Protocol,
            new Recorder(logs));
        return (manager, logs);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, cts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Fails ASYNCHRONOUSLY, after a yield, which is the case a discarded task swallows completely: a synchronous throw would at least have escaped into the caller.</summary>
    private sealed class ThrowingSink : IPacketSink
    {
        public ValueTask SendAsync(object packet, CancellationToken ct) => FailAsync();

        public ValueTask SendFrameAsync(int wireId, ReadOnlyMemory<byte> payload, CancellationToken ct) => FailAsync();

        private static async ValueTask FailAsync()
        {
            await Task.Yield();
            throw new ConnectionClosedException(CloseReason.SocketEof, "peer went away");
        }
    }

    private sealed class Recorder(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            entries.Enqueue($"{logLevel}: {formatter(state, exception)}");
        }
    }
}
