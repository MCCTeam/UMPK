using System.Globalization;
using System.Reflection;

namespace Umpk.Client;

/// <summary>
/// The engine's own version, read back from the assembly rather than restated in code.
/// <para>The build version becomes this assembly's <see cref="AssemblyInformationalVersionAttribute"/>. A host reads the same attribute from the assembly containing <see cref="UmpkClient"/>, avoiding a second version literal.</para>
/// <para><see cref="Informational"/> is whatever the attribute says. On a plain build that is exactly <c>"0.9.0"</c>; a build that also sets <c>SourceRevisionId</c> (SourceLink does) gets <c>"0.9.0+&lt;sha&gt;"</c>, and a prerelease suffix would appear as <c>"0.9.0-rc.1"</c>. <see cref="Current"/> is the core <c>major.minor.patch</c> triple with any prerelease or build metadata removed, which is the form a version RANGE is compared against.</para>
/// </summary>
public static class UmpkVersion
{
    /// <summary>What every member answers when the assembly carries no informational version at all, or one this cannot read a triple out of. Degrading rather than throwing is deliberate: a version string is diagnostic metadata, and a host must not fail to start because a repackager stripped an attribute.</summary>
    private const string Unknown = "0.0.0";

    static UmpkVersion()
    {
        Informational =
            typeof(UmpkClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion is { Length: > 0 } informational
                ? informational
                : Unknown;

        string core = CoreOf(Informational);
        if (TryParseTriple(core, out int major, out int minor, out int patch))
        {
            Current = core;
            Major = major;
            Minor = minor;
            Patch = patch;
        }
        else
            Current = Unknown;

    }

    /// <summary>The core <c>major.minor.patch</c> triple, with any <c>-prerelease</c> and <c>+build</c> metadata stripped: <c>"0.9.0"</c>. This is the form to compare a version range against.</summary>
    public static string Current { get; }

    /// <summary>The full informational version as the assembly carries it, build metadata included: <c>"0.9.0"</c>, or <c>"0.9.0+&lt;sha&gt;"</c> on a build that stamps a source revision.</summary>
    public static string Informational { get; }

    /// <summary>The major component of <see cref="Current"/>.</summary>
    public static int Major { get; }

    /// <summary>The minor component of <see cref="Current"/>.</summary>
    public static int Minor { get; }

    /// <summary>The patch component of <see cref="Current"/>.</summary>
    public static int Patch { get; }

    /// <summary>Everything before the first <c>-</c> (prerelease) or <c>+</c> (build metadata).</summary>
    private static string CoreOf(string informational)
    {
        int cut = informational.AsSpan().IndexOfAny('-', '+');
        return cut < 0 ? informational : informational[..cut];
    }

    private static bool TryParseTriple(string core, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;

        string[] parts = core.Split('.');
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch);
    }
}
