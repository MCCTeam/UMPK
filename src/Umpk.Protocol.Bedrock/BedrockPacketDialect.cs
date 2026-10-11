namespace Umpk.Protocol.Bedrock;

/// <summary>The packet layout family a profile decodes, tracked independently of the numeric game protocol.</summary>
/// <remarks>Packet identifiers and field layouts can shift while the protocol number stays put, so codecs bind to this dialect rather than to <see cref="GameVersion.Protocol"/>; today only the 2168-derived base layout backing release 26.45 is supported.</remarks>
public enum BedrockPacketDialect
{
    /// <summary>No layout family was selected; profiles reject this value instead of resolving forward silently.</summary>
    Unknown = 0,
    /// <summary>The 2168-derived base layout family used by the 26.45 support.</summary>
    Base2168 = 1,
}
