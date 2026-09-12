using System.Buffers;
using System.Text;
using Umpk.Client.Plugins;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="UmpkClient.Channels"/>, driven through a real session: registration before the dial, the configuration-phase payload route and the server's announced channel set.</summary>
public sealed class ClientChannelsTests
{
    private static readonly Identifier Early = new("umpk", "early");
    private static readonly Identifier ConfigChannel = new("umpk", "config");
    private static readonly Identifier RegisterChannel = Identifier.Minecraft("register");
    private static readonly Identifier UnregisterChannel = Identifier.Minecraft("unregister");

    /// <summary>The whole point of the pre-dial surface: a handler registered while the client is Created is in place for the session's FIRST play frame. Today's per-plugin path cannot cover that window at all, because <c>IClientPlugin.Attach</c> does not run until play has already begun.</summary>
    [Fact]
    public async Task ARegistrationMadeBeforeTheDial_GetsTheFirstPlayMessage()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Equal(ClientStatus.Created, client.Status);
        using PluginChannelRegistration registration =
            client.Channels.RegisterPlay(Early, data => received.TrySetResult(data.ToArray()));

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await PluginSessionHarness.DriveLoginAsync(server, ct);

        // Queued as the very first PLAY frame, before the client's receive loop has even started.
        await SendPlayPayloadAsync(server, Early, [0xDE, 0xAD], ct);
        await connect.WaitAsync(PluginSessionHarness.Budget, ct);

        Assert.Equal(new byte[] { 0xDE, 0xAD }, await received.Task.WaitAsync(PluginSessionHarness.Budget, ct));
    }

    /// <summary>A pre-dial registration cannot announce itself when it is made (there is no connection), so the announce is deferred to the moment play begins. Asserted on the frame, because a channel the server was never told about is exactly the failure this defers around.</summary>
    [Fact]
    public async Task ARegistrationMadeBeforeTheDial_IsAnnouncedWhenPlayBegins()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        using PluginChannelRegistration registration = client.Channels.RegisterPlay(Early, _ => { });

        await PluginSessionHarness.JoinAsync(client, server, ct);

        (Identifier channel, byte[] body) = await ReadPlayPayloadAsync(server, ct);
        Assert.Equal(RegisterChannel, channel);
        Assert.Equal("umpk:early", Encoding.UTF8.GetString(body));
    }

    /// <summary>A handler registered before the dial receives configuration payloads, which is the only way a proxy handshake or a mod loader's configuration traffic can be answered at all.</summary>
    [Fact]
    public async Task AConfigurationPayload_ReachesAConfigurationHandler()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using PluginChannelRegistration registration =
            client.Channels.RegisterConfiguration(ConfigChannel, data => received.TrySetResult(data.ToArray()));
        Assert.True(client.Channels.CanSendConfiguration);

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await DriveLoginWithConfigurationPayloadAsync(
            server, new ClientboundConfigCustomPayloadPacket(ConfigChannel, [0x01, 0x02, 0x03]), ct);
        await connect.WaitAsync(PluginSessionHarness.Budget, ct);

        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, await received.Task.WaitAsync(PluginSessionHarness.Budget, ct));
    }

    /// <summary>The brand keeps being captured into session state, and is now ALSO deliverable to a handler. The capture is the behaviour that must not move; the delivery is the new uniformity.</summary>
    [Fact]
    public async Task TheServerBrand_IsStillCaptured_AndAlsoRouted()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using PluginChannelRegistration registration = client.Channels.RegisterConfiguration(
            Identifier.Minecraft("brand"), data => received.TrySetResult(data.ToArray()));

        var brandBody = new ArrayBufferWriter<byte>();
        var brandWriter = new PacketWriter(brandBody);
        brandWriter.WriteString("Paper");

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);
        await DriveLoginWithConfigurationPayloadAsync(
            server,
            new ClientboundConfigCustomPayloadPacket(Identifier.Minecraft("brand"), brandBody.WrittenSpan.ToArray()),
            ct);
        await connect.WaitAsync(PluginSessionHarness.Budget, ct);

        Assert.Equal("Paper", client.State.Server.Brand);
        await received.Task.WaitAsync(PluginSessionHarness.Budget, ct);
    }

    /// <summary>A configuration handler can answer on the wire while the phase is still open.</summary>
    [Fact]
    public async Task SendConfigurationAsync_PutsAServerboundConfigurationPayloadOnTheWire()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        using PluginChannelRegistration registration = client.Channels.RegisterConfiguration(
            ConfigChannel,
            // The handler is synchronous by contract, so the answer is started here and observed by the test reading the frame, rather than awaited.
            data =>
            {
                _ = data;
                _ = client.Channels.SendConfigurationAsync(ConfigChannel, new byte[] { 0x42 }, CancellationToken.None);
            });

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await PluginSessionHarness.SendAsync(
            server, ProtocolPhase.Configuration,
            new ClientboundConfigCustomPayloadPacket(ConfigChannel, [0x00]), ct);

        InboundFrame answer = await server.NextFrameAsync(ct);
        var answered = Assert.IsType<ServerboundConfigCustomPayloadPacket>(
            Decode(ProtocolPhase.Configuration, answer));
        Assert.Equal(ConfigChannel, answered.Channel);
        Assert.Equal(new byte[] { 0x42 }, answered.Data);

        await PluginSessionHarness.SendAsync(
            server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await ScriptedServer.SendPlayReadinessFrameAsync(server, PluginSessionHarness.Version.Protocol, ct);
        await connect.WaitAsync(PluginSessionHarness.Budget, ct);
    }

    /// <summary><c>minecraft:register</c> is the server telling the client which channels its plugins speak. 1.x kept this list and bots used it to detect a server plugin before speaking to it; UMPK parsed it nowhere.</summary>
    [Fact]
    public async Task AServerRegisterAnnouncement_PopulatesServerAnnounced()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        var changes = new List<ServerAnnouncedChannelsChangedEventArgs>();
        var announced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Channels.ServerAnnouncedChanged += (_, args) =>
        {
            changes.Add(args);
            announced.TrySetResult();
        };

        Assert.Empty(client.Channels.ServerAnnounced);
        await PluginSessionHarness.JoinAsync(client, server, ct);

        await SendPlayPayloadAsync(
            server, RegisterChannel, Encoding.UTF8.GetBytes("umpk:one\0umpk:two"), ct);
        await announced.Task.WaitAsync(PluginSessionHarness.Budget, ct);

        Assert.Equal(
            [new Identifier("umpk", "one"), new Identifier("umpk", "two")],
            client.Channels.ServerAnnounced.OrderBy(i => i.Path, StringComparer.Ordinal));
        ServerAnnouncedChannelsChangedEventArgs first = Assert.Single(changes);
        Assert.Equal([new Identifier("umpk", "one"), new Identifier("umpk", "two")], first.Added);
        Assert.Empty(first.Removed);
    }

    [Fact]
    public async Task AServerUnregisterAnnouncement_RemovesFromServerAnnounced()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        var changes = new List<ServerAnnouncedChannelsChangedEventArgs>();
        client.Channels.ServerAnnouncedChanged += (_, args) => changes.Add(args);

        await PluginSessionHarness.JoinAsync(client, server, ct);

        await SendPlayPayloadAsync(server, RegisterChannel, Encoding.UTF8.GetBytes("umpk:one\0umpk:two"), ct);
        await WaitForAsync(() => client.Channels.ServerAnnounced.Count == 2, ct);

        await SendPlayPayloadAsync(server, UnregisterChannel, Encoding.UTF8.GetBytes("umpk:one"), ct);
        await WaitForAsync(() => client.Channels.ServerAnnounced.Count == 1, ct);

        Assert.Equal([new Identifier("umpk", "two")], client.Channels.ServerAnnounced);
        Assert.Equal([new Identifier("umpk", "one")], changes[^1].Removed);
    }

    /// <summary>The pre-1.13 spelling. <c>REGISTER</c> is not a valid identifier, so the check that sees it has to be on the raw channel name; a purely identifier-keyed one would never fire.</summary>
    [Fact]
    public async Task TheLegacyRegisterSpelling_IsAlsoRead()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        await PluginSessionHarness.JoinAsync(client, server, ct);

        await SendPlayPayloadRawAsync(server, "REGISTER", Encoding.UTF8.GetBytes("umpk:legacy"), ct);
        await WaitForAsync(() => client.Channels.ServerAnnounced.Count == 1, ct);

        Assert.Equal([new Identifier("umpk", "legacy")], client.Channels.ServerAnnounced);
    }

    /// <summary>The set described the server that just went away, so it does not survive the session.</summary>
    [Fact]
    public async Task ServerAnnounced_IsClearedWhenTheSessionEnds()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        await PluginSessionHarness.JoinAsync(client, server, ct);
        await SendPlayPayloadAsync(server, RegisterChannel, Encoding.UTF8.GetBytes("umpk:one"), ct);
        await WaitForAsync(() => client.Channels.ServerAnnounced.Count == 1, ct);

        await client.DisconnectAsync();

        Assert.Empty(client.Channels.ServerAnnounced);
    }

    /// <summary>Announcing something already announced is not a change, so it raises nothing. A consumer that rebuilds state on the event should not be woken by a server repeating itself.</summary>
    [Fact]
    public async Task ARepeatedAnnouncement_RaisesNothing()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        CancellationToken ct = cts.Token;
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        int raised = 0;
        client.Channels.ServerAnnouncedChanged += (_, _) => Interlocked.Increment(ref raised);

        await PluginSessionHarness.JoinAsync(client, server, ct);
        await SendPlayPayloadAsync(server, RegisterChannel, Encoding.UTF8.GetBytes("umpk:one"), ct);
        await WaitForAsync(() => Volatile.Read(ref raised) == 1, ct);

        await SendPlayPayloadAsync(server, RegisterChannel, Encoding.UTF8.GetBytes("umpk:one"), ct);
        await SendPlayPayloadAsync(server, Early, [0x01], ct);
        await WaitForAsync(() => client.Channels.ServerAnnounced.Count == 1, ct);

        Assert.Equal(1, Volatile.Read(ref raised));
    }

    /// <summary>Drives login and configuration, injecting <paramref name="payload"/> after the client's configuration announce and before <c>finish_configuration</c>.</summary>
    private static async Task DriveLoginWithConfigurationPayloadAsync(
        FakeJavaServer server, ClientboundConfigCustomPayloadPacket payload, CancellationToken ct)
    {
        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Configuration, payload, ct);
        await PluginSessionHarness.SendAsync(
            server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await ScriptedServer.SendPlayReadinessFrameAsync(server, PluginSessionHarness.Version.Protocol, ct);
    }

    /// <summary>Sends a clientbound PLAY <c>custom_payload</c>. It has no packet record on any protocol (the client routes it as a raw frame by design), so the frame is built by hand: length-prefixed channel name, then the body.</summary>
    private static Task SendPlayPayloadAsync(
        FakeJavaServer server, Identifier channel, byte[] data, CancellationToken ct)
        => SendPlayPayloadRawAsync(server, channel.ToString(), data, ct);

    private static async Task SendPlayPayloadRawAsync(
        FakeJavaServer server, string channel, byte[] data, CancellationToken ct)
    {
        var body = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(body);
        writer.WriteString(channel);
        writer.WriteBytes(data);
        await server.SendFrameAsync(PlayCustomPayloadWireId(PacketFlow.Clientbound), body.WrittenSpan.ToArray(), ct);
    }

    private static async Task<(Identifier Channel, byte[] Body)> ReadPlayPayloadAsync(
        FakeJavaServer server, CancellationToken ct)
    {
        InboundFrame frame = await server.NextFrameAsync(ct);
        Assert.Equal(PlayCustomPayloadWireId(PacketFlow.Serverbound), frame.WireId);
        var reader = new PacketReader(frame.Payload);
        string name = reader.ReadString();
        Assert.True(Identifier.TryParse(name, out Identifier channel), name);
        return (channel, reader.ReadRemaining().ToArray());
    }

    private static object Decode(ProtocolPhase phase, InboundFrame frame)
    {
        ProtocolDescriptor descriptor = PluginSessionHarness.Version.Protocol;
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Serverbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetInbound(frame.WireId, out BoundPacketCodec codec));
        return codec.Decode(frame.Payload, PacketCodecContext.Registryless);
    }

    private static int PlayCustomPayloadWireId(PacketFlow flow)
    {
        ProtocolDescriptor descriptor = PluginSessionHarness.Version.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, flow, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == Identifier.Minecraft("custom_payload"))
                return wireId;

        throw new Xunit.Sdk.XunitException($"custom_payload is not registered {flow} in play.");
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }
}
