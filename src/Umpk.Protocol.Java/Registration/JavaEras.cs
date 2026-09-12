namespace Umpk.Protocol.Java;

/// <summary>
/// The ten protocol boundaries that carry the majority of this library's timeline steps, named once so nine binding families stop re-deriving the same handful of eras in nine sets of words. This is a vocabulary, not a second source of truth: every member aliases a <see cref="JavaProtocols"/> constant.
/// <para>Two rules keep a second vocabulary for the same numbers from becoming a hazard, because swapping one era name for another is a one-token edit that reads plausibly and moves two bands. One timeline uses ONE vocabulary throughout, so a family whose boundaries are not all among these ten spells every step with <see cref="JavaProtocols"/>; and era names are <c>.From</c> arguments only, never part of a codec member name, so there is exactly one spelling per boundary per position.</para>
/// </summary>
internal static class JavaEras
{
    /// <summary>1.9 (107). Off-hand slot, the 1.9 metadata serializer table; more packets change wire here than at any other boundary.</summary>
    public const int Combat = JavaProtocols.V1_9;

    /// <summary>1.13 (393). The flattening: block and item ids stop being (id, damage) pairs.</summary>
    public const int Flattening = JavaProtocols.V1_13;

    /// <summary>1.14 (477). Sections gain the leading non-empty block-state count short and the heightmap NBT block; light moves to its own packet; biomes become chunk-level rather than per-section. Paletted containers themselves are older, from 1.9. The packed block position also moves y into the low bits (<see cref="Codecs.BlockPosLayout.Packed114"/>).</summary>
    public const int Palettes = JavaProtocols.V1_14;

    /// <summary>1.17 (755). The caves-and-cliffs metadata and chunk reshape. 1.16.4 is 754 and is a different band.</summary>
    public const int Caves = JavaProtocols.V1_17;

    /// <summary>1.19 (759). Chat signing generation 1; the argument-type registry becomes numeric.</summary>
    public const int ChatSigning = JavaProtocols.V1_19;

    /// <summary>1.20.2 (764). The configuration phase, and network NBT roots lose their name. This is the NBT framing boundary and nothing else: chat components are still JSON strings here, so 764 is a hybrid era of its own, unnamed-root NBT with JSON components.</summary>
    public const int ConfigurationPhase = JavaProtocols.V1_20_2;

    /// <summary>1.20.3 (765). TWO independent axes, one protocol number, and they must never share a flag. Chat components move from JSON strings to network NBT on the play and configuration paths (the 764 capture is <c>02 06 01 15 7b 22 74 65 78 74</c>, VarInt length then <c>{"text":</c>, against 765's <c>02 06 01 08 00 0a 54 65 73 74</c>, TAG_String). Independently, on the components that are still transported as JSON after this boundary, which on the supported set is the login disconnect reason and nothing else, a pure literal is written as a bare JSON string instead of <c>{"text":"..."}</c>.</summary>
    public const int ComponentNbtTransport = JavaProtocols.V1_20_3;

    /// <summary>1.20.5 (766). Item stacks become component patches.</summary>
    public const int ItemComponents = JavaProtocols.V1_20_5;

    /// <summary>1.21.2 (768). Container and slot ids widen from a signed byte to a VarInt; chunk long arrays become prefixed-raw.</summary>
    public const int WideIds = JavaProtocols.V1_21_2;

    /// <summary>1.21.5 (770). The modern component INTERACTION dialect and fixed-size chunk long arrays. Independent of <see cref="ItemComponents"/> and of <see cref="ComponentNbtTransport"/>.</summary>
    public const int ModernComponents = JavaProtocols.V1_21_5;
}
