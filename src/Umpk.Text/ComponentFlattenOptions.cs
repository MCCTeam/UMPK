namespace Umpk.Text;

/// <summary>Options controlling <see cref="ComponentFlattener"/>: where translation keys resolve from, whether embedded legacy section-sign codes are decoded, and the style the walk starts from.</summary>
public readonly record struct ComponentFlattenOptions
{
    /// <summary>The default options: no translation source, legacy codes decoded, empty base style.</summary>
    public static readonly ComponentFlattenOptions Default = new()
    {
        Translations = null,
        DecodeLegacyCodes = true,
        BaseStyle = Style.Empty,
    };

    /// <summary>The translation source used to resolve <see cref="TranslatableContent"/> keys, or null.</summary>
    public ITranslationSource? Translations { get; init; }

    /// <summary>Whether legacy section-sign codes embedded in literal text are decoded into styled runs via <see cref="Serialization.LegacyText.Decompose{TSink}"/>. When false, such text emits as one run with the codes left verbatim in <see cref="StyledRun.Text"/>.</summary>
    public bool DecodeLegacyCodes { get; init; }

    /// <summary>The style the walk starts from; the root component's own style resolves against this.</summary>
    public Style BaseStyle { get; init; }

    /// <summary>Options with the given translation source and vanilla defaults otherwise.</summary>
    public static ComponentFlattenOptions For(ITranslationSource? translations) => Default with { Translations = translations };
}
