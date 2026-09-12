using Umpk.Protocol.Java;

namespace Umpk.TestKit.Server;

/// <summary>One ordered step in a <see cref="FakeJavaServer"/> script. Steps run in sequence over a real <see cref="JavaConnection"/>, so framing, compression, and encryption execute for real. A step is either an expectation (block until the client sends a matching frame) or an action (send a frame, or reconfigure the connection). The closed hierarchy keeps the runner allocation-light and reflection-free.</summary>
public abstract class FakeServerStep
{
    private protected FakeServerStep()
    {
    }

    /// <summary>Waits for the next serverbound frame and asserts it matches; captures it for later.</summary>
    public sealed class ExpectFrame : FakeServerStep
    {
        /// <summary>Creates an expectation over a received frame.</summary>
        public ExpectFrame(Func<InboundFrame, bool> predicate, string description)
        {
            Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        /// <summary>The predicate the received frame must satisfy.</summary>
        public Func<InboundFrame, bool> Predicate { get; }

        /// <summary>Human-readable description used in assertion failures.</summary>
        public string Description { get; }
    }

    /// <summary>Sends a raw frame (wire id + body) to the client.</summary>
    public sealed class SendFrame : FakeServerStep
    {
        /// <summary>Creates a raw-frame send step.</summary>
        public SendFrame(int wireId, byte[] body)
        {
            WireId = wireId;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        /// <summary>The wire id to send.</summary>
        public int WireId { get; }

        /// <summary>The frame body (after the wire id).</summary>
        public byte[] Body { get; }
    }

    /// <summary>Enables zlib compression at a threshold on the server connection.</summary>
    public sealed class EnableCompression : FakeServerStep
    {
        /// <summary>Creates a compression step.</summary>
        public EnableCompression(int threshold) => Threshold = threshold;

        /// <summary>The compression threshold.</summary>
        public int Threshold { get; }
    }

    /// <summary>Advances the server connection's tracked phase.</summary>
    public sealed class SetPhase : FakeServerStep
    {
        /// <summary>Creates a phase-transition step.</summary>
        public SetPhase(ProtocolPhase phase) => Phase = phase;

        /// <summary>The phase to enter.</summary>
        public ProtocolPhase Phase { get; }
    }

    /// <summary>Runs an arbitrary async action against the running server (for custom logic).</summary>
    public sealed class Invoke : FakeServerStep
    {
        /// <summary>Creates a custom-action step.</summary>
        public Invoke(Func<FakeServerContext, CancellationToken, Task> action) =>
            Action = action ?? throw new ArgumentNullException(nameof(action));

        /// <summary>The action to run.</summary>
        public Func<FakeServerContext, CancellationToken, Task> Action { get; }
    }
}
