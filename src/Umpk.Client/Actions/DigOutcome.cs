namespace Umpk.Client.Actions;

/// <summary>What the server is known to have done about a dig, as observed after the fact, never as inferred from the send completing. The default is <see cref="Unconfirmed"/> ON PURPOSE: there is deliberately no value here that means "the packets were written", because a completed send is not a broken block. A survival player without enough mining progress, a protected region, a spectator, and a block the server simply refuses all produce exactly the same successful write, and vanilla answers every one of them by sending the block back unchanged, so the write alone cannot tell them apart.</summary>
public enum DigOutcome
{
    /// <summary>Neither an air read nor a re-asserted block arrived inside the confirmation window (or the column unloaded before either could). The client does not know what happened. This is the default value.</summary>
    Unconfirmed = 0,

    /// <summary>The position was observed to read air after the dig: the block is gone.</summary>
    Broken = 1,

    /// <summary>The server sent this exact position back with a non-air block: an observed refusal, not a guess from silence.</summary>
    NotBroken = 2,
}
