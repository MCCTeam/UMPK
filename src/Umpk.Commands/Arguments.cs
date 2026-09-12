using Umpk.Commands.Internal;
using Umpk.Geometry;
using Brig = Brigadier.NET;

namespace Umpk.Commands;

/// <summary>Factory for UMPK's library argument types. All of them are dataset-free: parsing needs no ambient state, and suggestion providers (attached at the builder) receive the source to query live state. <see cref="Location"/> and <see cref="ChunkPos"/> read like "vanilla" types but are dataset-free too - they are client-side command grammar (Brigadier's own coordinate syntax), unchanged since Minecraft 1.13 introduced Brigadier, so no per-protocol <c>features.json</c> axis selects them. Registry-entry argument types (item ids, block ids, ...) are a separate, later surface that does need a per-protocol registry and is not part of this one.</summary>
public static class Arguments
{
    /// <summary>A single unquoted word.</summary>
    public static IArgumentType<string> Word() =>
        new BrigadierArgumentType<string>(Brig.Arguments.Word());

    /// <summary>A quotable string (a bare word, or a double/single-quoted phrase).</summary>
    public static IArgumentType<string> QuotableString() =>
        new BrigadierArgumentType<string>(Brig.Arguments.String());

    /// <summary>A greedy string consuming the remainder of the input.</summary>
    public static IArgumentType<string> GreedyString() =>
        new BrigadierArgumentType<string>(Brig.Arguments.GreedyString());

    /// <summary>A boolean (<c>true</c> or <c>false</c>).</summary>
    public static IArgumentType<bool> Bool() =>
        new BrigadierArgumentType<bool>(Brig.Arguments.Bool());

    /// <summary>An unbounded 32-bit integer.</summary>
    public static IArgumentType<int> Integer() =>
        new BrigadierArgumentType<int>(Brig.Arguments.Integer());

    /// <summary>A 32-bit integer constrained to the inclusive range.</summary>
    public static IArgumentType<int> Integer(int min, int max) =>
        new BrigadierArgumentType<int>(Brig.Arguments.Integer(min, max));

    /// <summary>An unbounded 64-bit integer.</summary>
    public static IArgumentType<long> Long() =>
        new BrigadierArgumentType<long>(Brig.Arguments.Long());

    /// <summary>A 64-bit integer constrained to the inclusive range.</summary>
    public static IArgumentType<long> Long(long min, long max) =>
        new BrigadierArgumentType<long>(Brig.Arguments.Long(min, max));

    /// <summary>An unbounded single-precision float.</summary>
    public static IArgumentType<float> Float() =>
        new BrigadierArgumentType<float>(Brig.Arguments.Float());

    /// <summary>A single-precision float constrained to the inclusive range.</summary>
    public static IArgumentType<float> Float(float min, float max) =>
        new BrigadierArgumentType<float>(Brig.Arguments.Float(min, max));

    /// <summary>An unbounded double-precision float.</summary>
    public static IArgumentType<double> Double() =>
        new BrigadierArgumentType<double>(Brig.Arguments.Double());

    /// <summary>A double-precision float constrained to the inclusive range.</summary>
    public static IArgumentType<double> Double(double min, double max) =>
        new BrigadierArgumentType<double>(Brig.Arguments.Double(min, max));

    /// <summary>A namespaced <see cref="Identifier"/> (<c>namespace:path</c>), defaulting to <c>minecraft</c>.</summary>
    public static IArgumentType<Identifier> Identifier() =>
        new BrigadierArgumentType<Identifier>(IdentifierArgumentType.Instance);

    /// <summary>A namespaced <see cref="Identifier"/> naming an entry of <paramref name="registryId"/>. Parses exactly like <see cref="Identifier()"/> and NEVER validates the parsed id against the registry: a registry can be entirely absent (no session yet, or one that has not loaded its registries). A command body that cares whether the id names a real entry must validate it itself. Suggestions need the registry the parser does not: they require the command source to resolve an <see cref="IRegistrySuggestionSource"/> through <see cref="ICommandSource.GetService{T}"/>, and come back empty when it resolves to <c>null</c> or when the source has no entries for <paramref name="registryId"/>.</summary>
    public static IArgumentType<Identifier> RegistryId(Identifier registryId) =>
        new BrigadierArgumentType<Identifier>(new RegistryIdArgumentType(registryId));

    /// <summary>A coordinate triple: absolute values, player-relative <c>~</c> offsets, or a fully local <c>^</c> triple. See <see cref="CommandLocation"/> for the resolution rules.</summary>
    /// <param name="centerCorrect">When <c>true</c>, a bare-integer absolute X or Z value is offset by 0.5 to land in the block's centre. Y is never centre-corrected because it represents floor height. A value that already contains '.', and every relative or local axis, is never corrected.</param>
    public static IArgumentType<CommandLocation> Location(bool centerCorrect = true) =>
        new BrigadierArgumentType<CommandLocation>(new LocationArgumentType(centerCorrect));

    /// <summary>A chunk coordinate pair containing two whitespace-separated integers.</summary>
    public static IArgumentType<ChunkPos> ChunkPos() =>
        new BrigadierArgumentType<ChunkPos>(ChunkPosArgumentType.Instance);
}
