using System.IO.Pipelines;

namespace Umpk.Protocol.Java.Transport;

internal sealed class OwnedStreamDuplexPipe : IDuplexPipe, IDisposable, IAsyncDisposable
{
    private readonly Stream _stream;
    private int _disposed;

    public OwnedStreamDuplexPipe(Stream stream)
    {
        _stream = stream;
        Input = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        Output = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
    }

    public PipeReader Input { get; }

    public PipeWriter Output { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stream.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
