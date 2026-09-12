namespace Umpk.Client.Actions;

/// <summary>What the server is known to have done about a respawn request, as observed after the fact, never as inferred from the send completing. The default is <see cref="Unconfirmed"/> ON PURPOSE.</summary>
public enum RespawnOutcome
{
    /// <summary>Neither a clientbound respawn arrived inside the confirmation window, nor was the player known to be alive when the request went out. This is the default value.</summary>
    Unconfirmed = 0,

    /// <summary>A clientbound respawn arrived: the server actually respawned the player.</summary>
    Confirmed = 1,

    /// <summary>No respawn arrived, and the player was alive (health greater than zero) when the request was sent. Servers ignore an ordinary respawn request from a living player, but the won-the-game respawn out of the End is also sent while alive. Health therefore must not block the send; it only refines the outcome after the confirmation window closes without a response.</summary>
    NotDead = 2,
}
