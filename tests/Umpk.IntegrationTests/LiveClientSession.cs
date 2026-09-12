using System.IO.Pipelines;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;

namespace Umpk.IntegrationTests;

/// <summary>A thin live-client helper for the integration harness: connects to a local server, logs in offline via <see cref="JavaClientLogin"/>, and hands back the bound <see cref="JavaConnection"/> in the play phase. Callers drive the play-phase read loop themselves (keep-alive echo, chunk capture). Disposal closes the connection.</summary>
public sealed class LiveClientSession : IAsyncDisposable
{
    private LiveClientSession(JavaConnection connection, LoginResult login)
    {
        Connection = connection;
        Login = login;
    }

    /// <summary>The bound connection, in the play phase.</summary>
    public JavaConnection Connection { get; }

    /// <summary>The login outcome (uuid, username, phase).</summary>
    public LoginResult Login { get; }

    /// <summary>Connects and logs in, reaching the play phase.</summary>
    public static async Task<LiveClientSession> ConnectAsync(
        JavaVersion version, string host, int port, string username, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);
        var endpoint = new ServerEndpoint(host, (ushort)port);
        IDuplexPipe pipe = await TcpConnectionFactory.Shared.ConnectAsync(endpoint, ct).ConfigureAwait(false);

        var options = new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.FromSeconds(90),
        };
        var connection = new JavaConnection(pipe, options);
        connection.BindCodec(new DescriptorFrameCodecBinding(version.Protocol), PacketFlow.Clientbound);
        // Frame mode: never decode in the read loop, so a not-yet-robust codec (e.g. the level_chunk decoder on real data) cannot fault the read loop and tear the connection down mid-pump. The
        // login driver reads and decodes the handful of login/config packets it needs at frame level;
        // the play-phase pump reads raw frames and decodes chunks on demand in the test.
        connection.SetDecodeFilter(PacketDecodeFilter.None);
        connection.Start();

        var loginOptions = new JavaLoginOptions
        {
            Username = username,
            ServerHost = host,
            ServerPort = (ushort)port,
        };

        LoginResult login = await JavaClientLogin.LoginAsync(connection, version, loginOptions, ct).ConfigureAwait(false);
        return new LiveClientSession(connection, login);
    }

    /// <summary>Reads play-phase frames for <paramref name="duration"/>, echoing keep-alives so the server keeps the connection. Invokes <paramref name="onFrame"/> for every frame observed.</summary>
    public async Task PumpPlayAsync(
        JavaVersion version, TimeSpan duration, Action<InboundFrame> onFrame, CancellationToken ct)
    {
        ProtocolDescriptor descriptor = version.Protocol;
        PhaseRegistry playIn = descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        PhaseRegistry playOut = descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        int keepAliveInId = WireIdOf(playIn, Identifier.Minecraft("keep_alive"));
        int keepAliveOutId = WireIdOf(playOut, Identifier.Minecraft("keep_alive"));

        using var durationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        durationCts.CancelAfter(duration);

        // Hard wall-clock guard: the pump must never outlive the window even if a transport read blocks uncancellably on a pathological frame (defensive across all protocols). We race the read loop against a timer and abandon the loop when the window closes.
        Task pump = PumpLoopAsync(onFrame, keepAliveInId, keepAliveOutId, durationCts.Token);
        Task guard = Task.Delay(duration + TimeSpan.FromSeconds(10), ct);
        Task done = await Task.WhenAny(pump, guard).ConfigureAwait(false);
        if (done == guard)
            await durationCts.CancelAsync().ConfigureAwait(false);

        try
        {
            await pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (durationCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // duration elapsed
        }
    }

    private async Task PumpLoopAsync(
        Action<InboundFrame> onFrame, int keepAliveInId, int keepAliveOutId, CancellationToken ct)
    {
        await foreach (InboundFrame frame in Connection.ReceiveFramesAsync(ct).ConfigureAwait(false))
        {
            onFrame(frame);
            if (frame.WireId == keepAliveInId && keepAliveInId >= 0 && keepAliveOutId >= 0)
                await Connection.SendFrameAsync(keepAliveOutId, frame.CopyPayload(), ct).ConfigureAwait(false);

        }
    }

    private static int WireIdOf(PhaseRegistry registry, Identifier id)
    {
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id)
                return wireId;

        return -1;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Connection.CloseAsync(CloseReason.Local, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // best effort
        }

        await Connection.DisposeAsync().ConfigureAwait(false);
    }
}
