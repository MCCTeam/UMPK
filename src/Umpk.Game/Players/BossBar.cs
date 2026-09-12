using Umpk.Text;

namespace Umpk.Game.Players;

/// <summary>The boss-bar color ids, from pink=0 through white=6.</summary>
public enum BossBarColor
{
    /// <summary>Pink.</summary>
    Pink = 0,

    /// <summary>Blue.</summary>
    Blue = 1,

    /// <summary>Red.</summary>
    Red = 2,

    /// <summary>Green.</summary>
    Green = 3,

    /// <summary>Yellow.</summary>
    Yellow = 4,

    /// <summary>Purple.</summary>
    Purple = 5,

    /// <summary>White.</summary>
    White = 6,
}

/// <summary>The boss-bar overlay ids, from progress=0 through notched_20=4.</summary>
public enum BossBarOverlay
{
    /// <summary>Solid progress bar (no notches).</summary>
    Progress = 0,

    /// <summary>Six notches.</summary>
    Notched6 = 1,

    /// <summary>Ten notches.</summary>
    Notched10 = 2,

    /// <summary>Twelve notches.</summary>
    Notched12 = 3,

    /// <summary>Twenty notches.</summary>
    Notched20 = 4,
}

/// <summary>The boss-bar flag bits (darken sky = 1, play boss music = 2, create world fog = 4).</summary>
[Flags]
public enum BossBarFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>Darkens the sky while active.</summary>
    DarkenScreen = 1,

    /// <summary>Plays the boss music.</summary>
    PlayBossMusic = 2,

    /// <summary>Creates world fog.</summary>
    CreateWorldFog = 4,
}

/// <summary>A single boss bar, keyed by uuid on the owning <see cref="BossBarState"/>: its title, progress (0..1), color, overlay, and flags. The <c>BossEvent</c> packet's add and per-field update operations map onto the mutators here.</summary>
public sealed class BossBar
{
    /// <summary>Creates a boss bar from its add-operation fields.</summary>
    /// <param name="uuid">The boss-bar uuid (the state key).</param>
    /// <param name="title">The title component.</param>
    /// <param name="progress">The fill fraction (0..1); clamped on set.</param>
    /// <param name="color">The bar color.</param>
    /// <param name="overlay">The notch overlay.</param>
    /// <param name="flags">The bar flags.</param>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    public BossBar(Guid uuid, Component title, float progress, BossBarColor color, BossBarOverlay overlay, BossBarFlags flags)
    {
        ArgumentNullException.ThrowIfNull(title);
        Uuid = uuid;
        Title = title;
        Progress = Math.Clamp(progress, 0f, 1f);
        Color = color;
        Overlay = overlay;
        Flags = flags;
    }

    /// <summary>The boss-bar uuid.</summary>
    public Guid Uuid { get; }

    /// <summary>The title component.</summary>
    public Component Title { get; set; }

    /// <summary>The fill fraction, always in [0, 1].</summary>
    public float Progress { get; private set; }

    /// <summary>The bar color.</summary>
    public BossBarColor Color { get; set; }

    /// <summary>The notch overlay.</summary>
    public BossBarOverlay Overlay { get; set; }

    /// <summary>The bar flags.</summary>
    public BossBarFlags Flags { get; set; }

    /// <summary>Sets the progress, clamped to [0, 1].</summary>
    public void SetProgress(float progress) => Progress = Math.Clamp(progress, 0f, 1f);
}
