using System.Collections.Immutable;

namespace Umpk.Commands;

/// <summary>A single completion candidate: replacement text for a span of the input plus an optional tooltip.</summary>
/// <param name="Text">The text to insert in place of the covered input span.</param>
/// <param name="StartIndex">The inclusive start index of the input span this suggestion replaces.</param>
/// <param name="EndIndex">The exclusive end index of the input span this suggestion replaces.</param>
/// <param name="Tooltip">Optional descriptive tooltip, or <c>null</c>.</param>
public readonly record struct CompletionSuggestion(string Text, int StartIndex, int EndIndex, string? Tooltip);

/// <summary>The result of a completion query through <see cref="CommandService{TSource}.CompleteAsync"/>: the shared replacement range and the ordered candidate list.</summary>
public sealed class CompletionResult
{
    /// <summary>An empty completion result covering the cursor position.</summary>
    public static CompletionResult Empty { get; } =
        new(0, 0, ImmutableArray<CompletionSuggestion>.Empty);

    /// <summary>Creates a completion result.</summary>
    public CompletionResult(int rangeStart, int rangeEnd, ImmutableArray<CompletionSuggestion> suggestions)
    {
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        Suggestions = suggestions;
    }

    /// <summary>The inclusive start index of the input range the suggestions replace.</summary>
    public int RangeStart { get; }

    /// <summary>The exclusive end index of the input range the suggestions replace.</summary>
    public int RangeEnd { get; }

    /// <summary>The ordered completion candidates.</summary>
    public ImmutableArray<CompletionSuggestion> Suggestions { get; }
}
