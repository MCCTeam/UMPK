namespace Umpk.Commands;

/// <summary>Resolves the live entries of a named registry, for suggesting values of a <see cref="Arguments.RegistryId"/> argument. A command source exposes this (or not) through <see cref="ICommandSource.GetService{T}"/>; a source with no registries at all (no session, or a session that has not loaded any yet) simply resolves nothing, and every registry argument's suggestions come back empty rather than throwing. Parsing never needs this source at all - see <see cref="Arguments.RegistryId"/> for why.</summary>
public interface IRegistrySuggestionSource
{
    /// <summary>The keys currently registered under <paramref name="registryId"/>, or empty when unknown.</summary>
    IEnumerable<Identifier> GetEntries(Identifier registryId);
}
