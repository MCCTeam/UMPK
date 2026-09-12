namespace Umpk.Client.Actions;

/// <summary>What the server is known to have done about a block use, as observed afterwards in the tracked world, never as inferred from the send completing. The default is <see cref="Unconfirmed"/> for the reason <see cref="DigOutcome"/>'s is: there is deliberately no value meaning "the packet was written", because a completed send is not an opened door. A protected region, a spectator, adventure mode, a plugin that vetoes the interaction and a server that simply refuses all produce the same successful write, and vanilla answers every one of them by sending the block back unchanged.</summary>
public enum UseBlockOutcome
{
    /// <summary>Neither the expected property value nor a re-asserted block arrived inside the confirmation window (or the column unloaded before either could). The client does not know what happened. This is the default value.</summary>
    Unconfirmed = 0,

    /// <summary>The witness block was observed carrying the expected value: the interaction worked.</summary>
    Used = 1,

    /// <summary>The server sent the witness's position back and it still does not carry the expected value: an observed refusal rather than a guess from silence. Also the answer for an interaction vanilla's own server would have skipped, which this client refuses before sending.</summary>
    NotUsed = 2,

    /// <summary>Nothing was sent: the player's eye is further than <see cref="InteractionActions.BlockInteractionRange"/> from the nearest point of the target block. Distinct from <see cref="NotUsed"/> because it is a fact about where the PLAYER is, which a caller can fix by moving, rather than a fact about what the server allows.</summary>
    OutOfReach = 3,
}
