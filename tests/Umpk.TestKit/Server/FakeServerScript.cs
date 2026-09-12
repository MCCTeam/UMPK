using Umpk.Protocol.Java;

namespace Umpk.TestKit.Server;

/// <summary>An ordered script of <see cref="FakeServerStep"/>s for a <see cref="FakeJavaServer"/>. Build one with the fluent methods, then hand it to <see cref="FakeJavaServer.RunAsync"/>. The script drives the server side of a real <see cref="JavaConnection"/>, so every send/expect exercises the real framing, compression, and encryption code paths.</summary>
public sealed class FakeServerScript
{
    private readonly List<FakeServerStep> _steps = [];

    /// <summary>The ordered steps.</summary>
    public IReadOnlyList<FakeServerStep> Steps => _steps;

    /// <summary>Starts a new empty script.</summary>
    public static FakeServerScript Create() => new();

    /// <summary>Expects the next serverbound frame to match a predicate.</summary>
    public FakeServerScript ExpectFrame(Func<InboundFrame, bool> predicate, string description = "frame")
    {
        _steps.Add(new FakeServerStep.ExpectFrame(predicate, description));
        return this;
    }

    /// <summary>Expects the next serverbound frame to carry a specific wire id.</summary>
    public FakeServerScript ExpectWireId(int wireId) =>
        ExpectFrame(f => f.WireId == wireId, $"wireId=0x{wireId:X2}");

    /// <summary>Expects the next serverbound frame's wire id and full body bytes.</summary>
    public FakeServerScript ExpectFrameBytes(int wireId, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return ExpectFrame(
            f => f.WireId == wireId && f.Payload.SequenceEqual(body),
            $"wireId=0x{wireId:X2} body[{body.Length}]");
    }

    /// <summary>Sends a raw frame (wire id + body) to the client.</summary>
    public FakeServerScript SendFrame(int wireId, byte[] body)
    {
        _steps.Add(new FakeServerStep.SendFrame(wireId, body));
        return this;
    }

    /// <summary>Enables compression at the given threshold on the server connection.</summary>
    public FakeServerScript EnableCompression(int threshold)
    {
        _steps.Add(new FakeServerStep.EnableCompression(threshold));
        return this;
    }

    /// <summary>Advances the server connection's tracked phase.</summary>
    public FakeServerScript SetPhase(ProtocolPhase phase)
    {
        _steps.Add(new FakeServerStep.SetPhase(phase));
        return this;
    }

    /// <summary>Runs a custom async action against the server context.</summary>
    public FakeServerScript Invoke(Func<FakeServerContext, CancellationToken, Task> action)
    {
        _steps.Add(new FakeServerStep.Invoke(action));
        return this;
    }
}
