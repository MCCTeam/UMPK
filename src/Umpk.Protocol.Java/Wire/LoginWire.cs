using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>
/// Per-era layout flags for the login-phase packets, resolved at codec construction.
/// <para>One value serves four packets whose era boundaries do NOT coincide, so a member's name is a label for a flag combination and does not itself establish a packet boundary. The timeline in <c>LoginBindings</c> selects the member for each protocol.</para>
/// </summary>
/// <param name="RawUuid">True for modern raw 16-byte UUIDs; false for the 1.8 dashed-string UUID.</param>
/// <param name="HelloHasProfileId">True when login-start carries the player UUID (1.19.1+).</param>
/// <param name="FinishedHasProperties">True when login-finished carries a profile property array (modern).</param>
/// <param name="FinishedHasSessionId">True when login-finished carries a trailing session id (26.2, V26_2).</param>
/// <param name="HelloHasShouldAuthenticate">True when the encryption request carries the authenticate flag (1.20.5+).</param>
/// <param name="ReasonLegacyComponents">True when the login disconnect reason uses the legacy component dialect. The login disconnect reason is a length-prefixed JSON string on EVERY supported version (vanilla never gave it the NBT transport the play and configuration disconnects got in 1.20.3, because the client has no registry access during login); only the dialect moves, at 1.21.5.</param>
/// <param name="ReasonCollapsesLiteral">True when a pure-literal disconnect reason is written as a bare JSON string rather than <c>{"text":"..."}</c>. This is a SECOND, independent axis on the same field, and it moves at 1.20.3, one release earlier than <paramref name="ReasonLegacyComponents"/> does, because it tracks a a different wire change. See <see cref="ComponentJsonLiteralForm"/> for the byte forms on both sides of that boundary.</param>
public readonly record struct LoginWire(
    bool RawUuid,
    bool HelloHasProfileId,
    bool FinishedHasProperties,
    bool FinishedHasSessionId,
    bool HelloHasShouldAuthenticate,
    bool ReasonLegacyComponents,
    bool ReasonCollapsesLiteral)
{
    internal static LoginWire V1_8 { get; } = new(
        RawUuid: false, HelloHasProfileId: false, FinishedHasProperties: false,
        FinishedHasSessionId: false, HelloHasShouldAuthenticate: false, ReasonLegacyComponents: true,
        ReasonCollapsesLiteral: false);

    internal static LoginWire V1_21_5 { get; } = new(
        RawUuid: true, HelloHasProfileId: true, FinishedHasProperties: true,
        FinishedHasSessionId: false, HelloHasShouldAuthenticate: true, ReasonLegacyComponents: false,
        ReasonCollapsesLiteral: true);

    internal static LoginWire V26_2 { get; } = V1_21_5 with { FinishedHasSessionId = true };

    /// <summary>The 1.16-1.18.2 (protocols 735-758) login wire: login-start is name-only (no profile id), the encryption request has no authenticate flag, disconnect reasons use the legacy component dialect, and login-finished carries a 16-byte UUID as four big-endian ints, byte-identical to a raw UUID, plus the name with no property array.</summary>
    internal static LoginWire V1_16 { get; } = new(
        RawUuid: true, HelloHasProfileId: false, FinishedHasProperties: false,
        FinishedHasSessionId: false, HelloHasShouldAuthenticate: false, ReasonLegacyComponents: true,
        ReasonCollapsesLiteral: false);

    /// <summary>The 765-769 disconnect-reason wire, and ONLY that: this record feeds <see cref="LoginCodecs.DisconnectV1_20_3"/> and nothing else, so the four non-reason flags below are inherited unread from <see cref="V1_8"/> and must not be taken as claims about the 765-769 login-start / login-finished / encryption-request shapes (those genuinely differ inside this band, which is why they have their own records). What it says is the pair that IS read: the reason keeps the legacy component spelling (<c>clickEvent</c> / <c>hoverEvent</c>, hover body under <c>contents</c>) and already collapses a pure literal to a bare JSON string.</summary>
    internal static LoginWire V1_20_3 { get; } = V1_8 with { ReasonCollapsesLiteral = true };

    /// <inheritdoc />
    public override string ToString() =>
        $"rawuuid={(RawUuid ? 1 : 0)},helloprofile={(HelloHasProfileId ? 1 : 0)}," +
        $"finishedprops={(FinishedHasProperties ? 1 : 0)},finishedsession={(FinishedHasSessionId ? 1 : 0)}," +
        $"helloauth={(HelloHasShouldAuthenticate ? 1 : 0)},legacyreason={(ReasonLegacyComponents ? 1 : 0)}," +
        $"literalreason={(ReasonCollapsesLiteral ? 1 : 0)}";
}
