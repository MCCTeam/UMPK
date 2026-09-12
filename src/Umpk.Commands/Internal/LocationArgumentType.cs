using Brigadier.NET;
using Brigadier.NET.Context;
using Brigadier.NET.Exceptions;
using Brigadier.NET.Suggestion;
using Brig = Brigadier.NET.ArgumentTypes;
using StringReader = Brigadier.NET.StringReader;

namespace Umpk.Commands.Internal;

/// <summary>Parses a coordinate triple (absolute, <c>~</c>-relative, or a fully local <c>^</c> triple) into a <see cref="CommandLocation"/>. This is client-side command grammar, not wire protocol, so no protocol feature selects it.</summary>
/// <remarks>Two deliberate additive extensions are accepted: a fullwidth tilde (<c>～</c>, U+FF5E) is normalized to <c>~</c>, and a signed <c>~+n</c> offset is accepted alongside the unsigned <c>~n</c> form. Neither changes the meaning of an otherwise valid triple.</remarks>
internal sealed class LocationArgumentType : Brig.IArgumentType<CommandLocation>
{
    private const char FullwidthTilde = '～';

    // Keep coordinate parse failures concise and consistent with familiar command wording.
    private static readonly SimpleCommandExceptionType MixedType = new(new LiteralMessage(
        "Cannot mix world & local coordinates (everything must either use ^ or not)"));

    private static readonly SimpleCommandExceptionType Incomplete = new(new LiteralMessage(
        "Incomplete (expected 3 coordinates)"));

    private static readonly SimpleCommandExceptionType ExpectedCoordinate = new(new LiteralMessage(
        "Expected a coordinate"));

    private readonly bool _centerCorrect;

    internal LocationArgumentType(bool centerCorrect)
    {
        _centerCorrect = centerCorrect;
    }

    /// <inheritdoc/>
    public IEnumerable<string> Examples =>
        ["0 0 0", "~ ~ ~", "^ ^ ^", "^1 ^ ^-5", "0.1 -0.5 .9", "~0.5 ~1 ~-5"];

    /// <summary>A triple is local when its first character is <c>^</c>; anything else is parsed as an absolute/relative triple.</summary>
    /// <inheritdoc/>
    public CommandLocation Parse(IStringReader reader) =>
        reader.CanRead() && reader.Peek() == '^' ? ParseLocal(reader) : ParseAbsolute(reader);

    /// <summary>An empty token offers the three prefix depths of the default triple (<c>~</c>, <c>~ ~</c>, <c>~ ~ ~</c>, or their <c>^</c> equivalents once the typed text starts with <c>^</c>). A partially typed coordinate is extended with placeholders for unfinished axes.</summary>
    public Task<Suggestions> ListSuggestions<TSource>(CommandContext<TSource> context, SuggestionsBuilder builder)
    {
        string remaining = builder.Remaining;
        bool local = remaining.Length > 0 && remaining[0] == '^';
        string axis = local ? "^" : "~";

        var candidates = new List<string>();
        if (remaining.Length == 0)
            AddIfValid(candidates, axis, $"{axis} {axis}", $"{axis} {axis} {axis}");

        else
        {
            string[] tokens = remaining.Split(' ');
            if (tokens.Length == 1)
                AddIfValid(candidates, $"{tokens[0]} {axis}", $"{tokens[0]} {axis} {axis}");

            else if (tokens.Length == 2)
                AddIfValid(candidates, $"{tokens[0]} {tokens[1]} {axis}");

        }

        string typedLower = builder.RemainingLowerCase;
        foreach (string candidate in candidates)
            if (SuggestionMatching.MatchesSubStr(typedLower, candidate.ToLowerInvariant()))
                builder.Suggest(candidate);

        return builder.BuildAsync();
    }

    /// <summary>Adds every form in <paramref name="progressiveForms"/> (shallowest to deepest, the last entry being the fully specified triple) when the complete form parses successfully.</summary>
    private void AddIfValid(List<string> candidates, params string[] progressiveForms)
    {
        if (!Validate(progressiveForms[^1]))
            return;

        candidates.AddRange(progressiveForms);
    }

    private bool Validate(string candidate)
    {
        try
        {
            Parse(new StringReader(candidate));
            return true;
        }
        catch (CommandSyntaxException)
        {
            return false;
        }
    }

    /// <summary>Reads X, a required single-space separator, Y, another separator, then Z. X and Z honor <see cref="_centerCorrect"/>; Y never does because it represents floor height.</summary>
    private CommandLocation ParseAbsolute(IStringReader reader)
    {
        int tripleStart = reader.Cursor;
        (bool xRelative, double x) = ParseWorldCoordinate(reader, _centerCorrect);
        RequireSeparator(reader, tripleStart);
        (bool yRelative, double y) = ParseWorldCoordinate(reader, centerCorrect: false);
        RequireSeparator(reader, tripleStart);
        (bool zRelative, double z) = ParseWorldCoordinate(reader, _centerCorrect);

        byte relativity = 0;
        if (xRelative)
            relativity |= 0b001;

        if (yRelative)
            relativity |= 0b010;

        if (zRelative)
            relativity |= 0b100;

        return new CommandLocation(x, y, z, IsLocal: false, relativity);
    }

    /// <summary>A component starting with <c>^</c> in an absolute/relative triple is a mixed-type error. This can occur for Y or Z because a leading caret on X routes the entire triple to <see cref="ParseLocal"/>. A leading <c>~</c> or <c>～</c> marks the value as an offset. Centre-correction adds 0.5 to an absolute value whose consumed text contains no '.', so "10" becomes 10.5 while "10.0" stays 10.0; a relative value is never corrected.</summary>
    private static (bool Relative, double Value) ParseWorldCoordinate(IStringReader reader, bool centerCorrect)
    {
        if (reader.CanRead() && reader.Peek() == '^')
            throw MixedType.CreateWithContext(reader);

        if (!reader.CanRead())
            throw ExpectedCoordinate.CreateWithContext(reader);

        bool relative = TryConsumeTilde(reader);

        int start = reader.Cursor;
        double value = ReadCoordinateValue(reader, relative);
        string text = reader.String.Substring(start, reader.Cursor - start);

        if (relative && text.Length == 0)
            return (true, 0.0);

        if (centerCorrect && !relative && !text.Contains('.'))
            value += 0.5;

        return (relative, value);
    }

    private static bool TryConsumeTilde(IStringReader reader)
    {
        if (!reader.CanRead())
            return false;

        char c = reader.Peek();
        if (c != '~' && c != FullwidthTilde)
            return false;

        reader.Skip();
        return true;
    }

    private static double ReadCoordinateValue(IStringReader reader, bool relative)
    {
        // Accept a leading '+' only after '~' so users can write an explicit positive relative offset. Absolute "+5" remains invalid.
        if (relative && reader.CanRead() && reader.Peek() == '+')
        {
            reader.Skip();
            return reader.ReadDouble();
        }

        return reader.CanRead() && reader.Peek() != ' ' ? reader.ReadDouble() : 0.0;
    }

    /// <summary>Reads left, a required single-space separator, up, another separator, then forwards. Each component must start with <c>^</c>, so a local triple cannot mix in a tilde or bare value.</summary>
    private static CommandLocation ParseLocal(IStringReader reader)
    {
        int tripleStart = reader.Cursor;
        double left = ReadCaretDouble(reader, tripleStart);
        RequireSeparator(reader, tripleStart);
        double up = ReadCaretDouble(reader, tripleStart);
        RequireSeparator(reader, tripleStart);
        double forwards = ReadCaretDouble(reader, tripleStart);

        return new CommandLocation(left, up, forwards, IsLocal: true, Relativity: 0);
    }

    private static double ReadCaretDouble(IStringReader reader, int tripleStart)
    {
        if (!reader.CanRead())
            throw ExpectedCoordinate.CreateWithContext(reader);

        if (reader.Peek() != '^')
        {
            reader.Cursor = tripleStart;
            throw MixedType.CreateWithContext(reader);
        }

        reader.Skip();
        return reader.CanRead() && reader.Peek() != ' ' ? reader.ReadDouble() : 0.0;
    }

    /// <summary>Both triple shapes require one literal space between components. A missing separator resets the cursor to the triple's start before reporting an incomplete coordinate.</summary>
    private static void RequireSeparator(IStringReader reader, int tripleStart)
    {
        if (reader.CanRead() && reader.Peek() == ' ')
        {
            reader.Skip();
            return;
        }

        reader.Cursor = tripleStart;
        throw Incomplete.CreateWithContext(reader);
    }
}
