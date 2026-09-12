using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Client.Plugins;

/// <summary>
/// The client's plugin-channel surface: register for inbound custom payloads, send outbound ones, and read the channels the server announced.
/// <para>It hangs off <see cref="UmpkClient.Channels"/> and lives for the client's whole lifetime, which is what makes it usable while the client is still <see cref="ClientStatus.Created"/>. That timing is the point: <see cref="IClientPlugin.Attach"/> does not run until play has already begun, and a server sends its own <c>minecraft:register</c> and its mod handshakes immediately, so a registration made only at attach time can miss the first message on its own channel. A registration made here before the dial is in place before the first inbound frame of the session.</para>
/// <para>Registrations are owned by the handle <c>RegisterPlay</c> / <c>RegisterConfiguration</c> returns and are NOT dropped when a session ends: a client that outlives one session keeps them, and the <c>minecraft:register</c> announce is repeated when the next session reaches play. A plugin that registers through <see cref="ClientPluginContext.RegisterPluginChannel"/> instead gets the existing per-session behaviour, torn down with the plugin.</para>
/// </summary>
public sealed class ClientChannels
{
    private readonly PluginChannelManager _manager;
    private readonly BlockPosLayout _posLayout;

    internal ClientChannels(PluginChannelManager manager, JavaVersion version)
    {
        _manager = manager;
        _posLayout = version.Features.PosLayout;
        _manager.AnnouncementChanged = args => ServerAnnouncedChanged?.Invoke(this, args);
    }

    /// <summary>Raised when <see cref="ServerAnnounced"/> changes. See <see cref="ServerAnnouncedChannelsChangedEventArgs"/> for which loop it runs on.</summary>
    public event EventHandler<ServerAnnouncedChannelsChangedEventArgs>? ServerAnnouncedChanged;

    /// <summary>
    /// The channels the server has announced with <c>minecraft:register</c>, as a snapshot that does not change under the caller. Cleared when a session ends, because it described that server.
    /// <para>A pre-1.13 server announcing a legacy, non-namespaced name (<c>FML|HS</c>) contributes nothing here: that name cannot be expressed as an <see cref="Identifier"/> at all. It is still visible in the raw frame feed.</para>
    /// </summary>
    public IReadOnlySet<Identifier> ServerAnnounced => _manager.ServerAnnounced;

    /// <summary>Whether this version can carry a serverbound configuration-phase custom payload, that is whether it has a configuration phase at all (1.20.2+). False means <see cref="SendConfigurationAsync"/> would throw, and that a <see cref="RegisterConfiguration"/> handler can never fire.</summary>
    public bool CanSendConfiguration => _manager.CanSendConfiguration;

    /// <summary>Registers a PLAY-phase handler for <paramref name="channel"/>. The first registration for a channel announces it to the server with <c>minecraft:register</c>; the last disposal announces <c>minecraft:unregister</c>. Registering before the dial defers that announce to the moment play begins.</summary>
    /// <returns>A handle that stops delivery on dispose.</returns>
    public PluginChannelRegistration RegisterPlay(Identifier channel, Action<ReadOnlyMemory<byte>> onMessage)
        => _manager.Register(ProtocolPhase.Play, channel, onMessage, CancellationToken.None);

    /// <summary>
    /// Registers a CONFIGURATION-phase handler for <paramref name="channel"/> (1.20.2+). Nothing is announced: the register/unregister convention is a play-phase one, and vanilla's client announces nothing during configuration either.
    /// <para>Handlers run on the session loop, inline with the configuration driver, which is what makes it safe to answer with <see cref="SendConfigurationAsync"/> from inside one.</para>
    /// </summary>
    /// <returns>A handle that stops delivery on dispose.</returns>
    public PluginChannelRegistration RegisterConfiguration(Identifier channel, Action<ReadOnlyMemory<byte>> onMessage)
        => _manager.Register(ProtocolPhase.Configuration, channel, onMessage, CancellationToken.None);

    /// <summary>Sends a PLAY-phase custom payload on <paramref name="channel"/>. There is no "must be registered first" gate: vanilla routes a plugin message whether or not the channel was announced.</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version has no serverbound play <c>custom_payload</c> wire id; ask <see cref="ClientActionCapabilities.CanSendPluginMessage"/> to branch instead of catching.</exception>
    /// <exception cref="InvalidOperationException">The client has no live connection.</exception>
    public ValueTask SendAsync(Identifier channel, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => _manager.SendAsync(channel, data, ct);

    /// <summary>Sends a CONFIGURATION-phase custom payload on <paramref name="channel"/>. Only meaningful while the connection is actually in the configuration phase, which for the login entry is the window between login success and <c>finish_configuration</c>: the natural caller is a <see cref="RegisterConfiguration"/> handler answering what it was just handed.</summary>
    /// <exception cref="ActionNotSupportedException">This version has no configuration phase (pre-1.20.2).</exception>
    /// <exception cref="InvalidOperationException">The client has no live connection.</exception>
    public ValueTask SendConfigurationAsync(
        Identifier channel, ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => _manager.SendConfigurationAsync(channel, data, ct);

    /// <summary>A reader over <paramref name="payload"/> that already knows the negotiated version's block position layout. This is the intended way to decode what a channel handler was handed: the primitives are Minecraft's own, and the one that varies by era is filled in here rather than left to the caller to get right.</summary>
    public PluginPayloadReader Read(ReadOnlyMemory<byte> payload) => new(payload, _posLayout);

    /// <summary>An empty payload writer that already knows the negotiated version's block position layout. Hand its <see cref="PluginPayloadWriter.Written"/> to <see cref="SendAsync"/> or <see cref="SendConfigurationAsync"/>.</summary>
    public PluginPayloadWriter Write() => new(_posLayout);
}
