using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using Microsoft.Extensions.Logging;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Protocol.Java;

/// <summary>Server list ping helper. The status exchange uses a frozen wire format that works version-blind, so it is hand-rolled here without the packet-codec framework: handshake(next=1), status request, status response (JSON), then a ping/pong latency probe.</summary>
public static class JavaStatus
{
    private const int HandshakePacketId = 0x00;

    private const int StatusRequestId = 0x00;

    private const int StatusResponseId = 0x00;

    private const int PingId = 0x01;

    private const int PongId = 0x01;

    // Version -1 is the convention for a version-agnostic status handshake.
    private const int StatusProtocolVersion = -1;

    /// <summary>Full ping against an already-resolved endpoint using the given connection factory. Returns the raw status JSON and the measured round-trip latency.</summary>
    public static async Task<ServerStatus> QueryAsync(
        ServerEndpoint endpoint, IConnectionFactory factory, JavaStatusOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(options);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.Timeout);
        CancellationToken token = timeoutCts.Token;

        using Activity? activity = ConnectionDiagnostics.ActivitySource.StartActivity("java.status.query");
        IDuplexPipe pipe = await factory.ConnectAsync(endpoint, token).ConfigureAwait(false);

        var connection = new JavaConnection(pipe, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            Logger = options.Logger,
            ReadIdleTimeout = options.Timeout,
        });
        connection.BindCodec(null, PacketFlow.Clientbound);
        connection.SetDecodeFilter(PacketDecodeFilter.None);
        connection.Start();

        await using (connection.ConfigureAwait(false))
        {
            // Handshake: protocol version, server address, port, next state = 1 (status).
            await connection.SendFrameAsync(HandshakePacketId,
                BuildHandshake(endpoint, StatusProtocolVersion, nextState: 1), token).ConfigureAwait(false);
            connection.SetPhase(ProtocolPhase.Status);

            // Status request (empty body).
            await connection.SendFrameAsync(StatusRequestId, ReadOnlyMemory<byte>.Empty, token).ConfigureAwait(false);

            string json = await ReadStatusJsonAsync(connection, token).ConfigureAwait(false);

            // Ping with a timestamp payload; pong echoes it. Measure round trip.
            long payload = Stopwatch.GetTimestamp();
            var pingBody = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(pingBody, payload);
            long sentAt = Stopwatch.GetTimestamp();
            await connection.SendFrameAsync(PingId, pingBody, token).ConfigureAwait(false);

            TimeSpan latency = await ReadPongAsync(connection, payload, sentAt, token).ConfigureAwait(false);
            return ServerStatus.Parse(json, latency);
        }
    }

    /// <summary>Convenience overload: resolves the host via the given (or default) resolver, then pings via the given (or default TCP) factory.</summary>
    /// <remarks>The resolver is only consulted when <paramref name="port"/> is the default Java port and only looks up <c>_minecraft._tcp</c> for port 25565. A resolver failure is swallowed and the address is used as given, mirroring <c>UmpkClient.ConnectAsync</c>'s own address-resolution fallback.</remarks>
    public static async Task<ServerStatus> QueryAsync(
        string host, ushort port, JavaStatusOptions options, CancellationToken ct,
        IServerAddressResolver? resolver, IConnectionFactory? factory)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(options);

        resolver ??= new DnsSrvResolver();
        factory ??= TcpConnectionFactory.Shared;

        var requested = new ServerEndpoint(host, port);
        ServerEndpoint resolved = requested;
        if (port == ServerEndpoint.DefaultJavaPort)
            try
            {
                resolved = await resolver.ResolveAsync(requested, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                options.Logger.LogDebug(
                    ex, "Address resolution for {Host} failed; pinging the address as given.", host);
            }

        return await QueryAsync(resolved, factory, options, ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadStatusJsonAsync(JavaConnection connection, CancellationToken ct)
    {
        await foreach (InboundFrame frame in connection.ReceiveFramesAsync(ct).ConfigureAwait(false))
        {
            if (frame.WireId != StatusResponseId)
                continue;

            ReadOnlySpan<byte> payload = frame.Payload;
            int json = ReadVarInt(payload, out int header);
            ReadOnlySpan<byte> jsonBytes = payload.Slice(header, json);
            return Encoding.UTF8.GetString(jsonBytes);
        }

        throw new ConnectionClosedException(CloseReason.SocketEof, "Server closed before sending a status response.");
    }

    private static async Task<TimeSpan> ReadPongAsync(
        JavaConnection connection, long expectedPayload, long sentAt, CancellationToken ct)
    {
        await foreach (InboundFrame frame in connection.ReceiveFramesAsync(ct).ConfigureAwait(false))
        {
            if (frame.WireId != PongId || frame.Payload.Length < 8)
                continue;

            long echoed = BinaryPrimitives.ReadInt64BigEndian(frame.Payload);
            if (echoed != expectedPayload)
                continue;

            long elapsedTicks = Stopwatch.GetTimestamp() - sentAt;
            return Stopwatch.GetElapsedTime(sentAt, sentAt + elapsedTicks);
        }

        throw new ConnectionClosedException(CloseReason.SocketEof, "Server closed before responding to ping.");
    }

    private static byte[] BuildHandshake(ServerEndpoint endpoint, int protocolVersion, int nextState)
    {
        var writer = new ArrayBufferWriter<byte>(32);
        WriteVarInt(writer, protocolVersion);
        WriteString(writer, endpoint.Host);
        Span<byte> portSpan = writer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(portSpan, endpoint.Port);
        writer.Advance(2);
        WriteVarInt(writer, nextState);
        return writer.WrittenSpan.ToArray();
    }

    private static void WriteString(IBufferWriter<byte> writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(writer, bytes.Length);
        bytes.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
    }

    private static void WriteVarInt(IBufferWriter<byte> writer, int value)
    {
        Span<byte> span = writer.GetSpan(VarInt.MaxBytes);
        int n = VarInt.Write(value, span);
        writer.Advance(n);
    }

    internal static int ReadVarInt(ReadOnlySpan<byte> span, out int bytesRead) =>
        VarInt.Read(span, VarInt.MaxBytes, out int value, out bytesRead) switch
        {
            VarIntStatus.Ok => value,
            VarIntStatus.TooLong => throw new ProtocolViolationException("Status VarInt is too long."),
            _ => throw new ProtocolViolationException("Status payload ended before VarInt terminated."),
        };
}
