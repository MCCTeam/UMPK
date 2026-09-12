namespace Umpk;

/// <summary>A signed (or unsigned) property attached to a game profile, such as <c>textures</c>.</summary>
public sealed record ProfileProperty(string Name, string Value, string? Signature = null);

/// <summary>A player identity: UUID, username, and any session-service properties.</summary>
public sealed record GameProfile(Guid Id, string Name)
{
    /// <summary>Session-service properties (for example the skin <c>textures</c> blob). Empty by default.</summary>
    public IReadOnlyList<ProfileProperty> Properties { get; init; } = [];

    public override string ToString() => $"{Name} ({Id})";
}
