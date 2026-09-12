namespace Umpk.Game.Players;

/// <summary>A player game mode, matching wire ids (survival = 0, creative = 1, adventure = 2, spectator = 3). <see cref="Undefined"/> (-1) is the vanilla "not set" sentinel used in player-info updates.</summary>
public enum GameMode
{
    /// <summary>Not set (vanilla -1 sentinel).</summary>
    Undefined = -1,

    /// <summary>Survival.</summary>
    Survival = 0,

    /// <summary>Creative.</summary>
    Creative = 1,

    /// <summary>Adventure.</summary>
    Adventure = 2,

    /// <summary>Spectator.</summary>
    Spectator = 3,
}
