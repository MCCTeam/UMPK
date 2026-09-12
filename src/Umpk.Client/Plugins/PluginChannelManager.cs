using System.Buffers;
using System.Text;
using Microsoft.Extensions.Logging;
using Umpk.Client.Internal;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Plugins;

/// <summary>
/// Routes custom-payload traffic for plugin channels. Outbound play sends encode (channel identifier + data) into a raw <c>custom_payload</c> frame; inbound frames are matched by channel and dispatched to registered handlers. Supports the <c>minecraft:register</c>/<c>minecraft:unregister</c> convention in both directions: this client announces its own channels with it, and folds the server's announcement out of it into <see cref="ServerAnnounced"/>.
/// <para>Handlers are kept per phase. Play stays the default; the configuration phase (1.20.2+) is routed too because that is where a modern server announces its brand and where a proxy continues its handshake.</para>
/// <para>This object lives for the client's whole lifetime, not per session, so a handler may be registered while the client is still <see cref="ClientStatus.Created"/> and is therefore in place before the first inbound frame of the session that follows. The <c>minecraft:register</c> announce for such a registration cannot go out when it is made (there is no connection), so it is deferred to <see cref="OnPlayStarted"/>, which announces every channel that has a play handler by then.</para>
/// </summary>
internal sealed class PluginChannelManager
{
    private static readonly Identifier CustomPayloadId = Identifier.Minecraft("custom_payload");
    private static readonly Identifier RegisterChannel = Identifier.Minecraft("register");
    private static readonly Identifier UnregisterChannel = Identifier.Minecraft("unregister");

    /// <summary>The pre-1.13 spellings of the same convention. 1.13 namespaced every channel name; before that the register/unregister channels were these two literals, which are not valid identifiers and so cannot be expressed as an <see cref="Identifier"/> at all.</summary>
    private const string LegacyRegisterChannel = "REGISTER";
    private const string LegacyUnregisterChannel = "UNREGISTER";

    private readonly IPacketSink _sink;
    private readonly WireIndex _wire;
    private readonly int _protocol;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<Identifier, List<Action<ReadOnlyMemory<byte>>>> _playHandlers = [];
    private readonly Dictionary<Identifier, List<Action<ReadOnlyMemory<byte>>>> _configHandlers = [];
    private readonly HashSet<Identifier> _announced = [];
    private IReadOnlySet<Identifier> _announcedSnapshot = new HashSet<Identifier>();
    private bool _playLive;

    public PluginChannelManager(IPacketSink sink, WireIndex wire, int protocol, ILogger logger)
    {
        _sink = sink;
        _wire = wire;
        _protocol = protocol;
        _logger = logger;
    }

    /// <summary>Raised, off the lock, whenever the server's announced channel set actually changes. Supplied by the public <see cref="ClientChannels"/> facade so the event a consumer subscribes to has a public sender rather than this internal type.</summary>
    internal Action<ServerAnnouncedChannelsChangedEventArgs>? AnnouncementChanged { get; set; }

    /// <summary>The channels the server has announced through <c>minecraft:register</c>, as a snapshot.</summary>
    public IReadOnlySet<Identifier> ServerAnnounced => Volatile.Read(ref _announcedSnapshot);

    /// <summary>Whether this version can carry a serverbound configuration-phase custom payload.</summary>
    public bool CanSendConfiguration =>
        _wire.CanSendConfiguration(LoginFamilyPackets.Config.CustomPayloadServerbound);

    public PluginChannelRegistration Register(
        ProtocolPhase phase, Identifier channel, Action<ReadOnlyMemory<byte>> onMessage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onMessage);

        bool announceNow = false;
        lock (_gate)
        {
            Dictionary<Identifier, List<Action<ReadOnlyMemory<byte>>>> map = MapFor(phase);
            if (!map.TryGetValue(channel, out List<Action<ReadOnlyMemory<byte>>>? list))
            {
                list = [];
                map[channel] = list;

                // Announce only on the empty-to-non-empty transition, mirroring the unregister sent on the last removal and avoiding redundant frames for additional handlers on one channel.
                announceNow = phase == ProtocolPhase.Play && _playLive;
            }

            list.Add(onMessage);
        }

        // Best-effort, per the convention: vanilla servers route plugin messages whether or not they were announced. A registration made before play (the client is Created, or this session has not reached play yet) is announced by OnPlayStarted instead.
        if (announceNow)
            AnnounceChannel(channel, register: true, ct);

        return new PluginChannelRegistration(channel, () => Unregister(phase, channel, onMessage, ct));
    }

    /// <summary>Sends on a channel named by a RAW string rather than an identifier. Only one caller needs this: the 1.8-1.12.2 brand channel is spelled <c>MC|Brand</c>, which is not a valid namespaced identifier and therefore cannot be expressed by the public overload at all.</summary>
    internal ValueTask SendRawAsync(string channel, ReadOnlyMemory<byte> data, CancellationToken ct)
        => SendCore(channel, data, ct);

    public ValueTask SendAsync(Identifier channel, ReadOnlyMemory<byte> data, CancellationToken ct)
        => SendCore(channel.ToString(), data, ct);

    /// <summary>Sends a serverbound CONFIGURATION-phase custom payload. Unlike the play send this encodes the bound record rather than writing a raw frame, because the configuration custom payload is a real modelled packet on every version that has the phase at all.</summary>
    /// <exception cref="ActionNotSupportedException">The negotiated version has no configuration phase (pre-1.20.2), so there is no such packet to send.</exception>
    public ValueTask SendConfigurationAsync(Identifier channel, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (!CanSendConfiguration)
            throw new ActionNotSupportedException(
                "SendConfigurationAsync", CustomPayloadId, _protocol,
                "This version has no configuration phase, so it has no configuration custom_payload.");

        return _sink.SendAsync(new ServerboundConfigCustomPayloadPacket(channel, data.ToArray()), ct);
    }

    private ValueTask SendCore(string channel, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        // The identifier lookup IS the right question here, unlike the record-encoding action surfaces: this writes a raw frame, so there is no codec to be a marker.
        int wireId = _wire.ServerboundPlay(CustomPayloadId);
        if (wireId < 0)
            throw new ActionNotSupportedException(
                "SendPluginMessageAsync", CustomPayloadId, _protocol,
                "This version has no serverbound play custom_payload wire id.");

        var body = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(body);
        writer.WriteString(channel);
        writer.WriteBytes(data.Span);
        return _sink.SendFrameAsync(wireId, body.WrittenMemory, ct);
    }

    /// <summary>Handles an observed clientbound PLAY custom_payload frame. Returns true when routed.</summary>
    public bool DispatchInbound(int wireId, ReadOnlySpan<byte> payload)
    {
        int expected = _wire.ClientboundPlay(CustomPayloadId);
        if (expected < 0 || wireId != expected)
            return false;

        string channelName;
        byte[] data;
        try
        {
            var reader = new PacketReader(payload);
            channelName = reader.ReadString();
            data = reader.ReadRemaining().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to decode inbound custom_payload frame.");
            return false;
        }

        var memory = new ReadOnlyMemory<byte>(data);

        // The register/unregister convention first, by RAW name: the pre-1.13 spellings are not valid identifiers, so a name-keyed check is the only one that can see them. Recording the announcement does not consume the frame; a handler registered on minecraft:register still gets it.
        bool announced = TryRecordAnnouncement(channelName, memory);
        bool routed = Identifier.TryParse(channelName, out Identifier channel)
            && Fanout(ProtocolPhase.Play, channel, memory);
        return announced || routed;
    }

    /// <summary>Handles a decoded clientbound CONFIGURATION custom payload. The configuration phase decodes into a real record before it reaches the applier, so there is no frame to re-parse here.</summary>
    public bool DispatchConfiguration(Identifier channel, ReadOnlyMemory<byte> data)
    {
        bool announced = TryRecordAnnouncement(channel, data);
        bool routed = Fanout(ProtocolPhase.Configuration, channel, data);
        return announced || routed;
    }

    /// <summary>Called once per session, at the moment play begins, to put the register announce on the wire for every channel that already has a play handler. A registration made before the dial could not announce when it was made; this is where it does.</summary>
    public void OnPlayStarted(CancellationToken ct)
    {
        Identifier[] channels;
        lock (_gate)
        {
            _playLive = true;
            channels = [.. _playHandlers.Keys];
        }

        foreach (Identifier channel in channels)
            AnnounceChannel(channel, register: true, ct);

    }

    /// <summary>Called when a session ends. Handlers are owned by their registration handles (and, for the ones a plugin made, by <see cref="PluginHost.Detach"/>), so they are NOT dropped here. What resets is the announce gate, so a handler that outlives the session is announced again when the next one reaches play, and the server's announced set, which described the server that just went away.</summary>
    public void OnSessionEnded()
    {
        lock (_gate)
        {
            _playLive = false;
            if (_announced.Count > 0)
            {
                _announced.Clear();
                Volatile.Write(ref _announcedSnapshot, new HashSet<Identifier>());
            }
        }
    }

    private Dictionary<Identifier, List<Action<ReadOnlyMemory<byte>>>> MapFor(ProtocolPhase phase) =>
        phase == ProtocolPhase.Configuration ? _configHandlers : _playHandlers;

    private bool Fanout(ProtocolPhase phase, Identifier channel, ReadOnlyMemory<byte> data)
    {
        List<Action<ReadOnlyMemory<byte>>>? handlers;
        lock (_gate)
            handlers = MapFor(phase).TryGetValue(channel, out List<Action<ReadOnlyMemory<byte>>>? list)
                ? [.. list]
                : null;

        if (handlers is null)
            return false;

        foreach (Action<ReadOnlyMemory<byte>> handler in handlers)
            try
            {
                handler(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin-channel handler for {Channel} threw.", channel);
            }

        return true;
    }

    /// <summary>The identifier-keyed arm of the announcement check (1.13+, and the configuration phase).</summary>
    private bool TryRecordAnnouncement(Identifier channel, ReadOnlyMemory<byte> payload)
    {
        if (channel == RegisterChannel)
        {
            RecordAnnouncement(register: true, payload);
            return true;
        }

        if (channel == UnregisterChannel)
        {
            RecordAnnouncement(register: false, payload);
            return true;
        }

        return false;
    }

    /// <summary>The raw-name arm, which is the only one that can see the pre-1.13 spellings.</summary>
    private bool TryRecordAnnouncement(string channelName, ReadOnlyMemory<byte> payload)
    {
        if (channelName is LegacyRegisterChannel)
        {
            RecordAnnouncement(register: true, payload);
            return true;
        }

        if (channelName is LegacyUnregisterChannel)
        {
            RecordAnnouncement(register: false, payload);
            return true;
        }

        return Identifier.TryParse(channelName, out Identifier channel) && TryRecordAnnouncement(channel, payload);
    }

    /// <summary>Folds one register/unregister announcement into <see cref="ServerAnnounced"/>. The payload is the server's channel list joined with NUL bytes, the same shape this client writes in <see cref="SendRegisterAsync"/>.</summary>
    /// <remarks>A name that is not a valid identifier is skipped rather than recorded under some coerced form: the set is identifier-keyed because that is the currency of the whole channel API, and a pre-1.13 legacy name such as <c>FML|HS</c> genuinely cannot be expressed in it. Such a server's announcement stays visible in the raw frame feed but not in this set, which is a stated limit rather than a silent loss.</remarks>
    private void RecordAnnouncement(bool register, ReadOnlyMemory<byte> payload)
    {
        List<Identifier> added = [];
        List<Identifier> removed = [];
        IReadOnlySet<Identifier>? snapshot = null;

        lock (_gate)
        {
            foreach (string name in Encoding.UTF8.GetString(payload.Span).Split('\0'))
            {
                if (!Identifier.TryParse(name, out Identifier channel))
                    continue;

                if (register)
                {
                    if (_announced.Add(channel))
                        added.Add(channel);

                }
                else if (_announced.Remove(channel))
                    removed.Add(channel);

            }

            if (added.Count > 0 || removed.Count > 0)
            {
                snapshot = new HashSet<Identifier>(_announced);
                Volatile.Write(ref _announcedSnapshot, snapshot);
            }
        }

        if (snapshot is null)
            return;

        try
        {
            AnnouncementChanged?.Invoke(new ServerAnnouncedChannelsChangedEventArgs(snapshot, added, removed));
        }
        catch (Exception ex)
        {
            // Raised from the read loop (play) or the session loop (configuration). A subscriber that throws must not take either down, and least of all must it stop the frame that carried the announcement from reaching its own handlers.
            _logger.LogError(ex, "A ServerAnnouncedChanged handler threw.");
        }
    }

    private void Unregister(
        ProtocolPhase phase, Identifier channel, Action<ReadOnlyMemory<byte>> handler, CancellationToken ct)
    {
        bool announce;
        lock (_gate)
        {
            bool removedChannel = false;
            Dictionary<Identifier, List<Action<ReadOnlyMemory<byte>>>> map = MapFor(phase);
            if (map.TryGetValue(channel, out List<Action<ReadOnlyMemory<byte>>>? list))
            {
                list.Remove(handler);
                if (list.Count == 0)
                {
                    map.Remove(channel);
                    removedChannel = true;
                }
            }

            announce = removedChannel && phase == ProtocolPhase.Play && _playLive;
        }

        if (announce)
            AnnounceChannel(channel, register: false, ct);

    }

    /// <summary>Starts the <c>minecraft:register</c>/<c>minecraft:unregister</c> announce for a channel and OBSERVES it. <see cref="Register"/> and <see cref="Unregister"/> are synchronous, so the send cannot be awaited by the caller; discarding the task instead meant a failed announce left no trace at all, and a channel the server was never told about looks exactly like a channel the server chose to ignore. A failure is logged and nothing else happens: the announce is advisory (vanilla servers route plugin messages whether or not they were announced), so it must not fail the registration the caller already holds a handle to.</summary>
    private void AnnounceChannel(Identifier channel, bool register, CancellationToken ct) =>
        _ = ObserveAnnounceAsync(channel, register, ct);

    private async Task ObserveAnnounceAsync(Identifier channel, bool register, CancellationToken ct)
    {
        try
        {
            await SendRegisterAsync(channel, register, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "The plugin-channel {Action} announce for {Channel} failed.",
                register ? "register" : "unregister", channel);
        }
    }

    private ValueTask SendRegisterAsync(Identifier channel, bool register, CancellationToken ct)
    {
        int wireId = _wire.ServerboundPlay(CustomPayloadId);
        if (wireId < 0)
        {
            // Deliberately silent, unlike SendAsync: a version with no serverbound play custom_payload wire id cannot carry the convention at all, which is a fact about the protocol rather than a failure of this send. The caller-visible SendAsync is the one that has to be honest.
            return ValueTask.CompletedTask;
        }

        // The register/unregister payload is a NUL-joined list of channel names.
        byte[] name = Encoding.UTF8.GetBytes(channel.ToString());
        var body = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(body);
        writer.WriteString((register ? RegisterChannel : UnregisterChannel).ToString());
        writer.WriteBytes(name.AsSpan());
        return _sink.SendFrameAsync(wireId, body.WrittenMemory, ct);
    }
}
