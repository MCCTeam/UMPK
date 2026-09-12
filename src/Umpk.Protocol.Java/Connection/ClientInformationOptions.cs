using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>
/// Every field the client-information / client-settings announce carries, across all versions. A consumer sets what it cares about and leaves the rest at the documented defaults.
/// <para>The negotiated version decides how much of this reaches the wire: 1.8 carries no main hand, 1.17 adds <see cref="TextFilteringEnabled"/>, 1.18 adds <see cref="AllowsListing"/>, and 1.21.2 adds <see cref="ParticleStatus"/>. Fields a version does not carry are simply not written; nothing throws.</para>
/// </summary>
public sealed record ClientInformationOptions
{
    /// <summary>The advertised locale, for example <c>en_us</c>. Capped at 16 characters on the wire.</summary>
    public string Locale { get; init; } = "en_us";

    /// <summary>The advertised view distance in chunks.</summary>
    public int ViewDistance { get; init; } = 8;

    /// <summary>Which chat the client wants to receive. Defaults to <see cref="ChatVisibility.Full"/>.</summary>
    public ChatVisibility ChatVisibility { get; init; } = ChatVisibility.Full;

    /// <summary>Whether the client renders chat colors. Defaults to true.</summary>
    public bool ChatColors { get; init; } = true;

    /// <summary>The displayed skin parts. Defaults to <see cref="SkinParts.All"/> (<c>0x7F</c>).</summary>
    public SkinParts DisplayedSkinParts { get; init; } = SkinParts.All;

    /// <summary>The main hand. Defaults to <see cref="MainHand.Right"/>, vanilla's default. 1.9+ only.</summary>
    public MainHand MainHand { get; init; } = MainHand.Right;

    /// <summary>Whether server-side text filtering is requested. Defaults to false. 1.17+ only.</summary>
    public bool TextFilteringEnabled { get; init; }

    /// <summary>Whether this player may be listed in public server listings. Defaults to true. 1.18+ only.</summary>
    public bool AllowsListing { get; init; } = true;

    /// <summary>The particle detail level. Defaults to <see cref="ParticleStatus.All"/>. 1.21.2+ only.</summary>
    public ParticleStatus ParticleStatus { get; init; } = ParticleStatus.All;

    /// <summary>Builds the configuration-phase (1.20.2+) announce record from these options.</summary>
    public ServerboundClientInformationPacket ToConfigurationPacket() =>
        new(Locale, ClampedViewDistance, (int)ChatVisibility, ChatColors, (byte)DisplayedSkinParts,
            (int)MainHand, TextFilteringEnabled, AllowsListing, (int)ParticleStatus);

    /// <summary>Builds the play-phase announce record from these options.</summary>
    public ServerboundPlayClientInformationPacket ToPlayPacket() =>
        new(Locale, ClampedViewDistance, (int)ChatVisibility, ChatColors, (byte)DisplayedSkinParts,
            (int)MainHand, TextFilteringEnabled, AllowsListing, (int)ParticleStatus);

    /// <summary>View distance is a signed byte on the wire on every version, so an out-of-range option would otherwise wrap into a nonsensical value (a 200-chunk request arriving as -56).</summary>
    private sbyte ClampedViewDistance => (sbyte)Math.Clamp(ViewDistance, 2, sbyte.MaxValue);
}

/// <summary>Which chat the client wants to receive. Values are wire ordinals.</summary>
public enum ChatVisibility
{
    /// <summary>All chat.</summary>
    Full = 0,

    /// <summary>System messages and game info only.</summary>
    System = 1,

    /// <summary>No chat.</summary>
    Hidden = 2,
}

/// <summary>The player's main hand. Values are wire ordinals.</summary>
public enum MainHand
{
    /// <summary>Left-handed.</summary>
    Left = 0,

    /// <summary>Right-handed; vanilla's default.</summary>
    Right = 1,
}

/// <summary>The particle detail level. Values are wire ordinals. 1.21.2+ only.</summary>
public enum ParticleStatus
{
    /// <summary>All particles.</summary>
    All = 0,

    /// <summary>Reduced particles.</summary>
    Decreased = 1,

    /// <summary>Minimal particles.</summary>
    Minimal = 2,
}

/// <summary>The displayed skin-part bit flags. Each mask is <c>1 &lt;&lt; ordinal</c>.</summary>
[Flags]
public enum SkinParts : byte
{
    /// <summary>No skin parts shown.</summary>
    None = 0,

    /// <summary>The cape.</summary>
    Cape = 1 << 0,

    /// <summary>The jacket (torso overlay).</summary>
    Jacket = 1 << 1,

    /// <summary>The left sleeve.</summary>
    LeftSleeve = 1 << 2,

    /// <summary>The right sleeve.</summary>
    RightSleeve = 1 << 3,

    /// <summary>The left trouser leg.</summary>
    LeftPantsLeg = 1 << 4,

    /// <summary>The right trouser leg.</summary>
    RightPantsLeg = 1 << 5,

    /// <summary>The hat (head overlay).</summary>
    Hat = 1 << 6,

    /// <summary>Every skin part (<c>0x7F</c>).</summary>
    All = Cape | Jacket | LeftSleeve | RightSleeve | LeftPantsLeg | RightPantsLeg | Hat,
}
