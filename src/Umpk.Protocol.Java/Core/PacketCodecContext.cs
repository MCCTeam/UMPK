using Umpk.Game.Registries;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The context a codec reads during decode/encode. It exposes the live per-connection registry view and the dynamic connection codec state, and deliberately does NOT expose a protocol version: if a codec needs version-dependent behavior it is constructed as a different codec instance for that version range. The type surface enforces that rule rather than relying on convention.</summary>
/// <remarks>Memory model (normative): <see cref="Registries"/> is immutable for the duration of a phase and is swapped only at a phase-transition pause via <c>JavaConnection.SetCodecState</c>. The single exception is the descriptor-declared first-packet registry update (JoinGame on 1.16-1.20.1), applied on the read loop before any later packet decodes. <see cref="State"/> is mutated only by decode-side hooks in wire order on the read loop.</remarks>
public sealed class PacketCodecContext
{
    /// <summary>Creates a context over a registry view and dynamic connection state.</summary>
    public PacketCodecContext(RegistryAccess registries, IConnectionCodecState state)
    {
        ArgumentNullException.ThrowIfNull(registries);
        ArgumentNullException.ThrowIfNull(state);
        Registries = registries;
        State = state;
    }

    /// <summary>The live per-connection registries, needed for holder-id fields.</summary>
    public RegistryAccess Registries { get; }

    /// <summary>The per-connection dynamic codec state.</summary>
    public IConnectionCodecState State { get; }

    /// <summary>A context with empty registries and empty state, for codecs (handshake/status/login and the version-invariant primitives) that never touch registry holders. Built lazily.</summary>
    public static PacketCodecContext Registryless { get; } =
        new(EmptyRegistries.Access, IConnectionCodecState.Empty);
}
