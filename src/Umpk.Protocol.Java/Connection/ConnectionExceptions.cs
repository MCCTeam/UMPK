namespace Umpk.Protocol.Java;

/// <summary>Thrown when a <see cref="JavaConnection"/> operation fails because the connection closed.</summary>
public sealed class ConnectionClosedException : Exception
{
    public ConnectionClosedException(CloseReason reason)
        : base($"Connection closed: {reason}.")
        => Reason = reason;

    public ConnectionClosedException(CloseReason reason, string message)
        : base(message)
        => Reason = reason;

    public ConnectionClosedException(CloseReason reason, string message, Exception innerException)
        : base(message, innerException)
        => Reason = reason;

    /// <summary>The reason the connection closed.</summary>
    public CloseReason Reason { get; }

    /// <summary>The server's own decoded kick reason, when this closed a login or a configuration-phase kick. Null for every other close reason, and null when the frame carried a reason that failed to decode (the flattened <see cref="Exception.Message"/> still names the fallback text in that case). Both login and configuration kicks decode a <c>Umpk.Text.Component</c> before this exception is built; this is what lets a caller show the server's own formatting instead of only the flattened string <see cref="Exception.Message"/> carries.</summary>
    public Umpk.Text.Component? Disconnect { get; init; }
}

/// <summary>Thrown when inbound bytes violate the wire protocol at the frame or decode level.</summary>
public sealed class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message)
        : base(message)
    {
    }

    public ProtocolViolationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The wire id of the offending frame, when known.</summary>
    public int? WireId { get; init; }

    /// <summary>Bytes left unconsumed after decode, when the violation is trailing garbage.</summary>
    public int? RemainingBytes { get; init; }
}

/// <summary>Thrown when a compact (non-length-delimited) item component patch carries a component id that the era's own table knows by name but UMPK does not model. This is the one decode fault that costs a packet instead of the session.</summary>
/// <remarks>
/// 1.20.5+ structured components are <c>(VarInt id, payload)</c> with NO length prefix, so an unmodeled component cannot be skipped: nothing says where its payload ends, and every byte after it in that item stream is unreadable. The CONNECTION, however, is fine. Frames are length-delimited at the transport, so the packet is the correct recovery boundary: the reader abandons the rest of the frame, the packet is dropped, and the session continues on the next frame.
/// <para>This type is deliberately NOT a <see cref="ProtocolViolationException"/> and deliberately NOT thrown anywhere else. It is raised at exactly one place, <c>ItemStackCodecs.ReadPatch</c>'s compact add-component branch, and only for a wire id that is IN RANGE for the era's component table. An id outside that range still raises <see cref="ProtocolViolationException"/>, because that is a real framing fault rather than a modeling gap. The two sibling call sites do not need this escape at all: the length-delimited patch captures the unmodeled payload verbatim, and a removal entry has no payload to skip, so both stay lossless.</para>
/// <para>The packet-scoped policy also applies to cosmetic item components, such as decorated-pot data in an advancement icon. Compact payloads still have no length to skip, regardless of the containing packet, so recovery cannot safely be narrowed to one component or widened to the whole connection.</para>
/// </remarks>
public sealed class UnmodeledItemComponentException : Exception
{
    /// <summary>Creates the fault for an unmodeled component.</summary>
    /// <param name="componentWireId">The component's numeric wire id on this era.</param>
    /// <param name="componentId">The component's namespaced id on this era.</param>
    public UnmodeledItemComponentException(int componentWireId, Identifier componentId)
        : base($"Item component '{componentId}' (wire id {componentWireId}) is not modeled on this era and its compact payload carries no length prefix, so the rest of this packet cannot be read.")
    {
        ComponentWireId = componentWireId;
        ComponentId = componentId;
    }

    /// <summary>Creates the fault with an explicit message, preserving the component identity.</summary>
    /// <param name="componentWireId">The component's numeric wire id on this era.</param>
    /// <param name="componentId">The component's namespaced id on this era.</param>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The originating fault.</param>
    public UnmodeledItemComponentException(int componentWireId, Identifier componentId, string message, Exception innerException)
        : base(message, innerException)
    {
        ComponentWireId = componentWireId;
        ComponentId = componentId;
    }

    /// <summary>The component's numeric wire id on this era.</summary>
    public int ComponentWireId { get; }

    /// <summary>The component's namespaced id on this era.</summary>
    public Identifier ComponentId { get; }

    /// <summary>The wire id of the packet that carried it, attached once the packet identity is known.</summary>
    public int? WireId { get; init; }
}

/// <summary>Thrown when a connection is asked to bind a protocol it does not support.</summary>
public sealed class UnsupportedProtocolException : Exception
{
    public UnsupportedProtocolException(string message)
        : base(message)
    {
    }
}
