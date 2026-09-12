namespace Umpk.Nbt;

/// <summary>Base type of the NBT document model. Every concrete tag (<see cref="NbtByte"/>, <see cref="NbtCompound"/>, and so on) derives from this. Instances are mutable where the tag holds a collection; scalar tags are immutable value carriers. The type is closed: the twelve derived types cover the complete wire tag set.</summary>
public abstract class NbtTag
{
    private protected NbtTag()
    {
    }

    /// <summary>The wire type id of this tag.</summary>
    public abstract NbtTagType Type { get; }

    /// <summary>Produces a deep, independent copy of this tag.</summary>
    public abstract NbtTag Copy();

    /// <summary>Returns the canonical SNBT rendering of this tag.</summary>
    public override string ToString() => Snbt.SnbtPrinter.Print(this);
}
