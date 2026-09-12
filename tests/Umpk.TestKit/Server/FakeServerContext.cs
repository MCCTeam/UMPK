using Umpk.Protocol.Java;

namespace Umpk.TestKit.Server;

/// <summary>The live context a <see cref="FakeServerStep.Invoke"/> action receives: the server's real <see cref="JavaConnection"/> and helpers for sending frames and awaiting the next serverbound frame. Lets a script step run bespoke logic (respond to a decoded value, branch, etc.).</summary>
public sealed class FakeServerContext
{
    private readonly FakeJavaServer _server;

    internal FakeServerContext(FakeJavaServer server, JavaConnection connection)
    {
        _server = server;
        Connection = connection;
    }

    /// <summary>The server side of the connection.</summary>
    public JavaConnection Connection { get; }

    /// <summary>Sends a raw frame to the client.</summary>
    public ValueTask SendFrameAsync(int wireId, byte[] body, CancellationToken ct) =>
        Connection.SendFrameAsync(wireId, body, ct);

    /// <summary>Awaits the next serverbound frame from the client.</summary>
    public ValueTask<InboundFrame> NextFrameAsync(CancellationToken ct) => _server.NextFrameAsync(ct);
}
