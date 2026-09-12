using Umpk.Text;

namespace Umpk.Game.Scoreboard;

/// <summary>A scoreboard objective: a stable internal name plus a display component and a render type. Scores are held on the owning <see cref="Scoreboard"/>, not here, so per-owner values survive display-name and render-type edits.</summary>
public sealed class Objective
{
    /// <summary>Creates an objective.</summary>
    /// <param name="name">The stable internal name (the wire key).</param>
    /// <param name="displayName">The rendered display component.</param>
    /// <param name="renderType">How scores render.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="displayName"/> is null.</exception>
    public Objective(string name, Component displayName, ObjectiveRenderType renderType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(displayName);
        Name = name;
        DisplayName = displayName;
        RenderType = renderType;
    }

    /// <summary>The stable internal name; the identity used across score and display packets.</summary>
    public string Name { get; }

    /// <summary>The rendered display component. Updated by the <c>UpdateObjective</c> change operation.</summary>
    public Component DisplayName { get; set; }

    /// <summary>How scores render.</summary>
    public ObjectiveRenderType RenderType { get; set; }
}
