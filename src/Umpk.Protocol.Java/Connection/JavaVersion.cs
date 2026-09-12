namespace Umpk.Protocol.Java;

/// <summary>A supported Java protocol version: its neutral <see cref="GameVersion"/> identity, its <see cref="ProtocolDescriptor"/> (packet tables + codecs), and its <see cref="ProtocolFeatures"/>. Instances are constructed by <c>Umpk.Data.Java</c>'s generated code; the type is declared here so the protocol package owns the seam. It carries the data needed by the codec framework and the login and configuration flows.</summary>
public sealed class JavaVersion
{
    /// <summary>Creates a version handle from its identity, descriptor, and features.</summary>
    public JavaVersion(GameVersion version, ProtocolDescriptor protocolDescriptor, ProtocolFeatures features)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(protocolDescriptor);
        ArgumentNullException.ThrowIfNull(features);
        Version = version;
        Protocol = protocolDescriptor;
        Features = features;
    }

    /// <summary>The edition-qualified version identity.</summary>
    public GameVersion Version { get; }

    /// <summary>The per-version packet tables and codecs.</summary>
    public ProtocolDescriptor Protocol { get; }

    /// <summary>The per-version feature flags.</summary>
    public ProtocolFeatures Features { get; }

    /// <summary>True when this version's release name matches (ordinal).</summary>
    public bool HasName(string name) => string.Equals(Version.Name, name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Version.ToString();
}

/// <summary>The seam a version catalog implements (the generated <c>JavaVersions</c> in <c>Umpk.Data.Java</c> is the production implementation). Kept minimal: protocol/name lookup with no version comparisons leaking into consumers.</summary>
public interface IVersionCatalog
{
    /// <summary>All supported versions. Referencing this roots the full catalog (trimming note).</summary>
    IReadOnlyList<JavaVersion> All { get; }

    /// <summary>Looks up a version by wire protocol number.</summary>
    bool TryGetByProtocol(int protocol, out JavaVersion version);

    /// <summary>Looks up a version by release name.</summary>
    bool TryGetByName(string name, out JavaVersion version);
}
