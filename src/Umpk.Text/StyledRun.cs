namespace Umpk.Text;

/// <summary>One flat, fully resolved run of text produced by <see cref="ComponentFlattener"/>. Style inheritance, translation substitution, and (optionally) legacy section-sign decoding are already applied; a null boolean on <see cref="Style"/> means "off" at this leaf, because nothing up the chain ever set it.</summary>
public readonly record struct StyledRun(string Text, Style Style)
{
    /// <summary>The resolved color, or null when unset anywhere up the chain.</summary>
    public TextColor? Color => Style.Color;

    /// <summary>Whether bold is on.</summary>
    public bool Bold => Style.Bold is true;

    /// <summary>Whether italic is on.</summary>
    public bool Italic => Style.Italic is true;

    /// <summary>Whether underline is on.</summary>
    public bool Underlined => Style.Underlined is true;

    /// <summary>Whether strikethrough is on.</summary>
    public bool Strikethrough => Style.Strikethrough is true;

    /// <summary>Whether obfuscation is on.</summary>
    public bool Obfuscated => Style.Obfuscated is true;

    /// <summary>The resolved click event, or null.</summary>
    public ClickEvent? ClickEvent => Style.ClickEvent;

    /// <summary>The resolved hover event, or null.</summary>
    public HoverEvent? HoverEvent => Style.HoverEvent;

    /// <summary>The resolved shift-click insertion text, or null.</summary>
    public string? Insertion => Style.Insertion;

    /// <summary>The resolved font identifier, or null.</summary>
    public string? Font => Style.Font;
}
