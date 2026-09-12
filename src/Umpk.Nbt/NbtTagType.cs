namespace Umpk.Nbt;

/// <summary>The twelve NBT tag types plus <see cref="End"/>, numbered exactly as on the wire.</summary>
public enum NbtTagType : byte
{
    /// <summary>Terminator tag (id 0). Also the empty-network-root marker.</summary>
    End = 0,

    /// <summary>Signed 8-bit integer (id 1).</summary>
    Byte = 1,

    /// <summary>Signed big-endian 16-bit integer (id 2).</summary>
    Short = 2,

    /// <summary>Signed big-endian 32-bit integer (id 3).</summary>
    Int = 3,

    /// <summary>Signed big-endian 64-bit integer (id 4).</summary>
    Long = 4,

    /// <summary>Big-endian IEEE-754 single (id 5).</summary>
    Float = 5,

    /// <summary>Big-endian IEEE-754 double (id 6).</summary>
    Double = 6,

    /// <summary>Length-prefixed array of signed bytes (id 7).</summary>
    ByteArray = 7,

    /// <summary>Modified-UTF-8 string (id 8).</summary>
    String = 8,

    /// <summary>Homogeneous list of tags (id 9).</summary>
    List = 9,

    /// <summary>Order-preserving map of named tags (id 10).</summary>
    Compound = 10,

    /// <summary>Length-prefixed array of big-endian 32-bit integers (id 11).</summary>
    IntArray = 11,

    /// <summary>Length-prefixed array of big-endian 64-bit integers (id 12).</summary>
    LongArray = 12,
}
