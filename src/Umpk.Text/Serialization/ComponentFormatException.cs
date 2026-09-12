namespace Umpk.Text.Serialization;

/// <summary>Thrown when component data cannot be decoded from a wire form (JSON, NBT, or legacy codes) because it is malformed or structurally invalid. Analogous to the codec parse failures vanilla surfaces as error <c>DataResult</c>s.</summary>
public sealed class ComponentFormatException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public ComponentFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public ComponentFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
