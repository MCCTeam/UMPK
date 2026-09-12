namespace Umpk.Protocol.Java.Codecs;

/// <summary>The bit layout a version uses when packing a <c>BlockPos</c> into a 64-bit long. The variation is an explicit codec parameter, never a protocol-version branch in the primitive layer.</summary>
public enum BlockPosLayout
{
    /// <summary>Pre-1.14 layout: <c>x (26 bits) | y (12 bits) | z (26 bits)</c>, y in the middle.</summary>
    PrePacked114 = 0,

    /// <summary>1.14+ layout: <c>x (26 bits) | z (26 bits) | y (12 bits)</c>, y in the low bits.</summary>
    Packed114 = 1,
}
