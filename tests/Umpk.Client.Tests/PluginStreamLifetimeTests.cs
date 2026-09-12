using Umpk.Client.Events;
using Umpk.Client.Plugins;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// A plugin's <see cref="ClientEvents.Stream{TEvent}(System.Threading.CancellationToken)"/> now ends when the plugin does, not only when the caller's own token fires.
/// <para>The stream must use the <c>Register</c> hook rather than going straight to the event bus, so <see cref="TrackingClientEvents"/> uses to auto-release everything else a plugin acquires. A plugin that streamed with <see cref="CancellationToken.None"/> (or any token it did not itself cancel on unload) kept a live enumerator, and the delegate closures the compiler generates for an <c>await foreach</c> body, running forever: past <c>RemoveAsync</c>, past a clean disconnect, past anything short of the process exiting. <see cref="Stream_Ends_WhenTheCallerCancelsFirst"/> is the control: the caller's own cancellation must still work.</para>
/// </summary>
public sealed class PluginStreamLifetimeTests
{
    private static readonly TimeSpan Budget = PluginSessionHarness.Budget;

    [Fact]
    public async Task Stream_Ends_WhenThePluginIsRemoved()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var plugin = new StreamingPlugin(CancellationToken.None);
        await using UmpkClient client = PluginSessionHarness.BuildClient(server, b => b.AddPlugin(plugin));
        await PluginSessionHarness.JoinAsync(client, server, ct);

        await AwaitStreamIsLiveAsync(server, plugin, ct);

        Assert.True(await client.Plugins.RemoveAsync(plugin.Id, ct));

        await plugin.Pump!.WaitAsync(Budget, ct);
        Assert.True(plugin.StreamEnded);
        Assert.True(plugin.StreamEndedByCancellation);
    }

    [Fact]
    public async Task Stream_Ends_WhenTheSessionEnds()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var plugin = new StreamingPlugin(CancellationToken.None);
        await using UmpkClient client = PluginSessionHarness.BuildClient(server, b => b.AddPlugin(plugin));
        await PluginSessionHarness.JoinAsync(client, server, ct);

        await AwaitStreamIsLiveAsync(server, plugin, ct);

        await client.DisconnectAsync(ct);

        await plugin.Pump!.WaitAsync(Budget, ct);
        Assert.True(plugin.StreamEnded);
        Assert.True(plugin.StreamEndedByCancellation);
    }

    [Fact]
    public async Task Stream_Ends_WhenTheCallerCancelsFirst()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        using var callerCts = new CancellationTokenSource();
        await using FakeJavaServer server = FakeJavaServer.Create();
        var plugin = new StreamingPlugin(callerCts.Token);
        await using UmpkClient client = PluginSessionHarness.BuildClient(server, b => b.AddPlugin(plugin));
        await PluginSessionHarness.JoinAsync(client, server, ct);

        await AwaitStreamIsLiveAsync(server, plugin, ct);

        // The plugin is never removed and the session never ends; only the caller's own token fires.
        await callerCts.CancelAsync();

        await plugin.Pump!.WaitAsync(Budget, ct);
        Assert.True(plugin.StreamEnded);
        Assert.True(plugin.StreamEndedByCancellation);

        // Control assertion: the plugin itself is still live and its Detached token is untouched, so the stream really did end from the caller's own token, not from a detach this test never triggered.
        Assert.True(client.Plugins.Contains(plugin.Id));
        Assert.False(plugin.Context!.Detached.IsCancellationRequested);
    }

    /// <summary>Sends one keep-alive and waits for the plugin's stream to observe it, proving it is live.</summary>
    private static async Task AwaitStreamIsLiveAsync(FakeJavaServer server, StreamingPlugin plugin, CancellationToken ct)
    {
        int before = plugin.EventCount;
        for (int attempt = 0; attempt < 100 && plugin.EventCount <= before; attempt++)
        {
            await PluginSessionHarness.SendAsync(server, ProtocolPhase.Play, new ClientboundPlayKeepAlivePacket(attempt), ct);
            await Task.Delay(25, ct);
        }

        Assert.True(plugin.EventCount > before, "the stream never observed a single event; it is not live.");
    }

    /// <summary>A plugin that pumps a <see cref="PacketReceived"/> stream on its own background task.</summary>
    private sealed class StreamingPlugin(CancellationToken streamToken) : IClientPlugin
    {
        public string Id => "streaming";

        public ClientPluginContext? Context { get; private set; }

        public Task? Pump { get; private set; }

        public int EventCount;

        public bool StreamEnded;

        public bool StreamEndedByCancellation;

        public void Attach(ClientPluginContext context)
        {
            Context = context;
            Pump = PumpAsync(context);
        }

        private async Task PumpAsync(ClientPluginContext context)
        {
            try
            {
                await foreach (PacketReceived e in context.Events.Stream<PacketReceived>(streamToken))
                {
                    _ = e;
                    Interlocked.Increment(ref EventCount);
                }

                StreamEnded = true;
            }
            catch (OperationCanceledException)
            {
                StreamEndedByCancellation = true;
                StreamEnded = true;
            }
        }
    }
}
