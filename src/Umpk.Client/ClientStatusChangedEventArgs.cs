namespace Umpk.Client;

/// <summary>Raised when a <see cref="UmpkClient"/> (or a <c>UmpkClientSupervisor</c>) transitions between lifecycle states. When the transition is to <see cref="ClientStatus.Disconnected"/>, <see cref="Disconnect"/> carries the reason.</summary>
public sealed class ClientStatusChangedEventArgs(
    ClientStatus previous,
    ClientStatus current,
    DisconnectInfo? disconnect) : EventArgs
{
    /// <summary>The status before the transition.</summary>
    public ClientStatus Previous { get; } = previous;

    /// <summary>The status after the transition.</summary>
    public ClientStatus Current { get; } = current;

    /// <summary>The disconnect reason when transitioning to <see cref="ClientStatus.Disconnected"/>.</summary>
    public DisconnectInfo? Disconnect { get; } = disconnect;
}
