namespace Umpk.Text.Serialization;

/// <summary>A mutable accumulator for <see cref="Style"/> fields during decoding. Internal to the serializers; the public model stays immutable. Fields left null produce an unset style field.</summary>
internal sealed class StyleBuilder
{
    public TextColor? Color { get; set; }

    public bool? Bold { get; set; }

    public bool? Italic { get; set; }

    public bool? Underlined { get; set; }

    public bool? Strikethrough { get; set; }

    public bool? Obfuscated { get; set; }

    public ClickEvent? ClickEvent { get; set; }

    public HoverEvent? HoverEvent { get; set; }

    public string? Insertion { get; set; }

    public string? Font { get; set; }

    public Style Build() => new()
    {
        Color = Color,
        Bold = Bold,
        Italic = Italic,
        Underlined = Underlined,
        Strikethrough = Strikethrough,
        Obfuscated = Obfuscated,
        ClickEvent = ClickEvent,
        HoverEvent = HoverEvent,
        Insertion = Insertion,
        Font = Font,
    };
}
