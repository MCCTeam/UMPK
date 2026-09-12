using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>Verifies that the natively published <c>samples/MinimalBot</c> decodes the oldest and newest supported protocol frames, then logs in and chats over a loopback socket.</summary>
/// <remarks>Packet descriptors and codecs are discovered through data and can otherwise be removed by native trimming. The socket bridge copies bytes without interpreting them, leaving framing, compression, and phase transitions to the normal client and server paths.</remarks>
public sealed class AotPublishSmokeTests
{
    private const int SessionProtocol = 776;

    [AotSmokeFact]
    public async Task PublishedBot_DecodesBothWireLayoutsAndJoinsTheFakeServer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        string binary = Environment.GetEnvironmentVariable("UMPK_AOT_BINARY")!;
        Assert.True(File.Exists(binary), $"UMPK_AOT_BINARY names {binary}, which does not exist.");

        string oldest = await FrameArgumentAsync(47, cts.Token);
        string newest = await FrameArgumentAsync(SessionProtocol, cts.Token);

        await using FakeJavaServer server = FakeJavaServer.Create();
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        int port = ((IPEndPoint)listener.LocalEndPoint!).Port;

        using var bot = new Process
        {
            StartInfo = new ProcessStartInfo(binary)
            {
                ArgumentList =
                {
                    "--aot-smoke",
                    $"127.0.0.1:{port}",
                    SessionProtocol.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    oldest,
                    newest,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        Assert.True(bot.Start(), $"{binary} did not start.");
        Task<string> outputTask = bot.StandardOutput.ReadToEndAsync();
        Task<string> errorsTask = bot.StandardError.ReadToEndAsync();
        Task bridge = BridgeAsync(listener, server.ClientPipe, cts.Token);
        Task session = DriveToPlayThenExpectChatAsync(server, cts.Token);
        Exception? primaryFailure = null;

        try
        {
            await bot.WaitForExitAsync(cts.Token);
            string output = await outputTask;
            string errors = await errorsTask;

            // Asserted before the server script is awaited: a bot that died says why here, where a wait on the script would only say that no frame ever arrived.
            Assert.True(bot.ExitCode == 0, $"exit {bot.ExitCode}. stdout:\n{output}\nstderr:\n{errors}");
            Assert.Contains("decoded protocol=47 ", output, StringComparison.Ordinal);
            Assert.Contains($"decoded protocol={SessionProtocol} ", output, StringComparison.Ordinal);
            Assert.Contains("joined ", output, StringComparison.Ordinal);
            await session;
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            throw;
        }
        finally
        {
            // Attempt every cleanup step, even if another failed. Preserve the original test failure;
            // on an otherwise successful run, unexpected cleanup failures still fail the test.
            List<Exception> cleanupErrors = [];
            await ObserveCleanupAsync(cts.CancelAsync, cleanupErrors);
            await ObserveCleanupAsync(() => StopProcessAsync(bot), cleanupErrors);
            await ObserveCleanupAsync(
                () => Task.WhenAll(outputTask, errorsTask, SwallowAsync(session), SwallowAsync(bridge))
                    .WaitAsync(TimeSpan.FromSeconds(10)), cleanupErrors);

            if (primaryFailure is null && cleanupErrors.Count > 0)
                throw new AggregateException("Native smoke cleanup failed.", cleanupErrors);

        }
    }

    /// <summary>Picks a committed clientbound play frame this protocol binds an implemented codec to, and renders it as the <c>protocol/phase/wireId/hexBody</c> argument the bot decodes.</summary>
    private static async Task<string> FrameArgumentAsync(int protocol, CancellationToken ct)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        Assert.True(version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry play));

        foreach (string capture in CorpusLoader
            .DiscoverCaptures(Path.Combine(FixturePaths.CorpusRoot, protocol.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Order(StringComparer.Ordinal))
        {
            LoadedCorpus corpus = await CorpusLoader.LoadFileAsync(capture, ct);
            foreach (RecordedFrame frame in corpus.Frames)
            {
                // A small body keeps the argument inside every platform's command-line limit, and the frame still exercises the whole resolve-and-decode path.
                if (frame.Direction != CorpusDirection.Clientbound
                    || frame.Phase != CorpusPhase.Play
                    || frame.Body.Length is 0 or > 96
                    || !play.TryGetInbound(frame.WireId, out BoundPacketCodec codec)
                    || !codec.IsImplemented)
                    continue;

                return $"{protocol}/{ProtocolPhase.Play}/{frame.WireId}/{Convert.ToHexString(frame.Body)}";
            }
        }

        throw new Xunit.Sdk.XunitException($"No small implemented clientbound play frame in the protocol {protocol} corpus.");
    }

    /// <summary>Copies bytes between one accepted socket and the fake server's client-side pipe.</summary>
    private static async Task BridgeAsync(Socket listener, IDuplexPipe pipe, CancellationToken ct)
    {
        using Socket accepted = await listener.AcceptAsync(ct);
        accepted.NoDelay = true;
        using var stream = new NetworkStream(accepted, ownsSocket: false);
        using var bridgeStopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task inbound = stream.CopyToAsync(pipe.Output, bridgeStopping.Token);
        Task outbound = CopyAsync(pipe.Input, stream, bridgeStopping.Token);
        try
        {
            Task completed = await Task.WhenAny(inbound, outbound);
            await completed;
        }
        finally
        {
            await bridgeStopping.CancelAsync();
            await Task.WhenAll(SwallowAsync(inbound), SwallowAsync(outbound));
        }
    }

    private static async Task CopyAsync(PipeReader reader, Stream stream, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ReadResult read = await reader.ReadAsync(ct);
            foreach (ReadOnlyMemory<byte> segment in read.Buffer)
                await stream.WriteAsync(segment, ct);

            await stream.FlushAsync(ct);
            reader.AdvanceTo(read.Buffer.End);
            if (read.IsCompleted)
                return;

        }
    }

    /// <summary>Serves handshake, login and configuration, then waits for the bot's chat message.</summary>
    private static async Task DriveToPlayThenExpectChatAsync(FakeJavaServer server, CancellationToken ct)
    {
        Assert.True(JavaVersions.TryGetByProtocol(SessionProtocol, out JavaVersion version));
        ProtocolDescriptor descriptor = version.Protocol;

        await server.NextFrameAsync(ct); // intention
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "AotSmoke", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);

        // The client opens play senders only after the server's first play frame crosses the wire.
        await SendAsync(server, descriptor, ProtocolPhase.Play,
            new ClientboundSetTimePacket(GameTime: 0, DayTime: 0, TickDayTime: false, ClockUpdates: []), ct);

        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry play));
        InboundFrame chat = await server.NextFrameAsync(ct);
        Assert.True(play.TryGetInbound(chat.WireId, out BoundPacketCodec codec));
        Assert.Equal("minecraft:chat", codec.DatasetIdentifier.ToString());
        var message = Assert.IsType<ServerboundSignedChatPacket>(codec.Decode(chat.Payload, PacketCodecContext.Registryless));
        Assert.Equal("aot-smoke", message.Message);
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // A natural exit raced with Kill. It is already in the state we need.
        }

        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task ObserveCleanupAsync(Func<Task> cleanup, List<Exception> errors)
    {
        try
        {
            await cleanup();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolDescriptor descriptor, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private static async Task SwallowAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
    }
}
