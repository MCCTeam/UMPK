using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Umpk.TestKit.Server;

/// <summary>A minimal scripted status-ping server for tests: accepts one connection, reads the handshake and status-request frames (both ignored), replies with a fixed status JSON body, then echoes the ping frame it receives as a pong. Speaks the frozen status wire format directly (VarInt length + wire id + payload), not through <c>JavaConnection</c>, so it works independent of the codec framework.</summary>
public sealed class ScriptedStatusServer : IDisposable
{
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Starts listening on an ephemeral loopback port and serves the given status JSON once.</summary>
    public ScriptedStatusServer(string json)
    {
        _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        _listener.Listen(1);
        Port = (ushort)((IPEndPoint)_listener.LocalEndPoint!).Port;
        _ = Task.Run(() => ServeAsync(json));
    }

    /// <summary>The ephemeral loopback port the server is listening on.</summary>
    public ushort Port { get; }

    private async Task ServeAsync(string json)
    {
        Socket client;
        try
        {
            client = await _listener.AcceptAsync(_cts.Token);
        }
        catch (Exception)
        {
            return;
        }

        using var stream = new NetworkStream(client, ownsSocket: true);

        // Read handshake frame (next state = 1).
        await ReadFrameAsync(stream, _cts.Token); // handshake body ignored
        // Read status request frame (empty body, wire id 0x00).
        await ReadFrameAsync(stream, _cts.Token);

        // Send status response frame: wire id 0x00 + VarInt(len) + json.
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] respBody = [0x00, .. EncodeVarInt(jsonBytes.Length), .. jsonBytes];
        await WriteFrameAsync(stream, respBody, _cts.Token);

        // Read ping frame (wire id 0x01 + 8-byte payload); echo it as pong.
        byte[] ping = await ReadFrameAsync(stream, _cts.Token);
        await WriteFrameAsync(stream, ping, _cts.Token);

        await Task.Delay(50, _cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private static async Task WriteFrameAsync(NetworkStream stream, byte[] frameContent, CancellationToken ct)
    {
        byte[] len = EncodeVarInt(frameContent.Length);
        await stream.WriteAsync(len, ct);
        await stream.WriteAsync(frameContent, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        int length = await ReadVarIntAsync(stream, ct);
        byte[] body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = await stream.ReadAsync(body.AsMemory(read), ct);
            if (n == 0)
                throw new IOException("client closed early");

            read += n;
        }

        return body;
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
    {
        int value = 0, shift = 0;
        byte[] one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one.AsMemory(0, 1), ct);
            if (n == 0)
                throw new IOException("client closed early");

            value |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
                return value;

            shift += 7;
        }
    }

    private static byte[] EncodeVarInt(int value)
    {
        var bytes = new List<byte>();
        uint v = (uint)value;
        while ((v & 0xFFFFFF80u) != 0)
        {
            bytes.Add((byte)(v | 0x80));
            v >>= 7;
        }

        bytes.Add((byte)v);
        return [.. bytes];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _cts.Cancel();
        _listener.Dispose();
        _cts.Dispose();
    }
}
