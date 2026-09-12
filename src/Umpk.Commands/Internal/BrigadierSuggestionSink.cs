using Brigadier.NET.Suggestion;

namespace Umpk.Commands.Internal;

/// <summary>Internal adapter presenting a Brigadier <see cref="SuggestionsBuilder"/> as UMPK's <see cref="ISuggestionSink"/>.</summary>
/// <remarks>UMPK applies case-insensitive prefix filtering against <see cref="ISuggestionSink.Remaining"/> before forwarding a candidate to Brigadier. Brigadier.NET's own <c>SuggestionsBuilder.Suggest</c> does not prefix-filter (its built-in argument types filter themselves, for example <c>BoolArgumentType.ListSuggestions</c>). Filtering here gives UMPK consumers the vanilla-like behavior their custom providers expect, so they can emit their full candidate set unfiltered.</remarks>
internal sealed class BrigadierSuggestionSink : ISuggestionSink
{
    private readonly SuggestionsBuilder _builder;

    internal BrigadierSuggestionSink(SuggestionsBuilder builder)
    {
        _builder = builder;
    }

    public string Remaining => _builder.Remaining;

    public void Suggest(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Matches(text))
            _builder.Suggest(text);

    }

    public void Suggest(string text, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tooltip);
        if (Matches(text))
            _builder.Suggest(text, new Brigadier.NET.LiteralMessage(tooltip));

    }

    private bool Matches(string text) =>
        text.StartsWith(_builder.RemainingLowerCase, StringComparison.OrdinalIgnoreCase);
}
