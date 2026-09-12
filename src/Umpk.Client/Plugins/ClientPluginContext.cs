using Umpk.Client.Commands;
using Umpk.Client.Movement;
using Umpk.Commands;
using Umpk.Protocol.Java;

namespace Umpk.Client.Plugins;

/// <summary>The capabilities a plugin receives when it attaches. Everything acquired through this context is torn down automatically when the plugin detaches or the session ends: subscriptions, scheduled work, command scopes, movement leases, and plugin channels.</summary>
public sealed class ClientPluginContext
{
    private readonly Func<string, IMovementLease?> _acquireMovement;
    private readonly Func<string?> _movementOwner;
    private readonly Func<Identifier, Action<ReadOnlyMemory<byte>>, PluginChannelRegistration> _registerChannel;
    private readonly Func<Identifier, ReadOnlyMemory<byte>, CancellationToken, ValueTask> _sendChannel;
    private readonly Func<PacketFrameHandler, IDisposable> _observePackets;

    internal ClientPluginContext(
        UmpkClient client,
        ClientEvents events,
        ClientActions actions,
        PluginScheduler scheduler,
        ICommandRegistrationScope<ClientCommandSource> commands,
        Func<string, IMovementLease?> acquireMovement,
        Func<string?> movementOwner,
        Func<Identifier, Action<ReadOnlyMemory<byte>>, PluginChannelRegistration> registerChannel,
        Func<Identifier, ReadOnlyMemory<byte>, CancellationToken, ValueTask> sendChannel,
        Func<PacketFrameHandler, IDisposable> observePackets,
        CancellationToken detached)
    {
        Client = client;
        Events = events;
        Actions = actions;
        Scheduler = scheduler;
        Commands = commands;
        _acquireMovement = acquireMovement;
        _movementOwner = movementOwner;
        _registerChannel = registerChannel;
        _sendChannel = sendChannel;
        _observePackets = observePackets;
        Detached = detached;
    }

    /// <summary>The owning client.</summary>
    public UmpkClient Client { get; }

    /// <summary>Event subscriptions made here are auto-disposed on detach.</summary>
    public ClientEvents Events { get; }

    /// <summary>The action surface (safe from any thread).</summary>
    public ClientActions Actions { get; }

    /// <summary>Which version-optional actions the negotiated version can actually perform. Reachable through <see cref="Client"/> and <see cref="ClientActions.Capabilities"/> too; surfaced directly here because branching on a capability is something a plugin does constantly and should not have to go two hops for.</summary>
    public ClientActionCapabilities Capabilities => Client.Capabilities;

    /// <summary>
    /// The client's plugin-channel surface: the server's announced channel set, the configuration-phase registrations, and both sends.
    /// <para>Mind the difference in LIFETIME from <see cref="RegisterPluginChannel"/>. A registration made through this object belongs to the CLIENT and outlives this plugin's detach, so whoever makes it owns the disposal; one made through <see cref="RegisterPluginChannel"/> is torn down with the plugin. Registering a CONFIGURATION handler from here is also too late to be useful: attach runs after play has begun, so that phase is already over. Register those before the dial.</para>
    /// </summary>
    public ClientChannels Channels => Client.Channels;

    /// <summary>Per-plugin scheduling; all registrations are removed on detach.</summary>
    public PluginScheduler Scheduler { get; }

    /// <summary>The plugin's command registration scope; unregistered on detach.</summary>
    public ICommandRegistrationScope<ClientCommandSource> Commands { get; }

    /// <summary>Fires on unload/disconnect.</summary>
    public CancellationToken Detached { get; }

    /// <summary>Tries to acquire the exclusive movement lease.</summary>
    public IMovementLease? TryAcquireMovement(string reason) => _acquireMovement(reason);

    /// <summary>The tag of whoever currently holds the exclusive movement lease, or null when nothing does. This is the READ of what <see cref="TryAcquireMovement"/> competes for, and it exists because asking by acquiring is not free: a plugin that only wants to know whether something else is steering had to take the lease and hand it straight back, which for one tick is exactly the window a navigation starting on the same tick loses its own acquire in. A plugin that must stand aside while something else drives (a view humaniser, an idle animation) reads this instead and never touches the lease.</summary>
    public string? MovementOwner => _movementOwner();

    /// <summary>Registers a plugin channel handler; auto-unregistered on detach.</summary>
    public PluginChannelRegistration RegisterPluginChannel(Identifier channel, Action<ReadOnlyMemory<byte>> onMessage)
        => _registerChannel(channel, onMessage);

    /// <summary>Sends an outbound custom-payload frame on <paramref name="channel"/> (a serverbound <c>minecraft:custom_payload</c> carrying the channel identifier and <paramref name="data"/>). Lets a plugin reply on a custom channel.</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version has no serverbound play <c>custom_payload</c> wire id; ask <see cref="ClientActionCapabilities.CanSendPluginMessage"/> to branch instead of catching.</exception>
    public ValueTask SendPluginMessageAsync(Identifier channel, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => _sendChannel(channel, data, ct);

    /// <summary>
    /// Subscribes this plugin to the raw frame feed (see <see cref="UmpkClient.ObservePackets"/>); auto-unregistered on detach, so a plugin that never disposes its handle explicitly is still safe.
    /// <para>THREADING. <paramref name="handler"/> runs on the connection's read loop (inbound) or inside the caller's send (outbound), NOT on the session loop. It must be cheap and must not block; hand real work to <see cref="Scheduler"/> instead. A throwing handler is logged and swallowed by the client rather than tearing the session down.</para>
    /// <para>LIFETIME. <see cref="PacketFrame.Payload"/> is only valid inside the call. Use <see cref="PacketFrame.CopyPayload"/> to keep the bytes; the ref-struct frame makes accidentally keeping it a compile error rather than a recycled-buffer read.</para>
    /// <para>COST. Nothing when nobody subscribes: the client attaches its outbound observer only while at least one handler (from any source, plugin or otherwise) is registered.</para>
    /// </summary>
    /// <param name="handler">Called once per frame, both directions.</param>
    /// <returns>A handle that unsubscribes on dispose, or automatically on detach.</returns>
    public IDisposable ObservePackets(PacketFrameHandler handler) => _observePackets(handler);
}
