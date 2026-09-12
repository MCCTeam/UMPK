namespace Umpk;

/// <summary>A namespaced identifier (<c>namespace:path</c>) using Minecraft registry character rules. The namespace defaults to <c>minecraft</c> when omitted.</summary>
public readonly struct Identifier : IEquatable<Identifier>
{
    /// <summary>The default namespace applied when none is given.</summary>
    public const string DefaultNamespace = "minecraft";

    private readonly string? _namespace;
    private readonly string? _path;

    /// <summary>Creates an identifier from a validated namespace and path.</summary>
    /// <exception cref="FormatException">A part contains characters outside the allowed set.</exception>
    public Identifier(string @namespace, string path)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        ArgumentNullException.ThrowIfNull(path);
        if (!IsValidNamespace(@namespace))
            throw new FormatException($"Invalid identifier namespace '{@namespace}'. Allowed: [a-z0-9_.-]+");

        if (!IsValidPath(path))
            throw new FormatException($"Invalid identifier path '{path}'. Allowed: [a-z0-9/._-]+");

        _namespace = string.Equals(@namespace, DefaultNamespace, StringComparison.Ordinal) ? DefaultNamespace : @namespace;
        _path = path;
    }

    private Identifier(string @namespace, string path, bool validated)
    {
        _namespace = @namespace;
        _path = path;
    }

    /// <summary>The namespace part. <c>minecraft</c> for a default-constructed value.</summary>
    public string Namespace => _namespace ?? DefaultNamespace;

    /// <summary>The path part. Empty for a default-constructed value.</summary>
    public string Path => _path ?? string.Empty;

    /// <summary>True when the namespace is the vanilla <c>minecraft</c> namespace.</summary>
    public bool IsMinecraft => string.Equals(Namespace, DefaultNamespace, StringComparison.Ordinal);

    /// <summary>Parses <c>"path"</c>, <c>":path"</c> (both defaulting to <c>minecraft</c>) or <c>"namespace:path"</c>.</summary>
    /// <exception cref="FormatException">The input is not a valid identifier.</exception>
    public static Identifier Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryParse(value, out var id)
            ? id
            : throw new FormatException($"Invalid identifier '{value}'.");
    }

    /// <summary>Attempts to parse an identifier; see <see cref="Parse"/> for the accepted forms.</summary>
    public static bool TryParse(string? value, out Identifier identifier)
    {
        identifier = default;
        if (value is null)
            return false;

        string ns;
        string path;
        int colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            ns = DefaultNamespace;
            path = value;
        }
        else
        {
            // A leading colon uses the default namespace.
            ns = colon == 0 ? DefaultNamespace : value[..colon];
            path = value[(colon + 1)..];
        }

        if (!IsValidNamespace(ns) || !IsValidPath(path))
            return false;

        identifier = new Identifier(ns, path, validated: true);
        return true;
    }

    /// <summary>Creates a <c>minecraft:</c>-namespaced identifier from a path.</summary>
    public static Identifier Minecraft(string path) => new(DefaultNamespace, path);

    private static bool IsValidNamespace(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (char c in value)
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-'))
                return false;

        return true;
    }

    private static bool IsValidPath(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (char c in value)
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-' or '/'))
                return false;

        return true;
    }

    public bool Equals(Identifier other) =>
        string.Equals(Path, other.Path, StringComparison.Ordinal)
        && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Identifier other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.Ordinal.GetHashCode(Namespace), StringComparer.Ordinal.GetHashCode(Path));

    public override string ToString() => $"{Namespace}:{Path}";

    public static bool operator ==(Identifier left, Identifier right) => left.Equals(right);

    public static bool operator !=(Identifier left, Identifier right) => !left.Equals(right);
}
