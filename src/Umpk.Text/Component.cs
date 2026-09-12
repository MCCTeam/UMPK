using System.Collections.ObjectModel;
using System.Text;

namespace Umpk.Text;

/// <summary>An immutable text component: a typed <see cref="ComponentContent"/> body, a <see cref="Style"/>, and an ordered list of child components (the <c>extra</c> array). One model carries every kind of text the protocol transports, including chat, titles, item names, and kick reasons.</summary>
public sealed record Component
{
    /// <summary>Creates a component from a content body, a style, and children.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/>, <paramref name="style"/>, or <paramref name="children"/> is null.</exception>
    public Component(ComponentContent content, Style style, IReadOnlyList<Component> children)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(children);
        Content = content;
        Style = style;
        Children = children as ReadOnlyCollection<Component> ?? new ReadOnlyCollection<Component>([.. children]);
    }

    /// <summary>Creates a component with a style and no children.</summary>
    public Component(ComponentContent content, Style style)
        : this(content, style, [])
    {
    }

    /// <summary>Creates a component with an empty style and no children.</summary>
    public Component(ComponentContent content)
        : this(content, Style.Empty, [])
    {
    }

    /// <summary>The typed content body.</summary>
    public ComponentContent Content { get; }

    /// <summary>The formatting applied to this component (before parent inheritance).</summary>
    public Style Style { get; }

    /// <summary>The child components appended after this one, sharing this component's style as parent.</summary>
    public IReadOnlyList<Component> Children { get; }

    /// <inheritdoc/>
    public bool Equals(Component? other) =>
        other is not null
        && Content.Equals(other.Content)
        && Style.Equals(other.Style)
        && Children.SequenceEqual(other.Children);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Content);
        hash.Add(Style);
        foreach (Component child in Children)
            hash.Add(child);

        return hash.ToHashCode();
    }

    /// <summary>Creates a literal-text component with an empty style and no children.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static Component Text(string text) => new(new TextContent(text));

    /// <summary>Creates a translatable component with the given key and component arguments.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public static Component Translatable(string key, params Component[] args)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(args);
        return new Component(new TranslatableContent(key, null, args));
    }

    /// <summary>The shared empty component (empty literal text, empty style, no children).</summary>
    public static Component Empty { get; } = new(TextContent.Empty);

    /// <summary>Flattens this component tree to plain text, resolving translation keys through <paramref name="translations"/> (or leaving them as fallback/key when null or unresolved) and substituting positional arguments (<c>%s</c> and <c>%1$s</c> forms). Style, click, and hover data is dropped. Embedded <c>§</c> codes stay verbatim in the output.</summary>
    public string ToPlainText(ITranslationSource? translations = null)
    {
        var sb = new StringBuilder();
        var options = ComponentFlattenOptions.For(translations) with { DecodeLegacyCodes = false };
        ComponentFlattener.Flatten(this, new PlainTextSink(sb), options);
        return sb.ToString();
    }

    // Wraps a StringBuilder as a reference-semantic struct sink for ToPlainText's Flatten call.
    private readonly struct PlainTextSink : IStyledRunSink
    {
        private readonly StringBuilder _sb;

        public PlainTextSink(StringBuilder sb) => _sb = sb;

        public void Accept(in StyledRun run) => _sb.Append(run.Text);
    }
}
