using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;

namespace Umpk.TestKit.Server;

/// <summary>A scripted Minecraft server for tests. It owns one side of an in-memory <see cref="DuplexPipePair"/> and runs a real <see cref="JavaConnection"/> over it, so the client under test speaks to something that frames, compresses, and encrypts exactly like a real server. A <see cref="FakeServerScript"/> is an ordered list of expect/send steps; running it asserts the client's serverbound frames arrive in the scripted order and feeds back the scripted clientbound frames.</summary>
/// <remarks>The client side of the pair is exposed as <see cref="ClientPipe"/>; wrap it in the client's own <see cref="JavaConnection"/>. The fake reads serverbound frames in frame mode (no codec binding required), so it works without any codec binding. Client tests script realistic login/config/play exchanges here without a live server.</remarks>
public sealed class FakeJavaServer : IAsyncDisposable
{
    private readonly JavaConnection _serverConnection;
    private readonly DuplexPipePair _pair;

    private FakeJavaServer(DuplexPipePair pair, JavaConnection serverConnection, System.IO.Pipelines.IDuplexPipe clientPipe)
    {
        _pair = pair;
        _serverConnection = serverConnection;
        ClientPipe = clientPipe;
    }

    /// <summary>The client end of the in-memory link; wrap it in the client's own connection.</summary>
    public System.IO.Pipelines.IDuplexPipe ClientPipe { get; }

    /// <summary>The server side connection (frame mode, serverbound inbound flow).</summary>
    public JavaConnection ServerConnection => _serverConnection;

    /// <summary>Creates a fake server over a fresh in-memory pipe pair. The server side is started immediately in frame mode; the client side is returned via <see cref="ClientPipe"/>.</summary>
    public static FakeJavaServer Create(ILogger? logger = null)
    {
        DuplexPipePair pair = DuplexPipePair.Create();
        var options = new JavaConnectionOptions
        {
            Logger = logger ?? NullLogger.Instance,
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        };
        var server = new JavaConnection(pair.Left, options);
        server.BindCodec(null, PacketFlow.Serverbound);
        server.SetDecodeFilter(PacketDecodeFilter.None);
        server.Start();
        return new FakeJavaServer(pair, server, pair.Right);
    }

    /// <summary>Awaits the next serverbound frame the client sent.</summary>
    public async ValueTask<InboundFrame> NextFrameAsync(CancellationToken ct)
    {
        InboundItem item = await _serverConnection.ReceiveAsync(ct).ConfigureAwait(false);
        return item.Frame;
    }

    /// <summary>Sends a raw frame to the client.</summary>
    public ValueTask SendFrameAsync(int wireId, byte[] body, CancellationToken ct) =>
        _serverConnection.SendFrameAsync(wireId, body, ct);

    /// <summary>Enables compression on the server side (send a set-compression frame separately).</summary>
    public void EnableCompression(int threshold) => _serverConnection.EnableCompression(threshold);

    /// <summary>Runs the script to completion. Each expect step blocks (bounded by <paramref name="ct"/>) until a matching serverbound frame arrives; a mismatch throws <see cref="FakeServerScriptException"/> naming the step.</summary>
    public async Task RunAsync(FakeServerScript script, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(script);
        var context = new FakeServerContext(this, _serverConnection);

        for (int i = 0; i < script.Steps.Count; i++)
        {
            FakeServerStep step = script.Steps[i];
            switch (step)
            {
                case FakeServerStep.ExpectFrame expect:
                    InboundFrame frame = await NextFrameAsync(ct).ConfigureAwait(false);
                    if (!expect.Predicate(frame))
                        throw new FakeServerScriptException(
                            $"Step {i} expected {expect.Description} but received wireId=0x{frame.WireId:X2} body[{frame.Payload.Length}].");

                    break;

                case FakeServerStep.SendFrame send:
                    await _serverConnection.SendFrameAsync(send.WireId, send.Body, ct).ConfigureAwait(false);
                    break;

                case FakeServerStep.EnableCompression comp:
                    _serverConnection.EnableCompression(comp.Threshold);
                    break;

                case FakeServerStep.SetPhase phase:
                    _serverConnection.SetPhase(phase.Phase);
                    break;

                case FakeServerStep.Invoke invoke:
                    await invoke.Action(context, ct).ConfigureAwait(false);
                    break;

                default:
                    throw new FakeServerScriptException($"Step {i} is an unknown step type {step.GetType().Name}.");
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _serverConnection.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Raised when a <see cref="FakeJavaServer"/> script expectation fails.</summary>
public sealed class FakeServerScriptException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public FakeServerScriptException(string message)
        : base(message)
    {
    }
}
