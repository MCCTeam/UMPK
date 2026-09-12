using System.IO.Pipelines;
using System.Net.Sockets;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Adapts a connected <see cref="Socket"/> to an <see cref="IDuplexPipe"/> using the BCL stream-pipe adapters over a <see cref="NetworkStream"/>. This is the public seam a custom <see cref="IConnectionFactory"/> needs once it has finished its own handshake on the socket: wrap the connected socket here and return the result as the factory's <c>IDuplexPipe</c>. Owns the socket. Dispose this adapter to close the underlying stream and socket; completing the two pipe directions alone deliberately leaves the shared stream open.</summary>
public sealed class SocketDuplexPipe : IDuplexPipe, IDisposable, IAsyncDisposable
{
    private readonly NetworkStream _stream;
    private int _disposed;

    private SocketDuplexPipe(Socket socket)
    {
        _stream = new NetworkStream(socket, ownsSocket: true);
        Input = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
        Output = PipeWriter.Create(_stream, new StreamPipeWriterOptions(leaveOpen: true));
    }

    /// <inheritdoc />
    public PipeReader Input { get; }

    /// <inheritdoc />
    public PipeWriter Output { get; }

    /// <summary>Wraps an already-connected socket.</summary>
    public static SocketDuplexPipe Wrap(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        return new SocketDuplexPipe(socket);
    }

    /// <summary>Closes the owned network stream and socket. Safe to call more than once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stream.Dispose();
    }

    /// <summary>Asynchronously closes the owned network stream and socket. Safe to call more than once.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
