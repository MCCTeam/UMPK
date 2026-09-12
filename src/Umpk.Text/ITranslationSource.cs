using System.Diagnostics.CodeAnalysis;

namespace Umpk.Text;

/// <summary>Resolves translation keys to format templates (the <c>%s</c> / <c>%1$s</c> style strings vanilla stores per locale). This seam is deliberate: <see cref="Umpk.Text"/> ships the flattening logic, while the vanilla <c>en_us</c> data and any server resource-pack layers are supplied by the host or the dataset layer. A null source (or an unresolved key) means the key itself, or a component-provided fallback, is used.</summary>
public interface ITranslationSource
{
    /// <summary>Attempts to resolve <paramref name="key"/> to its format template. Returns false when the key is unknown; callers then fall back to the component's fallback string or the key itself.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    bool TryResolve(string key, [NotNullWhen(true)] out string? template);
}

/// <summary>A translation source that resolves nothing. Every lookup misses, so translatable content renders as its fallback (when present) or its raw key. Useful as a default and in tests.</summary>
public sealed class NullTranslationSource : ITranslationSource
{
    /// <summary>The shared instance.</summary>
    public static readonly NullTranslationSource Instance = new();

    private NullTranslationSource()
    {
    }

    /// <inheritdoc/>
    public bool TryResolve(string key, [NotNullWhen(true)] out string? template)
    {
        ArgumentNullException.ThrowIfNull(key);
        template = null;
        return false;
    }
}
