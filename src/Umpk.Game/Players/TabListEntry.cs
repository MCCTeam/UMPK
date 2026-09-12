using Umpk.Text;

namespace Umpk.Game.Players;

/// <summary>One tab-list (player-info) entry, mirroring the fields of the modern <c>PlayerInfoUpdate</c> packet: the profile, game mode, latency, an optional display-name override, the "listed" flag, and an opaque chat-session placeholder. The <c>ChatSession</c> profile-key/signature payload is owned by the chat-signing library; it is kept here as opaque data so the tab-list model stays complete without depending on it.</summary>
public sealed class TabListEntry
{
    /// <summary>Creates a tab-list entry for a profile.</summary>
    /// <param name="profile">The player profile (uuid is the entry key).</param>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public TabListEntry(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }

    /// <summary>The player profile; its <see cref="GameProfile.Id"/> is the tab-list key.</summary>
    public GameProfile Profile { get; }

    /// <summary>The player uuid (from the profile).</summary>
    public Guid Uuid => Profile.Id;

    /// <summary>The player game mode.</summary>
    public GameMode GameMode { get; set; } = GameMode.Undefined;

    /// <summary>The measured latency in milliseconds.</summary>
    public int Latency { get; set; }

    /// <summary>An optional display-name override; null falls back to the profile name.</summary>
    public Component? DisplayName { get; set; }

    /// <summary>Whether the entry is shown in the tab list (the 1.19.3+ "listed" flag).</summary>
    public bool Listed { get; set; } = true;

    /// <summary>The list-order priority (1.21.2+); higher sorts first. 0 when unspecified.</summary>
    public int ListOrder { get; set; }

    /// <summary>Whether the player has a hat layer shown (1.21.4+).</summary>
    public bool ShowHat { get; set; } = true;

    /// <summary>The opaque chat-session payload (profile key + signature), owned by the chat-signing library.</summary>
    public object? ChatSession { get; set; }
}
