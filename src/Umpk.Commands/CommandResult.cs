namespace Umpk.Commands;

/// <summary>The outcome of dispatching a command through <see cref="CommandService{TSource}.ExecuteAsync"/>.</summary>
/// <remarks>This is a value report, not a mutable command source. Success carries the numeric result a Brigadier command body returns (Mojang's convention: a positive value means the command ran). A parse or execution failure is reported without throwing, with the failure position and message preserved so a host can point the user at the offending token.</remarks>
public sealed class CommandResult
{
    private CommandResult(bool success, int value, string? errorMessage, int errorPosition)
    {
        Success = success;
        Value = value;
        ErrorMessage = errorMessage;
        ErrorPosition = errorPosition;
    }

    /// <summary>True when the command parsed and executed without error.</summary>
    public bool Success { get; }

    /// <summary>The numeric result returned by the executed command body; <c>0</c> on failure.</summary>
    public int Value { get; }

    /// <summary>A human-readable syntax/execution error in English, or <c>null</c> on success.</summary>
    public string? ErrorMessage { get; }

    /// <summary>The cursor position where parsing failed, or <c>-1</c> when not applicable.</summary>
    public int ErrorPosition { get; }

    /// <summary>Creates a successful result carrying the command's numeric return value.</summary>
    public static CommandResult Ok(int value) => new(true, value, null, -1);

    /// <summary>Creates a failed result carrying an error message and optional cursor position.</summary>
    public static CommandResult Failure(string errorMessage, int errorPosition = -1) =>
        new(false, 0, errorMessage, errorPosition);
}
