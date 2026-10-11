namespace Umpk;

/// <summary>The Minecraft edition a version or connection belongs to.</summary>
public enum GameEdition
{
    /// <summary>The Java Edition protocol family carried by <c>Umpk.Protocol.Java</c>.</summary>
    Java,
    /// <summary>The Bedrock protocol family carried by the Bedrock transport, codec, and client projects.</summary>
    Bedrock,
}

/// <summary>Edition-qualified version identity. Richer, edition-specific version handles (for Java: <c>JavaVersion</c> in <c>Umpk.Protocol.Java</c>) carry this as their neutral core.</summary>
/// <param name="Edition">The edition this version belongs to; no API takes a bare protocol integer where the edition could matter.</param>
/// <param name="Name">The release name as published by Mojang (for example <c>1.21.5</c> or <c>26.2</c>).</param>
/// <param name="Protocol">The wire protocol number.</param>
public sealed record GameVersion(GameEdition Edition, string Name, int Protocol)
{
    public override string ToString() => $"{Edition} {Name} (protocol {Protocol})";
}
