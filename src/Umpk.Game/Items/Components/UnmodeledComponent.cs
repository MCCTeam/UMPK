namespace Umpk.Game.Items.Components;

/// <summary>The payload of an item component this era's table knows by name but UMPK does not model, retained as its verbatim wire bytes so the component still round-trips byte-for-byte.</summary>
/// <remarks>
/// This is reachable only where the wire itself delimits the payload, which is the length-prefixed (creative/untrusted) component patch and the payload-free removal list. On the compact patch a component payload has no length prefix, so an unmodeled component genuinely cannot be captured or skipped there; that case raises <c>UnmodeledItemComponentException</c> in the Java codec layer and costs the packet, never the session.
/// <para>Equality is structural over the component id and the payload bytes, because <see cref="DataComponentMap"/> compares patch values with <see cref="object.Equals(object)"/> when it canonicalizes a patch.</para>
/// </remarks>
public sealed class UnmodeledComponent : IEquatable<UnmodeledComponent>
{
    private readonly byte[] _payload;

    /// <summary>Creates the carrier for a component id and its verbatim payload bytes.</summary>
    /// <param name="componentId">The component's namespaced id on this era.</param>
    /// <param name="payload">The payload bytes, exactly as they appeared on the wire.</param>
    public UnmodeledComponent(Identifier componentId, ReadOnlySpan<byte> payload)
    {
        ComponentId = componentId;
        _payload = payload.ToArray();
    }

    /// <summary>The component's namespaced id on this era.</summary>
    public Identifier ComponentId { get; }

    /// <summary>The verbatim payload bytes.</summary>
    public ReadOnlySpan<byte> Payload => _payload;

    /// <summary>The payload length in bytes.</summary>
    public int PayloadLength => _payload.Length;

    /// <inheritdoc/>
    public bool Equals(UnmodeledComponent? other) =>
        other is not null && ComponentId.Equals(other.ComponentId) && _payload.AsSpan().SequenceEqual(other._payload);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is UnmodeledComponent other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(ComponentId);
        hash.AddBytes(_payload);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => $"{ComponentId} ({_payload.Length} unmodeled byte(s))";
}
