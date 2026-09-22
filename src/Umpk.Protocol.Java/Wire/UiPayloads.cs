using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

// Scoreboard

/// <summary>A scoreboard number format (1.20.3+): how a score number renders. <c>Blank</c> hides it, <c>Styled</c> applies an NBT style tag, <c>Fixed</c> replaces it with a component. The wire is a registry-dispatched optional (blank=0, styled=1, fixed=2). Styled's raw style NBT is preserved.</summary>
/// <param name="Kind">The format kind (blank/styled/fixed), matching the registry id.</param>
/// <param name="StyledStyle">For <c>Styled</c>: the raw style tag preserved as network NBT; null otherwise.</param>
/// <param name="FixedContent">For <c>Fixed</c>: the replacement component; null otherwise.</param>
public sealed record ScoreNumberFormat(ScoreNumberFormatKind Kind, Umpk.Nbt.NbtTag? StyledStyle, Component? FixedContent);

/// <summary>The number-format registry kinds (blank=0, styled=1, fixed=2).</summary>
public enum ScoreNumberFormatKind
{
    /// <summary>No number is shown.</summary>
    Blank = 0,

    /// <summary>The number is shown with an NBT style applied.</summary>
    Styled = 1,

    /// <summary>The number is replaced by a fixed component.</summary>
    Fixed = 2,
}

/// <summary>The set-objective / scoreboard-objective mode byte (add=0, remove=1, change=2).</summary>
public enum ScoreboardObjectiveMode : byte
{
    /// <summary>Create the objective.</summary>
    Add = 0,

    /// <summary>Remove the objective.</summary>
    Remove = 1,

    /// <summary>Update the objective's display name / render type.</summary>
    Change = 2,
}

/// <summary>The add/change parameters of a team packet. Components are era-encoded by the codec. The color is the raw wire value: on 47 a signed byte ChatFormatting index (-1 for none), on 770 a Chat-formatting VarInt ordinal; on 776, an optional color id (0-15). It is carried as a nullable int so all three eras round-trip: null means "no color" (776 absent / 47's -1).</summary>
public sealed record TeamParameters(
    Component DisplayName,
    Component Prefix,
    Component Suffix,
    NameTagVisibility NameTagVisibility,
    CollisionRule CollisionRule,
    int? Color,
    byte Options);

/// <summary>The team packet method byte.</summary>
public enum TeamMethod : byte
{
    /// <summary>Create the team (parameters + member list).</summary>
    Add = 0,

    /// <summary>Remove the team.</summary>
    Remove = 1,

    /// <summary>Update the team parameters.</summary>
    Change = 2,

    /// <summary>Add members to the team.</summary>
    AddPlayers = 3,

    /// <summary>Remove members from the team.</summary>
    RemovePlayers = 4,
}

// Player list

/// <summary>A remote chat session record (1.19.3+ INITIALIZE_CHAT payload): the session uuid plus the profile public key (expiry, DER public key bytes, and Mojang's key signature). The signing subsystem verifies it; here it is a typed record so the tab-list payload round-trips with the raw bytes.</summary>
public sealed record RemoteChatSession(Guid SessionId, long ExpiresAtMillis, byte[] PublicKey, byte[] KeySignature);

/// <summary>One entry of a modern player-info update. Which fields are meaningful depends on the packet's action set; absent-action fields keep their defaults. This mirrors the per-entry wire payload.</summary>
public sealed record PlayerInfoEntry(
    Guid ProfileId,
    string? Name,
    IReadOnlyList<GameProfileProperty>? Properties,
    bool HasChatSession,
    RemoteChatSession? ChatSession,
    GameMode GameMode,
    bool Listed,
    int Latency,
    Component? DisplayName,
    int ListOrder,
    bool ShowHat);

/// <summary>The player-info-update action bits (the fixed BitSet, one bit per action in ordinal order).</summary>
[Flags]
public enum PlayerInfoActions
{
    /// <summary>No actions.</summary>
    None = 0,

    /// <summary>Add the player (name + properties).</summary>
    AddPlayer = 1 << 0,

    /// <summary>Initialize chat session.</summary>
    InitializeChat = 1 << 1,

    /// <summary>Update game mode.</summary>
    UpdateGameMode = 1 << 2,

    /// <summary>Update the listed flag.</summary>
    UpdateListed = 1 << 3,

    /// <summary>Update latency.</summary>
    UpdateLatency = 1 << 4,

    /// <summary>Update display name.</summary>
    UpdateDisplayName = 1 << 5,

    /// <summary>Update the list-order priority (1.21.2+).</summary>
    UpdateListOrder = 1 << 6,

    /// <summary>Update the show-hat flag (1.21.4+).</summary>
    UpdateHat = 1 << 7,
}

/// <summary>The resource-pack response action (770/776 has eight values; 47 the first four).</summary>
public enum ResourcePackAction
{
    /// <summary>Successfully loaded.</summary>
    SuccessfullyLoaded = 0,

    /// <summary>Declined.</summary>
    Declined = 1,

    /// <summary>Failed to download.</summary>
    FailedDownload = 2,

    /// <summary>Accepted.</summary>
    Accepted = 3,

    /// <summary>Downloaded (1.20.3+).</summary>
    Downloaded = 4,

    /// <summary>Invalid URL (1.20.3+).</summary>
    InvalidUrl = 5,

    /// <summary>Failed to reload (1.20.3+).</summary>
    FailedReload = 6,

    /// <summary>Discarded (1.20.3+).</summary>
    Discarded = 7,
}

// Advancements

/// <summary>The advancement frame/type shown in the UI (task/challenge/goal), a VarInt ordinal on the wire.</summary>
public enum AdvancementFrameType
{
    /// <summary>A normal task advancement.</summary>
    Task = 0,

    /// <summary>A challenge advancement.</summary>
    Challenge = 1,

    /// <summary>A goal advancement.</summary>
    Goal = 2,
}

/// <summary>The display info of an advancement (title, description, icon item, frame, optional background, toast and hidden flags, and the tree coordinates). The icon is a full modern ItemStack selected by era.</summary>
public sealed record AdvancementDisplayInfo(
    Component Title,
    Component Description,
    ItemStack Icon,
    AdvancementFrameType Frame,
    Identifier? Background,
    bool ShowToast,
    bool Hidden,
    float X,
    float Y);

/// <summary>An advancement node: an optional parent, optional display info, the criterion names, the requirements (AND of ORs of criterion names), and the telemetry flag.</summary>
/// <param name="Parent">The parent advancement id, or null for a tab root.</param>
/// <param name="Display">The display metadata, or null when the advancement is not shown.</param>
/// <param name="Criteria">The criterion names. On the wire from 1.12 to 1.20.1 (protocols 335-763) as a length-prefixed string list with an empty per-criterion body; removed at 1.20.2 (protocol 764), where the criterion names are recoverable from <paramref name="Requirements"/>. Empty on the eras that do not carry it.</param>
/// <param name="Requirements">The requirement groups (AND of ORs).</param>
/// <param name="SendsTelemetryEvent">The telemetry flag, added at 1.20 (protocol 763). False on the eras that do not carry it.</param>
public sealed record AdvancementNode(
    Identifier? Parent,
    AdvancementDisplayInfo? Display,
    IReadOnlyList<string> Criteria,
    IReadOnlyList<IReadOnlyList<string>> Requirements,
    bool SendsTelemetryEvent);

/// <summary>An added advancement: its identifier and node value.</summary>
public sealed record AdvancementEntry(Identifier Id, AdvancementNode Value)
{
    /// <summary>The 26.3+ tab position X, retained only where the era carries it; null on older eras.</summary>
    public float? PositionX { get; init; }

    /// <summary>The 26.3+ tab position Y, retained only where the era carries it; null on older eras.</summary>
    public float? PositionY { get; init; }
}

/// <summary>The progress of a single criterion: the epoch-millis instant when it was obtained, or null if unobtained.</summary>
public sealed record CriterionProgressEntry(string CriterionId, long? ObtainedEpochMillis);

/// <summary>The progress of one advancement: a map of criterion id to progress.</summary>
public sealed record AdvancementProgressEntry(Identifier Id, IReadOnlyList<CriterionProgressEntry> Criteria);

// Command suggestions / tab complete

/// <summary>One command suggestion: the suggested text and an optional tooltip component.</summary>
public sealed record CommandSuggestion(string Text, Component? Tooltip);
