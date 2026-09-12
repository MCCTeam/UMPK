namespace Umpk.Client.Events;

/// <summary>Marker for every event delivered through <see cref="ClientEvents"/>. Events are immutable records delivered synchronously on the session loop in subscription order.</summary>
public interface IClientEvent;
