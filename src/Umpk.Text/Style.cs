namespace Umpk.Text;

/// <summary>The formatting attached to a <see cref="Component"/>: color, the five boolean decorations, font, insertion, and click/hover events. Every field is optional. Null means "inherit from the parent", so a boolean can be true, false (explicit off), or unset. <see cref="Empty"/> is the all-null style.</summary>
public sealed record Style
{
    /// <summary>The empty style with every field unset. Used as the default for a component.</summary>
    public static readonly Style Empty = new();

    /// <summary>The color, or null to inherit.</summary>
    public TextColor? Color { get; init; }

    /// <summary>Bold on/off, or null to inherit.</summary>
    public bool? Bold { get; init; }

    /// <summary>Italic on/off, or null to inherit.</summary>
    public bool? Italic { get; init; }

    /// <summary>Underline on/off, or null to inherit.</summary>
    public bool? Underlined { get; init; }

    /// <summary>Strikethrough on/off, or null to inherit.</summary>
    public bool? Strikethrough { get; init; }

    /// <summary>Obfuscation on/off, or null to inherit.</summary>
    public bool? Obfuscated { get; init; }

    /// <summary>The click event, or null.</summary>
    public ClickEvent? ClickEvent { get; init; }

    /// <summary>The hover event, or null.</summary>
    public HoverEvent? HoverEvent { get; init; }

    /// <summary>Shift-click insertion text, or null.</summary>
    public string? Insertion { get; init; }

    /// <summary>The font identifier (for example <c>minecraft:default</c>), or null to inherit.</summary>
    public string? Font { get; init; }

    /// <summary>True when every field is unset.</summary>
    public bool IsEmpty =>
        Color is null
        && Bold is null
        && Italic is null
        && Underlined is null
        && Strikethrough is null
        && Obfuscated is null
        && ClickEvent is null
        && HoverEvent is null
        && Insertion is null
        && Font is null;

    /// <summary>Resolves this style against a parent: for each unset field, the parent's value is inherited. Matches the game <c>style inheritance</c> (child fields win; null falls through to the parent).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is null.</exception>
    public Style ApplyTo(Style parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.IsEmpty)
            return this;

        if (IsEmpty)
            return parent;

        return new Style
        {
            Color = Color ?? parent.Color,
            Bold = Bold ?? parent.Bold,
            Italic = Italic ?? parent.Italic,
            Underlined = Underlined ?? parent.Underlined,
            Strikethrough = Strikethrough ?? parent.Strikethrough,
            Obfuscated = Obfuscated ?? parent.Obfuscated,
            ClickEvent = ClickEvent ?? parent.ClickEvent,
            HoverEvent = HoverEvent ?? parent.HoverEvent,
            Insertion = Insertion ?? parent.Insertion,
            Font = Font ?? parent.Font,
        };
    }
}
