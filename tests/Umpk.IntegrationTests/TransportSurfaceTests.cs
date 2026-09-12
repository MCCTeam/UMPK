using System.Net;
using System.Net.Sockets;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.IntegrationTests;

/// <summary>Proves the transport seam types that a custom <c>IConnectionFactory</c> needs are actually reachable from outside <c>Umpk.Protocol.Java</c>. This project is not a friend assembly of Umpk.Protocol.Java, so these only compile once the seam types are public.</summary>
public sealed class TransportSurfaceTests
{
    [Fact]
    public async Task SocketDuplexPipe_IsUsableFromOutsideTheProtocolAssembly()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var listenPort = ((IPEndPoint)listener.LocalEndPoint!).Port;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Task connectTask = client.ConnectAsync(IPAddress.Loopback, listenPort);
        using Socket server = await listener.AcceptAsync();
        await connectTask;

        SocketDuplexPipe pipe = SocketDuplexPipe.Wrap(client);
        try
        {
            byte[] payload = [0x01, 0x02, 0x03, 0x04];
            await pipe.Output.WriteAsync(payload);

            byte[] received = new byte[payload.Length];
            int read = 0;
            while (read < received.Length)
            {
                int n = await server.ReceiveAsync(received.AsMemory(read));
                Assert.True(n > 0);
                read += n;
            }

            Assert.Equal(payload, received);
        }
        finally
        {
            pipe.Output.Complete();
            pipe.Input.Complete();
        }
    }

    [Fact]
    public async Task JavaConnectionDispose_ClosesTheOwnedSocket_AndIsIdempotent()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var listenPort = ((IPEndPoint)listener.LocalEndPoint!).Port;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        Task connectTask = client.ConnectAsync(IPAddress.Loopback, listenPort);
        using Socket server = await listener.AcceptAsync();
        await connectTask;

        SocketDuplexPipe pipe = SocketDuplexPipe.Wrap(client);
        var connection = new JavaConnection(pipe, new JavaConnectionOptions());
        connection.Start();

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var oneByte = new byte[1];
        int read = await server.ReceiveAsync(oneByte, SocketFlags.None, timeout.Token);
        Assert.Equal(0, read);

        // The owning transport is a public seam and remains safe when a caller also disposes it.
        IAsyncDisposable asyncPipe = Assert.IsAssignableFrom<IAsyncDisposable>(pipe);
        await asyncPipe.DisposeAsync();
        IDisposable syncPipe = Assert.IsAssignableFrom<IDisposable>(pipe);
        syncPipe.Dispose();
    }
}
