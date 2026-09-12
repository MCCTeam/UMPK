using System.Globalization;
using Umpk.Text.Serialization;

namespace Umpk.Text;

/// <summary>Flattens a <see cref="Component"/> tree into a sequence of <see cref="StyledRun"/>s: style inheritance resolved, translation keys substituted, and (optionally) legacy section-sign codes decoded. One shared traversal supports renderers with different sinks; <see cref="Component.ToPlainText"/> uses the same walk with legacy-code decoding disabled.</summary>
public static class ComponentFlattener
{
    /// <summary>Flattens <paramref name="component"/>, pushing each resolved run to <paramref name="sink"/> in document order. Generic over <typeparamref name="TSink"/> so a struct sink devirtualizes and the walk performs no allocation beyond what the sink itself allocates.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static void Flatten<TSink>(Component component, TSink sink, in ComponentFlattenOptions options)
        where TSink : IStyledRunSink
    {
        ArgumentNullException.ThrowIfNull(component);
        AppendComponent(component, options.BaseStyle, sink, options);
    }

    /// <summary>Flattens <paramref name="component"/> into a list of runs.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static IReadOnlyList<StyledRun> Flatten(Component component, in ComponentFlattenOptions options)
    {
        var runs = new List<StyledRun>();
        Flatten(component, new ListSink(runs), options);
        return runs;
    }

    /// <summary>Flattens <paramref name="component"/> into a list of runs, using the vanilla defaults.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static IReadOnlyList<StyledRun> Flatten(Component component) =>
        Flatten(component, ComponentFlattenOptions.Default);

    /// <summary>Flattens <paramref name="component"/> into a list of runs, resolving translation keys through <paramref name="translations"/> and otherwise using the vanilla defaults.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public static IReadOnlyList<StyledRun> Flatten(Component component, ITranslationSource? translations) =>
        Flatten(component, ComponentFlattenOptions.For(translations));

    private static void AppendComponent<TSink>(Component component, Style inherited, TSink sink, in ComponentFlattenOptions options)
        where TSink : IStyledRunSink
    {
        Style effective = component.Style.ApplyTo(inherited);
        AppendContent(component.Content, effective, sink, options);
        foreach (Component child in component.Children)
            AppendComponent(child, effective, sink, options);

    }

    private static void AppendContent<TSink>(ComponentContent content, Style style, TSink sink, in ComponentFlattenOptions options)
        where TSink : IStyledRunSink
    {
        switch (content)
        {
            case TextContent text:
                AppendText(text.Text, style, sink, options);
                break;

            case TranslatableContent translatable:
                AppendTranslatable(translatable, style, sink, options);
                break;

            case SelectorContent selector:
                AppendRun(selector.Pattern, style, sink);
                break;

            case KeybindContent keybind:
                AppendRun(keybind.Keybind, style, sink);
                break;

            case NbtContent nbt:
                AppendRun(nbt.NbtPath, style, sink);
                break;

            case ScoreContent:
                // No live scoreboard here; resolving against game state is a host concern.
                break;
        }
    }

    // Literal text may carry legacy section-sign codes (some servers send them even in modern JSON components). Decode those through LegacyText.Decompose so the codes turn into styled runs instead of leaking into StyledRun.Text; the decoded runs' accumulator starts at the current effective style.
    private static void AppendText<TSink>(string text, Style style, TSink sink, in ComponentFlattenOptions options)
        where TSink : IStyledRunSink
    {
        if (text.Length == 0)
            return;

        if (options.DecodeLegacyCodes && text.IndexOf(LegacyText.Prefix) >= 0)
        {
            LegacyText.Decompose(text, style, sink);
            return;
        }

        AppendRun(text, style, sink);
    }

    // Literal segments between placeholders go through AppendText (so embedded legacy codes decode), and argument components recurse with the outer style as their inherited parent, so each argument resolves its own style against the style in scope at the placeholder.
    private static void AppendTranslatable<TSink>(TranslatableContent content, Style style, TSink sink, in ComponentFlattenOptions options)
        where TSink : IStyledRunSink
    {
        string? template = null;
        if (options.Translations is not null && options.Translations.TryResolve(content.Key, out string? resolved))
            template = resolved;

        template ??= content.Fallback ?? content.Key;

        int autoIndex = 0;
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c != '%')
            {
                int start = i;
                while (i < template.Length && template[i] != '%')
                    i++;

                AppendText(template[start..i], style, sink, options);
                continue;
            }

            i++;
            if (i >= template.Length)
            {
                // Trailing lone percent; vanilla translation-template decomposition throws, we emit it verbatim.
                AppendRun("%", style, sink);
                break;
            }

            // Optional positional index: digits followed by '$'.
            int explicitIndex = -1;
            int digitsStart = i;
            while (i < template.Length && char.IsAsciiDigit(template[i]))
                i++;

            if (i > digitsStart && i < template.Length && template[i] == '$')
            {
                explicitIndex = int.Parse(template.AsSpan(digitsStart, i - digitsStart), CultureInfo.InvariantCulture) - 1;
                i++;
            }
            else
            {
                // Not a positional group; rewind to just after the percent.
                i = digitsStart;
            }

            if (i >= template.Length)
            {
                AppendRun("%", style, sink);
                break;
            }

            char conversion = template[i];
            i++;
            switch (conversion)
            {
                case '%':
                    AppendRun("%", style, sink);
                    break;

                case 's':
                    int argIndex = explicitIndex >= 0 ? explicitIndex : autoIndex++;
                    if (argIndex >= 0 && argIndex < content.Args.Count)
                        AppendComponent(content.Args[argIndex], style, sink, options);

                    // Out-of-range index; vanilla getArgument throws, we emit nothing for that slot.
                    break;

                default:
                    // Unsupported conversion; vanilla translation-template decomposition throws TranslatableFormatException, we emit '%' plus the char verbatim. Real case: 1.8.9's commands.setworldspawn.success is "Set the world spawn point to (%d, %d, %d)".
                    AppendRun("%", style, sink);
                    AppendRun(conversion.ToString(), style, sink);
                    break;
            }
        }
    }

    private static void AppendRun<TSink>(string text, Style style, TSink sink)
        where TSink : IStyledRunSink
    {
        if (text.Length == 0)
            return;

        sink.Accept(new StyledRun(text, style));
    }

    // Wraps a List<StyledRun> as a reference-semantic struct sink: the struct is copied by value through the recursive walk, but every copy shares the same underlying list.
    private readonly struct ListSink : IStyledRunSink
    {
        private readonly List<StyledRun> _runs;

        public ListSink(List<StyledRun> runs) => _runs = runs;

        public void Accept(in StyledRun run) => _runs.Add(run);
    }
}
