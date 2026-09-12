namespace Umpk.Nbt;

/// <summary>Selects which Java NBT root-framing convention a reader or writer uses. The flavor is an explicit parameter rather than a protocol-version branch, matching the architecture's codec model. All three flavors share identical tag bodies; they differ only in how the root tag is framed.</summary>
public enum NbtWireFormat
{
    /// <summary>Type byte, then a modified-UTF-8 root name (usually empty), then the tag body. This is the disk format for every version and the network format before 1.20.2. The root must be a compound.</summary>
    JavaNamedRoot = 0,

    /// <summary>Type byte, then the tag body with no root name. This is the network format from 1.20.2 onward. A bare <see cref="NbtTagType.End"/> byte decodes to <see cref="NbtEnd.Instance"/>, the null-NBT marker.</summary>
    JavaUnnamedRoot = 1,

    /// <summary>Same framing as <see cref="JavaUnnamedRoot"/> (type byte then body, no root name), but the root tag may legitimately be any type, notably a bare <see cref="NbtString"/>. Used for 1.20.3+ text-component NBT where a component can be a plain string.</summary>
    JavaRootTagOrString = 2,
}
