namespace Umpk.Client.Events;

/// <summary>Raised when a bounded event stream dropped items because its consumer fell behind. Reported through the normal event bus so it never blocks the session loop.</summary>
public sealed record StreamLagged(Type EventType, int DroppedCount) : IClientEvent;
