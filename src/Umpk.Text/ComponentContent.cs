using System.Collections.ObjectModel;

namespace Umpk.Text;

/// <summary>The typed body of a <see cref="Component"/>: a closed union of the six wire content kinds (<see cref="TextContent"/>, <see cref="TranslatableContent"/>, <see cref="ScoreContent"/>, <see cref="SelectorContent"/>, <see cref="KeybindContent"/>, <see cref="NbtContent"/>). The union is closed: the constructor is private-protected so only these six types can derive from it.</summary>
public abstract record ComponentContent
{
    private protected ComponentContent()
    {
    }

    /// <summary>The vanilla content-type name (<c>text</c>, <c>translatable</c>, and so on).</summary>
    public abstract string TypeName { get; }

    /// <summary>Dispatches to the matching visitor method (double-dispatch entry point).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is null.</exception>
    public abstract TResult Accept<TResult>(IComponentContentVisitor<TResult> visitor);
}

/// <summary>Literal text content (<c>text</c>). The default and simplest content kind; a bare JSON or NBT string decodes to this. Mirrors <c>PlainTextContents</c>.</summary>
public sealed record TextContent : ComponentContent
{
    /// <summary>The shared empty-text content.</summary>
    public static readonly TextContent Empty = new(string.Empty);

    /// <summary>Creates literal text content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public TextContent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>The literal text.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override string TypeName => "text";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IComponentContentVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitText(this);
    }
}

/// <summary>A localizable message (<c>translatable</c>): a translation key, optional literal fallback, and substitution arguments. Arguments are components; a plain value is wrapped as literal text.</summary>
public sealed record TranslatableContent : ComponentContent
{
    /// <summary>Creates translatable content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="args"/> is null.</exception>
    public TranslatableContent(string key, string? fallback, IReadOnlyList<Component> args)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(args);
        Key = key;
        Fallback = fallback;
        Args = args as ReadOnlyCollection<Component> ?? new ReadOnlyCollection<Component>([.. args]);
    }

    /// <summary>The translation key (for example <c>chat.type.text</c>).</summary>
    public string Key { get; }

    /// <summary>The literal fallback used when the key is unknown, or null.</summary>
    public string? Fallback { get; }

    /// <summary>The substitution arguments, in order.</summary>
    public IReadOnlyList<Component> Args { get; }

    /// <inheritdoc/>
    public override string TypeName => "translatable";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IComponentContentVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitTranslatable(this);
    }

    /// <inheritdoc/>
    public bool Equals(TranslatableContent? other) =>
        other is not null
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(Fallback, other.Fallback, StringComparison.Ordinal)
        && Args.SequenceEqual(other.Args);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Key, StringComparer.Ordinal);
        hash.Add(Fallback, StringComparer.Ordinal);
        foreach (Component arg in Args)
            hash.Add(arg);

        return hash.ToHashCode();
    }
}

/// <summary>A scoreboard value (<c>score</c>): a holder name (or selector) and an objective. Mirrors <c>ScoreContents</c>. Resolution against a live scoreboard is a host concern; the model carries the raw fields.</summary>
public sealed record ScoreContent : ComponentContent
{
    /// <summary>Creates score content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="objective"/> is null.</exception>
    public ScoreContent(string name, string objective)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(objective);
        Name = name;
        Objective = objective;
    }

    /// <summary>The score holder name or entity selector.</summary>
    public string Name { get; }

    /// <summary>The objective name.</summary>
    public string Objective { get; }

    /// <inheritdoc/>
    public override string TypeName => "score";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IComponentContentVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitScore(this);
    }
}

/// <summary>An entity selector (<c>selector</c>): a selector pattern and an optional separator component. Mirrors <c>SelectorContents</c>.</summary>
public sealed record SelectorContent : ComponentContent
{
    /// <summary>Creates selector content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is null.</exception>
    public SelectorContent(string pattern, Component? separator)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        Pattern = pattern;
        Separator = separator;
    }

    /// <summary>The entity selector pattern (for example <c>@a</c>).</summary>
    public string Pattern { get; }

    /// <summary>The optional separator component inserted between matched entities; null if absent.</summary>
    public Component? Separator { get; }

    /// <inheritdoc/>
    public override string TypeName => "selector";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IComponentContentVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitSelector(this);
    }
}

/// <summary>A keybind reference (<c>keybind</c>): the name of a client keybinding, rendered as the bound key. Mirrors <c>KeybindContents</c>.</summary>
public sealed record KeybindContent : ComponentContent
{
    /// <summary>Creates keybind content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="keybind"/> is null.</exception>
    public KeybindContent(string keybind)
    {
        ArgumentNullException.ThrowIfNull(keybind);
        Keybind = keybind;
    }

    /// <summary>The keybind name (for example <c>key.jump</c>).</summary>
    public string Keybind { get; }

    /// <inheritdoc/>
    public override string TypeName => "keybind";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IComponentContentVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitKeybind(this);
    }
}

/// <summary>An NBT-value reference (<c>nbt</c>): an NBT path, a data source (block/entity/storage), the <c>interpret</c> flag, and an optional separator. Mirrors <c>NbtContents</c>. Resolution against live game data is a host concern; the model carries the raw fields.</summary>
public sealed record NbtContent : ComponentContent
{
    /// <summary>Creates NBT content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nbtPath"/>, <paramref name="source"/>, or <paramref name="sourceValue"/> is null.</exception>
    public NbtContent(string nbtPath, bool interpret, Component? separator, NbtDataSource source, string sourceValue)
    {
        ArgumentNullException.ThrowIfNull(nbtPath);
        ArgumentNullException.ThrowIfNull(sourceValue);
        NbtPath = nbtPath;
        Interpret = interpret;
        Separator = separator;
        Source = source;
        SourceValue = sourceValue;
    }

    /// <summary>The NBT path pattern.</summary>
    public string NbtPath { get; }

    /// <summary>Whether resolved strings are themselves parsed as components.</summary>
    public bool Interpret { get; }

    /// <summary>The optional separator component; null if absent.</summary>
    public Component? Separator { get; }

    /// <summary>Which data source the NBT is read from.</summary>
    public NbtDataSource Source { get; }

    /// <summary>The source argument: a block position string, entity selector, or storage identifier.</summary>
    public string SourceValue { get; }

    /// <inheritdoc/>
    public override string TypeName => "nbt";

    /// <inheritdoc/>
    public override TResult Accept<TResult>(IComponentContentVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitNbt(this);
    }
}

/// <summary>The data source an <see cref="NbtContent"/> reads from. Mirrors the <c>DataSources</c> registry.</summary>
public enum NbtDataSource
{
    /// <summary>A block entity at a position (<c>block</c>).</summary>
    Block,

    /// <summary>An entity selected by a pattern (<c>entity</c>).</summary>
    Entity,

    /// <summary>A command-storage identifier (<c>storage</c>).</summary>
    Storage,
}
