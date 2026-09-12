using System.IO.Pipelines;
using Umpk.Protocol.Java.Transport;

namespace Umpk.TestKit.Pipes;

/// <summary>Convenience factory for the in-memory duplex pipe pair the transport layer ships (<see cref="DuplexPipePair"/>). Re-exported here so test code has a single TestKit import for wiring a client and a scripted/fake server over a byte-accurate in-memory link.</summary>
public static class InMemoryPipe
{
    /// <summary>Creates a connected pair of in-memory duplex pipes.</summary>
    public static DuplexPipePair CreatePair(PipeOptions? options = null) => DuplexPipePair.Create(options);
}
