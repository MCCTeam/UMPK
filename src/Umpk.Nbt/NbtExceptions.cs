namespace Umpk.Nbt;

/// <summary>Base type for every malformed-NBT condition this library raises. All decode-time failures (truncation, absurd lengths, depth or size overruns, illegal type ids) surface as this type or a derived type, never as <see cref="OutOfMemoryException"/> or index-range faults.</summary>
public class NbtFormatException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public NbtFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public NbtFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Thrown when nesting exceeds the accounter's depth limit.</summary>
public sealed class NbtDepthLimitException : NbtFormatException
{
    /// <summary>Creates the exception with a message.</summary>
    public NbtDepthLimitException(string message)
        : base(message)
    {
    }
}

/// <summary>Thrown when the running byte budget would be exceeded.</summary>
public sealed class NbtSizeLimitException : NbtFormatException
{
    /// <summary>Creates the exception with a message.</summary>
    public NbtSizeLimitException(string message)
        : base(message)
    {
    }
}
