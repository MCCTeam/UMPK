using Microsoft.Extensions.Logging;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Actions;

/// <summary>Session-level actions: client settings, respawn, and abilities toggles. Cookie/transfer handling lives in the connection flow.</summary>
public sealed class SessionActions
{
    private readonly IPacketSink _sink;
    private readonly ClientSessionServices _services;

    internal SessionActions(IPacketSink sink, ClientSessionServices services)
    {
        _sink = sink;
        _services = services;
    }

    /// <summary>Announces the client information / settings in the play phase. Every field the negotiated version carries is written; fields it does not carry are omitted, so the same options are safe to pass on any version.</summary>
    /// <param name="options">The settings to announce, or <see langword="null"/> to use the session's configured <see cref="ClientOptions.ClientInformation"/>.</param>
    /// <param name="ct">Cancels the send.</param>
    public Task SetClientSettingsAsync(ClientInformationOptions? options = null, CancellationToken ct = default)
    {
        // The play-phase announce needs the play-phase record. Sending the configuration-phase one here resolved no codec in the Play/Serverbound registry and threw for every version.
        ClientInformationOptions effective = options ?? _services.Options.ClientInformation;
        return _sink.SendAsync(effective.ToPlayPacket(), ct).AsTask();
    }

    /// <summary>How long <see cref="RespawnAsync"/> waits for the server's clientbound respawn before it reports that nothing was confirmed. A respawn is a single round trip on a server that accepts it.</summary>
    public static readonly TimeSpan DefaultRespawnConfirmationWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Requests a respawn after death and reports what the server is known to have done about it, instead of reporting the completed send as a respawn.
    ///
    /// <para><c>client_command</c> with the perform-respawn action is the protocol's route out of the death screen. The server returns without doing anything when the player is alive and did not just win the game, and sends nothing back to say so, so a caller that treats the completed send as a respawn is reporting a state change the server explicitly declined to make. The acknowledgement is the clientbound respawn packet itself, the signal a client waits for before it leaves the death screen.</para>
    ///
    /// <para>Health is read BEFORE the send: once the server respawns the player, the health is 20 again, so the only moment "you were not dead" can be established is before the request goes out. It refines the failure into <see cref="RespawnOutcome.NotDead"/> but never refuses the send, because the won-the-game respawn out of the End is also made by a living player and gating on health there would turn a false success into a false failure.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="ClientSessionServices.Events"/> is null on this session. A poll-only send cannot tell an observed refusal from silence, so this refuses rather than degrading to a guess.</exception>
    public async Task<RespawnOutcome> RespawnAsync(CancellationToken ct = default)
    {
        EventBus events = _services.Events
            ?? throw new InvalidOperationException(
                $"{nameof(RespawnAsync)} requires an event bus (ClientSessionServices.Events), and none is wired on this session.");

        // Read before the send: once the server respawns the player the health is 20 again, so the only moment "you were not dead" can be established is before the request goes out.
        float healthBeforeSend = _services.Self.Health;

        // Armed before the send, for the same reason the dig confirmation and the spawn signal are: the respawn can be applied before the send's own task resumes.
        var respawned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = events.Subscribe<Respawned>(_ => respawned.TrySetResult(true));

        await SendClientCommandAsync(ClientCommandAction.PerformRespawn, ct).ConfigureAwait(false);

        Task completed = await Task.WhenAny(
            respawned.Task, Task.Delay(DefaultRespawnConfirmationWindow, ct)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (ReferenceEquals(completed, respawned.Task))
            return RespawnOutcome.Confirmed;

        return healthBeforeSend > 0f ? RespawnOutcome.NotDead : RespawnOutcome.Unconfirmed;
    }

    /// <summary>Asks the server to send the player's statistics (<c>client_command</c> with the request-stats action). The reply is <c>award_stats</c>, which is not decoded yet, so this is the request half.</summary>
    public Task RequestStatisticsAsync(CancellationToken ct = default) =>
        SendClientCommandAsync(ClientCommandAction.RequestStats, ct);

    private Task SendClientCommandAsync(ClientCommandAction action, CancellationToken ct) =>
        _sink.SendAsync(new ServerboundClientCommandPacket(action), ct).AsTask();

    /// <summary>Toggles the flying ability and notifies the server.</summary>
    public async Task SetFlyingAsync(bool flying, CancellationToken ct = default)
    {
        _services.State.Self.Flying = flying;
        byte flags = (byte)(flying ? 0x02 : 0x00);
        await _sink.SendAsync(new ServerboundPlayerAbilitiesPacket(flags), ct).ConfigureAwait(false);
    }
}
