using Brigadier.NET;
using Brigadier.NET.Context;
using Brigadier.NET.Suggestion;
using Brig = Brigadier.NET.ArgumentTypes;

namespace Umpk.Commands.Internal;

/// <summary>Parses a namespaced <see cref="Identifier"/> exactly like <see cref="IdentifierArgumentType"/> (reused directly - naming a registry adds no new parse grammar over a plain identifier one). Parsing never touches the named registry: a source can have none at all. A command body that cares whether the id is real must validate that itself. Suggestions are the part that needs the registry, and they come from the <see cref="IRegistrySuggestionSource"/> resolved by the command source.</summary>
/// <remarks>Matching is colon-aware: after a colon it compares the whole id; before a colon it compares the namespace and, for the default namespace, the path. The suggested text is always the complete <c>namespace:path</c> form. Separator matching is shared with <see cref="SuggestionMatching"/>.</remarks>
internal sealed class RegistryIdArgumentType : Brig.IArgumentType<Identifier>
{
    private readonly Identifier _registryId;

    internal RegistryIdArgumentType(Identifier registryId)
    {
        _registryId = registryId;
    }

    /// <inheritdoc/>
    public IEnumerable<string> Examples => IdentifierArgumentType.Instance.Examples;

    /// <inheritdoc/>
    public Identifier Parse(IStringReader reader) => IdentifierArgumentType.Instance.Parse(reader);

    /// <inheritdoc/>
    public Task<Suggestions> ListSuggestions<TSource>(CommandContext<TSource> context, SuggestionsBuilder builder)
    {
        if (context.Source is ICommandSource source &&
            source.GetService<IRegistrySuggestionSource>() is { } registrySource)
        {
            string typed = builder.RemainingLowerCase;
            bool typedNamespace = typed.Contains(':');

            foreach (var id in registrySource.GetEntries(_registryId))
            {
                bool matches = typedNamespace
                    ? SuggestionMatching.MatchesSubStr(typed, id.ToString())
                    : SuggestionMatching.MatchesSubStr(typed, id.Namespace)
                      || (id.IsMinecraft && SuggestionMatching.MatchesSubStr(typed, id.Path));

                if (matches)
                    builder.Suggest(id.ToString());

            }
        }

        return builder.BuildAsync();
    }
}
