namespace Umpk.Protocol.Java;

/// <summary>Everything the read loop needs about one inbound frame, resolved in a single lookup instead of one tuple-keyed lookup per question.</summary>
/// <remarks><see cref="Role"/> and <see cref="NextPhase"/> are carried as members rather than read back off <see cref="Codec"/>, and the constructor is public, so a binding that holds no <see cref="BoundPacketCodec"/> can still describe a frame's lifecycle effect and opt in.</remarks>
/// <param name="Codec">The bound entry, or null when the wire id is unmapped in this phase and flow.</param>
/// <param name="WireId">The wire id this frame carried.</param>
/// <param name="Role">The frame's lifecycle roles, or <see cref="FrameRole.None"/> when unmapped.</param>
/// <param name="NextPhase">The phase a terminal frame enters; meaningless otherwise.</param>
public readonly record struct ResolvedFrame(
    BoundPacketCodec? Codec,
    int WireId,
    FrameRole Role,
    ProtocolPhase NextPhase)
{
    /// <summary>True when a codec is bound and implemented; a marker reports false.</summary>
    public bool CanDecode => Codec is { IsImplemented: true };

    /// <summary>True when <paramref name="role"/> is among this frame's roles.</summary>
    /// <param name="role">The role to test for.</param>
    public bool Has(FrameRole role) => (Role & role) != 0;
}
