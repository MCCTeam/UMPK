using Umpk.Client.Events;

namespace Umpk.Client.Chat;

/// <summary>The signature standing a host displays for an inbound chat message. Purely a classification of <see cref="ChatVerification"/> and <see cref="ChatCategory"/>; glyphs, colours and configuration gating are host policy, not this library's.</summary>
public enum ChatStanding
{
    /// <summary>Not marked: pre-1.19 chat, where no message carries a signature by design.</summary>
    None = 0,

    /// <summary>Signed, and the signature verified against the sender's profile key.</summary>
    Verified = 1,

    /// <summary>Signed, checked, and REJECTED.</summary>
    Rejected = 2,

    /// <summary>Signed but not checkable (no profile key seen for the sender).</summary>
    Unverified = 3,

    /// <summary>Carried no signature at all, as an offline-mode server sends.</summary>
    Insecure = 4,

    /// <summary>Server-voiced (system or disguised) chat, which never carries a signature.</summary>
    ServerVoiced = 5,
}

/// <summary>Turns the signature standing UMPK reports on an inbound chat message (<see cref="ChatMessageReceived.Verification"/> and <see cref="ChatMessageReceived.Category"/>) into the <see cref="ChatStanding"/> a host shows. Every host that displays chat must classify the same verification state identically, so the rule lives here once.</summary>
public static class ChatStandingRule
{
    /// <summary>Classifies a message's standing from its verification and category directly. <see cref="ChatVerification.NotApplicable"/> covers both server-voiced chat and pre-1.19 legacy chat; only the former resolves to a standing, because the legacy client never marked pre-1.19 chat, where no message carries a signature by design and a marker on every line would say nothing.</summary>
    public static ChatStanding Classify(ChatVerification verification, ChatCategory category) => verification switch
    {
        ChatVerification.Verified => ChatStanding.Verified,
        ChatVerification.Failed => ChatStanding.Rejected,
        ChatVerification.Unverified => ChatStanding.Unverified,
        ChatVerification.Insecure => ChatStanding.Insecure,
        _ => category is ChatCategory.System or ChatCategory.Disguised
            ? ChatStanding.ServerVoiced
            : ChatStanding.None,
    };

    /// <summary>Classifies a received message's standing from its <see cref="ChatMessageReceived.Verification"/> and <see cref="ChatMessageReceived.Category"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is null.</exception>
    public static ChatStanding Classify(ChatMessageReceived message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Classify(message.Verification, message.Category);
    }

    /// <summary>The one standing a host may legitimately hide. Every other standing must be delivered and marked: a consumer that wants to treat unsigned or uncheckable chat differently has to see it first. Returns true only for <see cref="ChatStanding.Rejected"/>.</summary>
    public static bool IsHidable(ChatStanding standing) => standing == ChatStanding.Rejected;
}
