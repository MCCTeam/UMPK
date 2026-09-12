using System.IO.Pipelines;

namespace Umpk.Protocol.Java.Transport;

/// <summary>A connected pair of in-memory <see cref="IDuplexPipe"/>s: whatever one side writes, the other side reads. Used for testing (Umpk.TestKit consumes it) and for pairing the two legs of a proxy without a real socket. Not a transport factory; construct directly.</summary>
public sealed class DuplexPipePair
{
    private DuplexPipePair(IDuplexPipe left, IDuplexPipe right)
    {
        Left = left;
        Right = right;
    }

    /// <summary>One end of the connection.</summary>
    public IDuplexPipe Left { get; }

    /// <summary>The other end; its input is the left end's output and vice versa.</summary>
    public IDuplexPipe Right { get; }

    /// <summary>Creates a connected pair using the given (or default) pipe options.</summary>
    public static DuplexPipePair Create(PipeOptions? options = null)
    {
        options ??= new PipeOptions(useSynchronizationContext: false);
        var leftToRight = new Pipe(options);
        var rightToLeft = new Pipe(options);

        var left = new HalfDuplexPipe(rightToLeft.Reader, leftToRight.Writer);
        var right = new HalfDuplexPipe(leftToRight.Reader, rightToLeft.Writer);
        return new DuplexPipePair(left, right);
    }

    private sealed class HalfDuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;

        public PipeWriter Output { get; } = output;
    }
}
