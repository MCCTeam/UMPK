using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Corpus;

namespace Umpk.PacketRecorder;

/// <summary>Records a packet corpus from the pre-decode frame view of a live offline-mode connection. Each record contains the decrypted, decompressed wire id and body with direction, phase, and timing.</summary>
/// <remarks>A client connection observes clientbound frames from its read loop. Serverbound handshake, login, and keep-alive frames are therefore outside a client-side capture.</remarks>
public sealed class LiveCorpusRecorder
{
    private readonly ILogger _logger;

    /// <summary>Creates a recorder with an optional logger.</summary>
    public LiveCorpusRecorder(ILogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Connects to <paramref name="endpoint"/>, logs in offline as <paramref name="username"/>, binds the version's codec so decode runs alongside the raw tap, records every observed frame for <paramref name="holdDuration"/> after reaching play, then returns the recorded frames.</summary>
    public async Task<IReadOnlyList<RecordedFrame>> RecordAsync(
        JavaVersion version,
        ServerEndpoint endpoint,
        string username,
        TimeSpan holdDuration,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(username);

        IDuplexPipe pipe = await TcpConnectionFactory.Shared.ConnectAsync(endpoint, ct).ConfigureAwait(false);
        var options = new JavaConnectionOptions
        {
            Logger = _logger,
            // A recorder must never abort on an unmapped frame: keep every byte, decoded or not.
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            // Play cadence has long gaps (keep-alive ~15s on modern); do not idle-timeout during a hold.
            ReadIdleTimeout = holdDuration + TimeSpan.FromSeconds(60),
        };

        await using var connection = new JavaConnection(pipe, options);
        // Pure frame mode: bind the descriptor so the compression and encryption gates still park the read loop, but install a decode filter that decodes NOTHING. A recorder must capture
        // pre-decode ground truth and must never fault on a codec error, so no packet is decoded here;
        // the raw payload of every frame is what gets recorded.
        //
        // The phase gate is deliberately NOT armed by this: it requires a decoded packet, and nothing decodes here. The login driver drives its own phase transitions instead, calling SetPhase from the frames it reads itself.
        var binding = new DescriptorFrameCodecBinding(version.Protocol);
        connection.BindCodec(binding, PacketFlow.Clientbound);
        connection.SetDecodeFilter(PacketDecodeFilter.None);

        using var recorder = new FrameRecorder();
        recorder.Attach(connection);

        connection.Start();

        var loginOptions = new JavaLoginOptions
        {
            Username = username,
            ServerHost = endpoint.Host,
            ServerPort = endpoint.Port,
        };

        LoginResult login = await JavaClientLogin.LoginAsync(connection, version, loginOptions, ct).ConfigureAwait(false);
        _logger.LogInformation("Reached {Phase} as {User} (uuid {Uuid}); holding {Hold}s.",
            login.Phase, login.Username, login.Uuid, holdDuration.TotalSeconds);

        // Keep the connection alive in the play phase: answer keep-alives so the server does not drop us, while the read loop keeps observing frames into the recorder.
        await HoldAndKeepAliveAsync(connection, version, holdDuration, ct).ConfigureAwait(false);

        await connection.CloseAsync(CloseReason.Local, ct).ConfigureAwait(false);
        return recorder.Snapshot();
    }

    private async Task HoldAndKeepAliveAsync(
        JavaConnection connection, JavaVersion version, TimeSpan hold, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = version.Protocol;
        PhaseRegistry playIn = descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        PhaseRegistry playOut = descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        int keepAliveInId = WireIdOf(playIn, Identifier.Minecraft("keep_alive"));
        int keepAliveOutId = WireIdOf(playOut, Identifier.Minecraft("keep_alive"));

        using var holdCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        holdCts.CancelAfter(hold);

        try
        {
            await foreach (InboundFrame frame in connection.ReceiveFramesAsync(holdCts.Token).ConfigureAwait(false))
            {
                if (frame.WireId == keepAliveInId && keepAliveInId >= 0 && keepAliveOutId >= 0)
                {
                    // Echo the keep-alive id back so the server keeps us connected. The play keep-alive id width matches the clientbound one for the version (long on modern, varint on 47).
                    await connection.SendFrameAsync(keepAliveOutId, frame.CopyPayload(), holdCts.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (holdCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Hold window elapsed; normal completion.
        }
    }

    private static int WireIdOf(PhaseRegistry registry, Identifier id)
    {
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id)
                return wireId;

        return -1;
    }
}
