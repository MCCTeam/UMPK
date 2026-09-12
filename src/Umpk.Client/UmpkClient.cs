using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using Umpk.Client.Actions;
using Umpk.Client.Commands;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Movement;
using Umpk.Client.Navigation;
using Umpk.Client.Plugins;
using Umpk.Client.Snapshots;
using Umpk.Commands;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Umpk.Protocol.Java.Transport;

namespace Umpk.Client;

/// <summary>One Minecraft Java client session. Owns the connection, the session loop, tracked state, the event bus, the action surface, and the loaded plugins. No statics; any number of instances may run in one process. All state mutation happens on the session loop; actions and <see cref="InvokeAsync{T}"/> are safe from any thread.</summary>
public sealed class UmpkClient : IAsyncDisposable
{
    private readonly UmpkClientSettings _settings;
    private readonly ILogger _logger;
    private readonly ISessionScheduler _scheduler;
    private readonly bool _ownsScheduler;
    private readonly ITickSource _tickSource;
    private readonly EventBus _eventBus;
    private readonly MovementLeaseManager _leases = new();
    private readonly CommandService<ClientCommandSource> _commands = new();
    private readonly WireIndex _wire;

    /// <summary>The play-phase plugin-message packet the server brand rides on before 1.20.2.</summary>
    private static readonly Identifier CustomPayloadChannelId = Identifier.Minecraft("custom_payload");

    /// <summary>Whether the negotiated version carries <c>client_tick_end</c>; resolved once at construction.</summary>
    private readonly bool _sendsTickEnd;

    private readonly ClientSessionServices _services;
    private readonly SequenceTracker _sequences = new();
    private readonly EntityTypeResolver _entityTypes = new();

    private readonly Umpk.Game.Entities.IMetadataKeySource _metadataKeys;
    private readonly Dispatcher _dispatcher;
    private readonly PhysicsEngineHolder? _physics;
    private readonly Navigator? _navigator;
    private readonly MovementReporter _movementReporter;
    private readonly MovementReadiness _movementReadiness = new();
    private readonly PluginChannelManager _channels;
    private readonly Commands.CommandCompletionService _commandCompletions;

    private readonly object _frameObserverGate = new();

    private JavaConnection? _connection;
    private Action<PacketObservation>? _frameObservers;
    private PacketSink? _sink;
    private ChatSigningCoordinator? _chatSigning;

    // 0 until the join packet has run the deferred chat-signing setup, 1 afterwards. A one-shot per connection: a server that sends a second join packet on the same connection must not turn the connect-time announcement into a key rotation, which is what a second SendSessionUpdateAsync would be (its finally sets the coordinator's _pastConnect).
    private int _chatSigningSetUp;
    private Func<Guid, SignedChatVerifier?>? _chatVerifiers;
    private MessageSignatureCache? _chatSignatureCache;
    private CancellationTokenSource? _sessionCts;
    private Task? _receiveTask;
    private Task? _tickTask;
    private DisconnectInfo? _disconnect;
    private int _disposed;

    // Backs Spawned. Created here so the property answers before ConnectAsync is ever called, and
    // RE-ARMED inside ConnectAsync itself (ArmSpawnedSignal) so every connection gets its own signal;
    // see the Spawned property doc for why the arming point is load-bearing.
    private TaskCompletionSource<bool> _spawned =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal UmpkClient(UmpkClientSettings settings)
    {
        _settings = settings;
        _logger = settings.LoggerFactory.CreateLogger("Umpk.Client");
        _ownsScheduler = settings.Scheduler is null;
        _scheduler = settings.Scheduler ?? new ChannelSessionScheduler(ex => _logger.LogError(ex, "Session loop work threw."));
        _tickSource = settings.TickSource ?? new PeriodicTimerTickSource();
        _eventBus = new EventBus(_logger, settings.Options.LoopStallThreshold, settings.Options.DefaultStreamCapacity);

        State = new ClientState(settings.Features);
        Events = new ClientEvents(_eventBus);
        Snapshots = new ClientSnapshots(this);
        // The one discarded task in this class that stays discarded, and the reason is structural rather than convenient: EventBus.PublishAsync isolates every handler in its own try/catch and its stream sinks cannot throw, so this task has no failure to observe. It also cannot be awaited - the lag report is raised from inside another publish (or from a stream consumer's thread), where awaiting would re-enter the bus.
        _eventBus.SetLagPublisher(lag => _ = _eventBus.PublishAsync(lag));

        // Registered once, for the client's whole lifetime, not per connection: PositionCorrected is published by the self applier for every ClientboundPlayerPositionPacket, and TrySetResult makes every one after the first (per arming) a no-op. Subscribing here, ahead of any call to ConnectAsync, is what makes the arming inside ConnectAsync race-free - see the Spawned property.
        _ = _eventBus.Subscribe<PositionCorrected>(_ => _spawned.TrySetResult(true));

        _wire = new WireIndex(settings.Version);
        _sendsTickEnd = _wire.CanSendPlay(PlayPackets.Serverbound.ClientTickEnd);
        _sink = null; // set on connect
        _services = new ClientSessionServices
        {
            Version = settings.Version,
            Options = settings.Options,
            Policies = settings.Policies,
            State = State,
            Wire = _wire,
            Logger = _logger,
            Scheduler = _scheduler,
            CurrentPhase = () => _connection?.Phase ?? ProtocolPhase.Play,
            CurrentSessionCancellation = () => _sessionCts?.Token,
            Events = _eventBus,
        };

        var deferredSink = new DeferredSink(() => _sink
            ?? throw new InvalidOperationException("The client is not connected."));

        // The tier-2 metadata index table for this version. Entity metadata was decoded and stored index-keyed on every protocol, but with no key source every typed read (custom name, pose, health) missed, which reads exactly like a server that never sent the field.
        _metadataKeys = Umpk.Data.Java.JavaGameData.EntityMetadataKeys(settings.Version.Version.Protocol);

        // Default to the version's generated shape tables, not the unit-cube fallback: without them the engine walks into an invisible wall at a slab and stands on top of a carpet. The flags-only source stays as the answer for a protocol with no generated data at all, which is what JavaGameData.BlockShapes already degrades to per uncovered state.
        _physics = settings.Features.Physics
            ? new PhysicsEngineHolder(
                _services,
                settings.BlockShapes ?? Umpk.Data.Java.JavaGameData.BlockShapes(settings.Version.Version.Protocol),
                _logger)
            : null;
        _movementReporter = new MovementReporter(_services);
        _navigator = _physics is not null
            ? new Navigator(_services, _physics, _leases, _logger, () => Actions?.Interaction)
            : null;

        // A piston push is a block-entity tick that runs on both sides and sends no packet for the displacement; the ONLY notice a client gets is this block event, because piston structure movement writes the moving_piston blocks without UPDATE_CLIENTS. Vanilla's client answers it by running trigger event behavior itself. Delivery is synchronous on the session loop, the same loop the tick runs on, so no locking is needed.
        if (_physics is not null)
            _ = _eventBus.Subscribe<Umpk.Client.Events.BlockEventOccurred>(
                evt => _physics.OnBlockEvent(evt.Position, evt.ActionId, evt.ActionParam));

        _channels = new PluginChannelManager(deferredSink, _wire, settings.Version.Version.Protocol, _logger);

        // Built here, in the constructor, so it is usable while the client is still Created: a channel registered before the dial is in place before the session's first inbound frame, which is the window IClientPlugin.Attach (post-play) structurally cannot cover.
        Channels = new ClientChannels(_channels, settings.Version);
        _commandCompletions = new Commands.CommandCompletionService(deferredSink);

        var chat = new ChatActions(
            deferredSink,
            _services,
            _commandCompletions,
            _commands,
            () => new ClientCommandSource(this, (message, token) =>
                _eventBus.PublishAsync(new Umpk.Client.Events.ChatMessageReceived(
                    message, Umpk.Client.Events.ChatCategory.System, SenderName: null, SenderId: null, IsOverlay: false))),
            // The signing coordinator is installed after login when a signing provider is configured on a signing-era version; absent it (offline, or a non-signing version) this stays the unsigned path. The send runs INSIDE the coordinator's ordering lock: a profile-key rotation swaps the session id, key and message index together and announces the swap on the wire, so a sender that resolved before the swap must not be able to send after the announcement. The factory is what guarantees the field is observed ONCE per send; the inline lambda this replaced read it twice and could NRE against a concurrent TeardownSessionAsync.
            ChatSigningScopes.Create(() => _chatSigning));
        var movement = new MovementActions(deferredSink, _services, () => _navigator, _movementReporter);
        var interaction = new InteractionActions(deferredSink, _services, _sequences);
        var inventory = new InventoryActions(deferredSink, _services);
        var session = new SessionActions(deferredSink, _services);
        var dialog = new DialogActions(deferredSink, _services, chat);
        Actions = new ClientActions(chat, movement, interaction, inventory, session, dialog, _services.Capabilities);

        _dispatcher = new Dispatcher(Appliers.ApplierCatalog.Build(_settings.Features));

        // Seeded from the builder's plugins, but that seed only matters once: AttachPlugins (via Plugins.BeginSessionAsync) always reads the collection's CURRENT membership at session start, so runtime AddAsync/RemoveAsync calls control every session after this one.
        Plugins = new ClientPluginCollection(_settings.Plugins, _scheduler, CreateHost, _logger);
    }

    /// <summary>The tracked session state.</summary>
    public ClientState State { get; }

    /// <summary>The typed event bus.</summary>
    public ClientEvents Events { get; }

    /// <summary>The off-loop read-model. <see cref="State"/> is the live tracker; this is what a UI thread or a web socket reads instead.</summary>
    public ClientSnapshots Snapshots { get; }

    /// <summary>
    /// The raw frame feed: every frame this client sends or receives, in frame order, with its real wire id, its phase, its direction, the raw decrypted and decompressed body bytes, and the decoded packet object when one exists. This is the pre-decode ground truth a recorder or a protocol inspector needs and that the decoded <see cref="Events.PacketReceived"/> event cannot give: that one is inbound only and carries no bytes.
    /// <para>Raised synchronously on the connection's read loop (inbound) or inside the caller's send (outbound), NOT on the session loop, so a handler must be cheap and must not block. The payload span is only valid for the duration of the callback: the inbound buffer is pooled and the outbound buffer is the connection's encode scratch. Call <see cref="PacketObservation.CopyPayload"/> to keep the bytes.</para>
    /// <para>Cost when nobody subscribes: nothing. Outbound observation is attached to the live connection only while at least one handler is registered, so an unobserved client never scans a VarInt, builds an observation, or copies a frame on the send path. Inbound frames are already observed unconditionally for server-brand capture and plugin-channel dispatch, so a subscriber there adds one delegate invocation per frame. With a listener attached the cost is one <see cref="PacketObservation"/> (a struct, no allocation) plus whatever the handler does; nothing is buffered or copied by the client itself.</para>
    /// </summary>
    public event Action<PacketObservation>? PacketFrameObserved
    {
        add
        {
            if (value is null)
                return;

            lock (_frameObserverGate)
            {
                bool wasEmpty = _frameObservers is null;
                _frameObservers += value;
                if (wasEmpty)
                    AttachOutboundObserver(_connection);

            }
        }

        remove
        {
            if (value is null)
                return;

            lock (_frameObserverGate)
            {
                _frameObservers -= value;
                if (_frameObservers is null)
                    DetachOutboundObserver(_connection);

            }
        }
    }

    /// <summary>Raised when a mapped inbound packet cannot be decoded and that error will terminate the session. The record contains bounded evidence and omits handshake/login payload bytes. Delivery occurs on the connection read path or the login/configuration driver path; handlers must not block. Throwing handlers are logged and contained so later subscribers still receive the failure.</summary>
    public event Action<PacketDecodeFailure>? PacketDecodeFailed;

    /// <summary>Subscribes to the raw frame feed through the non-escaping <see cref="PacketFrame"/> ref-struct view instead of the boxed-friendly <see cref="PacketObservation"/> event. Wraps <see cref="PacketFrameObserved"/>: same delivery thread (the connection's read loop for inbound, the caller's own thread inside its send for outbound, never the session loop), same cost contract (nothing when nobody is listening), same throwing-handler-is-logged-and-swallowed behavior, so a host bug in <paramref name="handler"/> cannot tear the session down. <see cref="PacketFrame.Payload"/> is only valid for the duration of the call; call <see cref="PacketFrame.CopyPayload"/> to keep the bytes. Lives for the client's lifetime (or until disposed); a plugin should use <see cref="Plugins.ClientPluginContext.ObservePackets"/> instead, which releases automatically on detach.</summary>
    /// <param name="handler">Called once per frame, both directions.</param>
    /// <returns>A handle that unsubscribes on dispose.</returns>
    public IDisposable ObservePackets(PacketFrameHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new PacketFrameSubscription(this, handler);
    }

    /// <summary>The client-initiated action surface.</summary>
    public ClientActions Actions { get; }

    /// <summary>Which version-optional actions the negotiated version can actually perform. Resolved from the version at construction, so it is answerable before connect. Shorthand for <see cref="ClientActions.Capabilities"/>.</summary>
    public ClientActionCapabilities Capabilities => Actions.Capabilities;

    /// <summary>The pathfinding/movement navigator; throws when the Pathfinding feature is off.</summary>
    public Navigator Navigation => _navigator ?? throw new FeatureDisabledException("Pathfinding");

    /// <summary>Facts about the current session, or null before connect.</summary>
    public SessionInfo? Session { get; private set; }

    /// <summary>Completes true once this session's player has been placed in the world (the initial position teleport applies), and completes false if the session ends before that happens. Armed at construction, so this is answerable before <see cref="ConnectAsync"/> is ever called, and RE-ARMED synchronously inside <see cref="ConnectAsync"/>, before its first await, so a fresh connection always gets its own fresh, initially-incomplete task rather than one left over from a previous connection on this client.</summary>
    /// <remarks>
    /// <para><see cref="ConnectAsync"/> returns as soon as the LOGIN phase finishes, before the play-phase join packet and the initial position teleport are applied, so a read taken at that instant returns the tracker's DEFAULTS - the origin, in survival - and a default is indistinguishable from a reading. This keys on the initial position teleport (<see cref="PositionCorrected"/>, published by the self applier when <c>ClientboundPlayerPositionPacket</c> is applied), not on <see cref="Umpk.Client.State.SelfState.HasSpawned"/>, which the connection applier already sets at the JOIN packet: at that exact moment a position read is still a default, not a reading.</para>
    /// <para>The re-arming has to happen inside <see cref="ConnectAsync"/> itself, synchronously and before its first await, because that is the only race-free point: a consumer that subscribed to the teleport only after <see cref="ConnectAsync"/> returned could miss it entirely and then wait forever. The subscription that completes this task is the client's own, installed once in the constructor rather than per connection, so it is already listening no matter how quickly the server responds - no caller can land in that window.</para>
    /// </remarks>
    public Task<bool> Spawned => _spawned.Task;

    /// <summary>Connects, then waits for <see cref="Spawned"/>. Returns true once the player has been placed in the world, or false if the session ended before that happened; never throws for a clean session end, so a caller supplies its own deadline through <paramref name="ct"/>.</summary>
    public async Task<bool> ConnectAndWaitForSpawnAsync(ServerEndpoint endpoint, CancellationToken ct = default)
    {
        await ConnectAsync(endpoint, ct).ConfigureAwait(false);
        return await Spawned.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The cookie bag this client answers <c>cookie_request</c> from and stores <c>store_cookie</c> into (1.20.5+). It outlives a single connection on purpose: cookies are how a transfer target recognises the client that was handed to it.</summary>
    public CookieStore Cookies { get; } = new();

    /// <summary>The profile configured for a not-yet-connected supervised attempt.</summary>
    internal GameProfile ConfiguredProfile => _settings.Profile;

    /// <summary>
    /// The login-phase plugin channels this client claims. Register on this BEFORE <see cref="ConnectAsync"/> is called (the client is in <see cref="ClientStatus.Created"/> then): the login driver reads the registry as each <c>minecraft:custom_query</c> arrives, and login is over before any per-session attach point exists. A channel nobody claimed keeps getting <c>understood = false</c>, which is what this client has always answered.
    /// <para>Claims are RELEASED when the session ends, so a responder holding session state cannot answer for the next one. A caller that reconnects the same client instance therefore re-registers; the supervised path builds a fresh client per attempt and registers on that.</para>
    /// </summary>
    public LoginQueryResponders LoginQueries { get; } = new();

    /// <summary>The plugin-channel surface: play and configuration registrations, the two sends, and the set of channels the server announced. Usable from <see cref="ClientStatus.Created"/> onward, which is how a registration lands before the session's first inbound frame.</summary>
    public ClientChannels Channels { get; }

    /// <summary>Whether the live connection negotiated encryption, which is exactly whether the server sent an encryption request and so whether it is running in online mode. False before a connection exists and after one ends. See <see cref="JavaConnection.IsEncrypted"/> for why a chat-signing host cares.</summary>
    public bool IsConnectionEncrypted => _connection?.IsEncrypted ?? false;

    /// <summary>The high-level lifecycle status. Never <see cref="ClientStatus.Authenticating"/> or <see cref="ClientStatus.Reconnecting"/>: both are supervisor-only states.</summary>
    public ClientStatus Status { get; private set; } = ClientStatus.Created;

    /// <summary>The reason the most recent session ended, or null before any session has ended. Populated even when the ending session never reached <see cref="ClientStatus.Playing"/>: a kick during login or configuration throws out of <see cref="ConnectAsync"/> before the receive loop starts, so <see cref="Events.Disconnected"/> never fires for it, but the applier that decoded the kick still records it here.</summary>
    public DisconnectInfo? LastDisconnect => _disconnect;

    /// <summary>Raised on every <see cref="Status"/> transition, synchronously, with the previous and current status and, on the terminal transition to <see cref="ClientStatus.Disconnected"/>, the <see cref="DisconnectInfo"/>. A throwing handler is logged and swallowed: this is the same contract <see cref="PacketFrameObserved"/> has, because a host bug in a status handler must not tear the session down.</summary>
    public event EventHandler<ClientStatusChangedEventArgs>? StatusChanged;

    /// <summary>The command hosting service for host commands, registered by <see cref="ClientCommandSource"/>. This tree feeds <see cref="Actions.ChatActions.CompleteAsync"/>'s merged completion; the reconstructed server command tree (<c>State.ServerCommands.Tree</c>) is a separate input to that same merge and never enters this service. A host with its own dispatcher for server commands should not register into this service for them.</summary>
    public CommandService<ClientCommandSource> Commands => _commands;

    /// <summary>The live plugin membership: which plugins are attached now, and which will attach at the start of this client's next session. Seeded from <see cref="UmpkClientBuilder.AddPlugin"/>, and mutable at runtime through <see cref="ClientPluginCollection.AddAsync"/> and <see cref="ClientPluginCollection.RemoveAsync"/>, including while a session is live.</summary>
    public ClientPluginCollection Plugins { get; }

    /// <summary>Connects, logs in, runs configuration, and enters play. Completes when play begins.</summary>
    public async Task ConnectAsync(ServerEndpoint endpoint, CancellationToken ct = default)
        => await ConnectWithIntentAsync(endpoint, transferIntent: false, ct).ConfigureAwait(false);

    internal Task ConnectForTransferAsync(ServerEndpoint endpoint, CancellationToken ct)
        => ConnectWithIntentAsync(endpoint, transferIntent: true, ct);

    private async Task ConnectWithIntentAsync(ServerEndpoint endpoint, bool transferIntent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Status != ClientStatus.Created && Status != ClientStatus.Disconnected)
            throw new InvalidOperationException($"Cannot connect while {Status}.");

        try
        {
            await ConnectCoreAsync(endpoint, transferIntent, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CloseReason reason = ex switch
            {
                ConnectionClosedException closed => closed.Reason,
                OperationCanceledException => CloseReason.Cancelled,
                _ => CloseReason.Local,
            };
            RecordDisconnect(new DisconnectInfo { Reason = reason, Fault = ex });
            await TeardownSessionAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ConnectCoreAsync(ServerEndpoint endpoint, bool transferIntent, CancellationToken ct)
    {

        SetStatus(ClientStatus.Connecting);
        _disconnect = null;
        _movementReporter.Reset();
        _movementReadiness.Reset();
        _navigator?.ResetSession();
        _sessionCts = new CancellationTokenSource();

        // Re-armed here, synchronously, after the status guard above and before this method's first await: a connect the guard rejects can never disturb a live session's signal, and everything from here on races against the fresh task only, never a fresh task racing this method's own suspension. See the Spawned property doc for the failure this closes.
        ArmSpawnedSignal();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _sessionCts.Token);
        using var connectTimeout = new CancellationTokenSource(_settings.Options.ConnectTimeout);
        using var connectLinked = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, connectTimeout.Token);

        // Resolve once and use the result for both the socket and intention packet. SRV discovery is attempted only for the default port; a resolver failure falls back to the supplied address.
        ServerEndpoint target = endpoint;
        if (endpoint.Port == ServerEndpoint.DefaultJavaPort)
            try
            {
                target = await _settings.Resolver.ResolveAsync(endpoint, connectLinked.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Address resolution for {Host} failed; connecting to the address as given.", endpoint.Host);
            }

        IDuplexPipe pipe = await _settings.ConnectionFactory.ConnectAsync(target, connectLinked.Token).ConfigureAwait(false);

        var connectionOptions = new JavaConnectionOptions
        {
            Logger = _logger,
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = _settings.Options.ReadIdleTimeout,
        };
        var connection = new JavaConnection(pipe, connectionOptions);
        var binding = new DescriptorFrameCodecBinding(_settings.Version.Protocol);
        connection.BindCodec(binding, PacketFlow.Clientbound);
        connection.PacketObserved += OnPacketObserved;
        connection.PacketDecodeFailed += OnPacketDecodeFailed;

        // A consumer may have subscribed to the raw feed before this connection existed (or before any connection existed at all), so the outbound hook is (re)attached per connection here rather than only in the event's add accessor.
        lock (_frameObserverGate)
            if (_frameObservers is not null)
                AttachOutboundObserver(connection);

        connection.Start();
        _connection = connection;
        _sink = new PacketSink(connection);

        // Resolve the signing era once (null when no provider or a non-signing version). The 1.19/1.19.1 eras carry the profile public key in login-start, so fetch it before login; the 1.19.3+ era moves the key to a post-login chat_session_update (sent below).
        ChatSignatureEra? signingEra =
            _settings.SigningProvider is not null
            && ChatSigningEras.TryFromFeature(_settings.Version.Features.ChatSigning, out ChatSignatureEra resolvedEra)
                ? resolvedEra
                : null;

        ProfilePublicKeyData? loginKey = null;
        PlayerCertificates? loginCertificates = null;
        if (signingEra is ChatSignatureEra.V1_19 or ChatSignatureEra.V1_19_1)
            try
            {
                PlayerCertificates? certs = await _settings.SigningProvider!
                    .GetCertificatesAsync(connectLinked.Token).ConfigureAwait(false);
                if (certs is not null)
                {
                    loginCertificates = certs;
                    loginKey = ProfileKeyMaterial.Build(certs, useV2Signature: signingEra == ChatSignatureEra.V1_19_1);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Chat-signing certificate acquisition for login start failed; logging in without a profile key.");
            }

        var loginOptions = new JavaLoginOptions
        {
            Username = _settings.Profile.Name,
            ServerHost = target.Host,
            ServerPort = target.Port,
            ProfileId = _settings.Profile.Id,
            Authenticator = _settings.Authenticator,
            Credentials = _settings.Credentials,
            PlayRegistries = _settings.StaticRegistries,
            ProfileKey = loginKey,
            // The 1.19/1.19.1 encryption response must sign the server's challenge with the profile PRIVATE key whenever loginKey (the public half) was attached to hello - see JavaLoginOptions.ProfileCertificates / JavaClientLogin.HandleEncryptionRequestAsync. Always set together with loginKey above (both null or both non-null), so this never trips the driver's ProfileKey-without-ProfileCertificates guard.
            ProfileCertificates = loginCertificates,
            // Configuration-phase dialogs run through the normal applier pipeline, so they land in the same ClientState.Dialogs and raise the same events as the play-phase ones. Applied inline on the connect path: the session loop is not pumping packets yet, and the driver awaits this before reading the next configuration frame, which preserves frame order.
            ConfigurationPacketObserver = (observed, token) => ApplyPacketAsync(
                observed.Packet, ProtocolPhase.Configuration, observed.WireId, observed.PayloadLength, token,
                required: true),
            ClientInformation = _settings.Options.ClientInformation,
            // Whatever a host claimed while this client was Created. An empty collection preserves the driver's default "not understood" response.
            LoginQueries = LoginQueries,
            Cookies = Cookies,
            TransferIntent = transferIntent,
            BeforePlay = _ => ValueTask.FromResult(State.Registries),
            // Only reaches the wire on a version that HAS a configuration phase. The pre-1.20.2 eras announce the brand in play instead, which is done below once play is live.
            ClientBrand = _settings.Options.ClientBrand,
        };

        // Expose the static registries to state consumers (world block source, entity type resolver).
        State.Registries = _settings.StaticRegistries;

        SetStatus(ClientStatus.Configuring);
        LoginResult login = await JavaClientLogin.LoginAsync(connection, _settings.Version, loginOptions, connectLinked.Token)
            .ConfigureAwait(false);

        State.Self.Uuid = login.Uuid;
        State.Self.Username = login.Username;

        // Inbound per-peer signature verification. This is independent of OUR certificates: verifying a peer needs only the peer's announced profile key and the version's signature era, so it is installed on every signing-era session, provider or not. Null on 47-758, where no peer ever announces a key. Held per session so a reconnect starts every peer chain fresh.
        _chatVerifiers = PeerChatVerifiers.CreateResolver(
            State, _settings.Version.Features.ChatSigning, TimeProvider.System, _logger);

        // One signature cache for the whole connection, shared across every peer's verifier above (see ApplierContext.SignatureCache / SignedChatVerification.Verify): the server's own compression cache for player_chat last-seen entries is per-connection, not per-sender, so this side has to mirror that scope to resolve a cache-id reference no matter which peer's message carries it. Null exactly when _chatVerifiers is null (no signing era at all), for the same reason: a version with no chat signing never carries a last-seen window to resolve.
        _chatSignatureCache = _chatVerifiers is not null ? new MessageSignatureCache() : null;

        // Install chat signing when a provider is configured and the negotiated version has a signing era. The client owns the session/era; the provider only yields certificates. Absent either, the send path stays unsigned.
        if (signingEra is { } era)
        {
            // The sink, the chat state and the login-announced certificates are all part of the rotation contract: a key is only ever replaced when the replacement can be announced on this era's wire. On 1.19/1.19.1 the only announcement channel is the login hello, which has already gone out by here, so the coordinator signs with the certificates seeded below or with nothing at all. loginCertificates is null when the login-time fetch failed or returned none, i.e. when the hello carried no key: the coordinator then refuses to acquire one rather than signing with material the server was never given.
            var coordinator = new ChatSigningCoordinator(
                login.Uuid, Guid.NewGuid(), era, _settings.SigningProvider!, TimeProvider.System, _logger,
                _sink, State.Chat, loginCertificates);
            _chatSigning = coordinator;

            // The setup itself is DEFERRED to the join packet; see SetUpChatSigningAsync. Arm it here, once per connection, so a reconnect through this same client runs it again.
            Volatile.Write(ref _chatSigningSetUp, 0);
        }

        Session = new SessionInfo
        {
            Profile = _settings.Profile with { Id = login.Uuid },
            Endpoint = target,
            Version = _settings.Version,
            Phase = ProtocolPhase.Play,
        };

        // A vanilla server changes its INBOUND decoder from LOGIN to PLAY when it writes its first clientbound PLAY frame, not when it writes login success. Until that frame has crossed the wire, every serverbound play packet (brand, REGISTER, plugin traffic, or a host action) can be decoded against the three-entry LOGIN table and disconnect the session. Receive and apply that first item before opening any local play sender. An unknown/marker item is sufficient: vanilla switched before writing it, and no packet-specific JoinGame assumption is needed here.
        InboundItem firstPlayItem = await connection.ReceiveAsync(linked.Token).ConfigureAwait(false);
        await ProcessInboundItemAsync(firstPlayItem, linked.Token).ConfigureAwait(false);

        SetStatus(ClientStatus.Playing);

        // The brand, on the eras that have no configuration phase to announce it in. 1.20.2+ already sent it during login (JavaConfigurationOptions.AnnounceBrand); 1.8 through 1.20.1 announce in play, which is only reachable from here. Best-effort by construction: a server that rejects or ignores a custom payload is not a reason to fail a session that has already joined.
        await AnnouncePlayBrandAsync(linked.Token).ConfigureAwait(false);

        // Play is live, so the register announce for every channel that was registered BEFORE this point (while the client was Created, or during login) can finally go out. Deliberately after the brand, which is the order vanilla's own client sends them in, and before the plugin attach below, whose own registrations announce themselves immediately because the gate is now open.
        _channels.OnPlayStarted(linked.Token);

        // The scheduler drains itself (default) or is pumped by the host. Attach plugins first (marshalled onto the session loop, so IClientPlugin.Attach genuinely runs there), then start the inbound pump and the tick driver.
        await Plugins.BeginSessionAsync(linked.Token).ConfigureAwait(false);
        CancellationTokenSource sessionCts = _sessionCts;
        _receiveTask = ReceiveLoopAsync(connection, sessionCts.Token);
        _tickTask = TickLoopAsync(connection, sessionCts);

        await _eventBus.PublishAsync(new PhaseChanged(ProtocolPhase.Play)).ConfigureAwait(false);
    }

    /// <summary>Announces the configured client brand in the PLAY phase, for the versions that have no configuration phase to announce it in (1.8 through 1.20.1). The channel changed with 1.13 and the older name is not a namespaced identifier, so the two eras are spelled separately; both bodies are the same single length-prefixed string. See <see cref="Internal.ServerBrandPayload"/> for the per-era evidence.</summary>
    private async ValueTask AnnouncePlayBrandAsync(CancellationToken ct)
    {
        if (_settings.Options.ClientBrand is not { Length: > 0 } brand
            || _settings.Version.Features.ConfigurationPhase)
            return;

        try
        {
            var body = new System.Buffers.ArrayBufferWriter<byte>();
            var writer = new Umpk.Protocol.Java.Codecs.PacketWriter(body);
            writer.WriteString(brand);

            if (_settings.Version.Version.Protocol >= FirstNamespacedBrandChannel)
                await _channels.SendAsync(Internal.ServerBrandPayload.Channel, body.WrittenMemory, ct)
                    .ConfigureAwait(false);

            else
                await _channels.SendRawAsync(LegacyBrandChannel, body.WrittenMemory, ct).ConfigureAwait(false);

        }
        catch (Exception ex) when (ex is ActionNotSupportedException or ProtocolViolationException or IOException
            or ObjectDisposedException or InvalidOperationException)
        {
            // A brand nobody could send is a cosmetic loss, not a failed join.
            _logger.LogDebug(ex, "Could not announce the client brand.");
        }
    }

    /// <summary>1.13, the first protocol whose brand channel is the namespaced <c>minecraft:brand</c>.</summary>
    private const int FirstNamespacedBrandChannel = 393;

    /// <summary>The 1.8-1.12.2 brand channel, which is not a valid namespaced identifier.</summary>
    private const string LegacyBrandChannel = "MC|Brand";

    /// <summary>Marshals value-returning work onto the session loop for a strongly-consistent read.</summary>
    public Task<T> InvokeAsync<T>(Func<UmpkClient, T> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _scheduler.InvokeAsync(() => work(this), ct);
    }

    /// <summary>Posts void work onto the session loop and awaits its completion.</summary>
    public Task PostAsync(Action<UmpkClient> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _scheduler.InvokeAsync(() => work(this), ct);
    }

    /// <summary>Disconnects the session cleanly.</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        _disconnect ??= new DisconnectInfo { Reason = CloseReason.Local };
        if (_connection is not null)
            await _connection.CloseAsync(CloseReason.Local, ct).ConfigureAwait(false);

        await TeardownSessionAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during client dispose.");
        }

        if (_ownsScheduler)
            await _scheduler.DisposeAsync().ConfigureAwait(false);

    }

    private async Task ReceiveLoopAsync(JavaConnection connection, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                InboundItem item = await connection.ReceiveAsync(ct).ConfigureAwait(false);
                await ProcessInboundItemAsync(item, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RecordDisconnect(new DisconnectInfo { Reason = CloseReason.Local });
        }
        catch (ConnectionClosedException ex)
        {
            RecordDisconnect(new DisconnectInfo { Reason = ex.Reason, Fault = ex });
        }
        catch (Exception ex)
        {
            RecordDisconnect(new DisconnectInfo { Reason = CloseReason.ProtocolViolation, Fault = ex });
        }
        finally
        {
            // The session is over whether or not the teleport ever arrived; TrySetResult is a no-op if Spawned already completed true, and false if the server never placed the player at all.
            _spawned.TrySetResult(false);
            await _eventBus.PublishAsync(new Events.Disconnected(_disconnect ?? new DisconnectInfo { Reason = CloseReason.Local }))
                .ConfigureAwait(false);
            SetStatus(ClientStatus.Disconnected);
        }
    }

    /// <summary>Processes one complete clientbound PLAY item. Used both by the normal receive loop and by the connect-time readiness gate, so the first frame has exactly the same bundle, marker and scheduler semantics as every later frame.</summary>
    private async ValueTask ProcessInboundItemAsync(InboundItem item, CancellationToken ct)
    {
        if (item.Bundle is PacketBundle bundle)
        {
            if (bundle.Packets.Count != 0)
                await _scheduler.InvokeAsync(() => ApplyBundleOnLoopAsync(bundle), ct).ConfigureAwait(false);

            return;
        }

        // A frame-only item is an unmapped wire id or a registered marker under UnknownPacketPolicy.Preserve. Its readable content already reached PacketObserved synchronously on the connection read loop.
        if (item.Packet is null)
            return;

        await _scheduler.InvokeAsync(
            () => ApplyOnLoopAsync(item.Packet, item.Frame.WireId, item.Frame.Payload.Length), ct).ConfigureAwait(false);
    }

    private ValueTask ApplyOnLoopAsync(object packet, int wireId, int payloadLength)
        => ApplyPacketAsync(packet, ProtocolPhase.Play, wireId, payloadLength, CancellationToken.None);

    /// <summary>Applies the contents of one closed bundle (1.19.4+), in wire order, inside a single session-loop turn.</summary>
    /// <remarks>
    /// The sub-packets are applied individually and in order, and the standard client exposes no grouping to anything downstream of the listener, so neither does this. What the bundle buys is that the group is one unit of work on the packet-processing thread, which is why the whole loop runs inside ONE <see cref="ISessionScheduler.InvokeAsync(Func{ValueTask}, CancellationToken)"/>: a tick or a caller's <see cref="InvokeAsync{T}"/> cannot land between an <c>add_entity</c> and the paired <c>set_entity_data</c>, so no observer sees a half-introduced entity.
    /// <para>Each sub-packet publishes its own <see cref="Events.PacketReceived"/> with the wire id and byte count of the frame it actually arrived in, carried on <see cref="PacketBundle.Frames"/>. The bundle item's own <c>Frame</c> is <c>default</c> and is deliberately not used: reporting the bundle's absent identity for its contents would be a fabricated wire id.</para>
    /// </remarks>
    private async ValueTask ApplyBundleOnLoopAsync(PacketBundle bundle)
    {
        IReadOnlyList<object> packets = bundle.Packets;
        IReadOnlyList<InboundFrame> frames = bundle.Frames;
        for (int i = 0; i < packets.Count; i++)
        {
            // A bundled marker rides in the bundle as an UnknownPacket so it keeps its place in wire order, but there is nothing to dispatch and nothing to name in PacketReceived. Skipping it is exactly what the null-Packet arm of the receive loop does for an unbundled marker, so a marker looks the same to a consumer whether or not it arrived inside a bundle.
            if (packets[i] is UnknownPacket)
                continue;

            InboundFrame frame = frames[i];
            int wireId = frame.WireId;
            int payloadLength = frame.Payload.Length;
            await ApplyPacketAsync(packets[i], ProtocolPhase.Play, wireId, payloadLength, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>The connect-time chat-signing setup, run from the JOIN applier rather than from the connect path: acquire the profile certificates, then announce the chat session. Idempotent per connection.</summary>
    /// <remarks>
    /// <para>Why it cannot run at connect time is a wire fact, not a preference: a vanilla server switches its channel protocol to PLAY only when it sends its own first play packet (the join packet), so between login success and the join packet its inbound decoder still holds the LOGIN packet set. A <c>chat_session_update</c> sent in that window is looked up in a three-entry list by its play-phase wire id and the server dies with <c>IndexOutOfBoundsException: Index 32 out of bounds for length 3</c>, then disconnects with the login disconnect packet, which this client decodes under the play table as <c>add_entity</c>. See <see cref="Internal.ApplierContext.AnnounceChatSession"/> for the ordering contract.</para>
    /// <para>The certificate acquisition moved with the announcement deliberately: the coordinator announces a key in the same lock hold that installs it (<c>ReplaceCertificatesAsync</c>), so acquiring early and announcing late would put the frame on the wire at acquisition time anyway and reopen the window. Nothing can be signed before the join in any case.</para>
    /// </remarks>
    private async ValueTask SetUpChatSigningAsync(CancellationToken ct)
    {
        if (_chatSigning is not { } coordinator || Interlocked.Exchange(ref _chatSigningSetUp, 1) != 0)
            return;

        try
        {
            // Acquire certificates so the first signed send has them and any failure surfaces here.
            await coordinator.EnsureAsync(ct).ConfigureAwait(false);

            // On 1.19.3+ signed chat is only accepted after the client announces its chat session and profile key. Send it once, on the join, before any signed chat.
            await coordinator.SendSessionUpdateAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The coordinator announces the key before installing it. A failed announcement therefore leaves no signing key installed, and chat cannot use a key the server was never told about.
            _logger.LogWarning(
                ex, "Chat-signing setup failed; the profile key was not announced and chat will not be signed.");
        }
    }

    private async ValueTask ApplyPacketAsync(
        object packet, ProtocolPhase phase, int wireId, int payloadLength, CancellationToken ct,
        bool required = false)
    {
        required |= packet is ClientboundLoginPacket or ClientboundRespawnPacket;

        var ctx = new ApplierContext
        {
            State = State,
            Events = _eventBus,
            Sink = _sink!,
            Logger = _logger,
            Version = _settings.Version,
            Features = _settings.Features,
            Policies = _settings.Policies,
            Wire = _wire,
            EntityTypes = _entityTypes,
            MetadataKeys = _metadataKeys,
            WorldSetup = SetupWorld,
            PhysicsConditionsDirty = () => _physics?.PushConditions(),
            PhysicsPositionDirty = () => _physics?.ResyncPosition(),
            PhysicsVelocityDirty = () => _physics?.ApplyVelocity(),
            CommandCompletions = _commandCompletions,
            Sequences = _sequences,
            DispatchConfigurationChannel = _channels.DispatchConfiguration,
            ChatVerifierResolver = _chatVerifiers,
            LastSeenTracker = _chatSigning?.Tracker,
            RecordDisconnect = RecordDisconnect,
            Cookies = Cookies,
            LegacyLastSeenCollector = _chatSigning?.LegacyCollector,
            SignatureCache = _chatSignatureCache,
            EnterConfiguration = EnterConfigurationAsync,
            AnnounceChatSession = _chatSigning is null ? null : SetUpChatSigningAsync,
        };

        try
        {
            await _dispatcher.DispatchAsync(packet, ctx, ct).ConfigureAwait(false);
            await _eventBus.PublishAsync(new PacketReceived(phase, wireId, packet, payloadLength)).ConfigureAwait(false);
        }
        catch (ConnectionClosedException)
        {
            // Not an applier bug: this is the transport itself saying the session is over. It reaches here from the 1.20.2+ play-to-configuration re-entry (ClientboundStartConfigurationPacket's applier calls ApplierContext.EnterConfiguration, which runs JavaClientLogin.RunConfigurationPhaseAsync and throws this exception straight out of a configuration-phase kick). Swallowing it here, like a genuine applier fault, meant the receive loop's own catch (ConnectionClosedException) arm - the one that records DisconnectInfo and publishes Events.Disconnected - was never reached for that path, so a config-phase kick after play could leave the session silently hung instead of ended. Rethrowing lets it unwind to that arm exactly the way an initial-flow kick already does out of ConnectAsync.
            throw;
        }
        catch (Exception ex) when (!required)
        {
            _logger.LogError(ex, "Applier for {PacketType} threw.", packet.GetType().Name);
        }
    }

    /// <summary>Takes the live connection into the configuration phase and runs it until the server ends it, at which point the connection is back in play. Called by the <c>start_configuration</c> applier, on the session loop, AFTER it has acknowledged.</summary>
    /// <remarks>
    /// This runs the same configuration driver as login. The only difference is the client-information announcement, which is sent on login but not on re-entry because the server preserves it.
    /// <para>Reading the connection directly from here is safe precisely because the caller is <see cref="ReceiveLoopAsync"/>: that loop awaits this work item on the session scheduler, so it is not itself draining <see cref="JavaConnection.ReceiveAsync"/> while the configuration phase runs. The tick loop queues behind the same scheduler and cannot interleave a play-phase send into the configuration phase.</para>
    /// </remarks>
    private async ValueTask EnterConfigurationAsync(CancellationToken ct)
    {
        if (_connection is not { } connection)
            return;

        SetStatus(ClientStatus.Configuring);
        if (Session is { } session)
            Session = session with { Phase = ProtocolPhase.Configuration };

        // Releases the read loop, which is parked at the start_configuration frame boundary, and swaps the descriptor so the following frames decode as configuration packets.
        connection.SetPhase(ProtocolPhase.Configuration);

        await JavaClientLogin.RunConfigurationPhaseAsync(
            connection,
            _settings.Version,
            new JavaConfigurationOptions
            {
                // The same observer the login path installs, so a configuration packet lands in the same ClientState through the same applier chain whichever entry brought the session here.
                PacketObserver = (observed, token) => ApplyPacketAsync(
                    observed.Packet, ProtocolPhase.Configuration, observed.WireId, observed.PayloadLength, token,
                    required: true),
                AnnounceClientInformation = null,
                PlayRegistries = _settings.StaticRegistries,
                Cookies = Cookies,
                BeforePlay = _ => ValueTask.FromResult(State.Registries),
            },
            ct).ConfigureAwait(false);

        // Reached only when the server ended the phase; a server that closes or kicks during configuration throws out of the driver and the receive loop records the disconnect instead.
        SetStatus(ClientStatus.Playing);

        if (Session is { } resumed)
            Session = resumed with { Phase = ProtocolPhase.Play };

        await _eventBus.PublishAsync(new PhaseChanged(ProtocolPhase.Play)).ConfigureAwait(false);
    }

    /// <summary>Creates and installs the world only after the current dimension can be resolved from the finalized session registries. Callers compute this before mutating prior join/respawn state, so a required modern dimension failure cannot leave a partially replaced world behind.</summary>
    private void SetupWorld(CommonWorldSetup setup)
    {
        if (!_settings.Features.Terrain)
            return;

        Game.Registries.RegistryAccess? registries = State.Registries;
        Game.Registries.Registry<Game.Registries.BlockDefinition>? blocks = registries?.Blocks;

        // Pre-flattening versions key block states as (id << 4) | meta, which is the identity TryDecodeLegacy exposes; the flattening at 1.13 replaced it with a flat state index and there is no such pair to recover after it.
        bool legacyStates = _settings.Version.Version.Protocol < FirstFlattenedProtocol;
        var blockData = blocks is not null
            ? (Game.Blocks.IBlockDataSource)new RegistryBlockDataSource(blocks, legacyStates)
            : new RegistryBlockDataSource(EmptyBlockRegistry(), legacyStates);

        Game.World.DimensionState dimension = WorldFactory.CreateDimension(
            setup, registries, _settings.Version.Version.Protocol);
        Game.Registries.Registry<Game.Registries.BiomeDefinition> biomes = WorldFactory.EmptyBiomes();
        var world = new Game.World.World(dimension, blockData, biomes);
        State.InstallWorld(world);
        _movementReadiness.Reset();
        _physics?.EnsureEngine();
    }

    /// <summary>The first protocol whose block states are a flat index rather than <c>(id &lt;&lt; 4) | meta</c> (1.13, the flattening). Below it the legacy id/meta pair is a real identity a consumer can read.</summary>
    private const int FirstFlattenedProtocol = 393;

    private static Game.Registries.Registry<Game.Registries.BlockDefinition> EmptyBlockRegistry()
        => WorldFactory.EmptyBlocks();

    private async Task TickLoopAsync(JavaConnection connection, CancellationTokenSource sessionCts)
    {
        CancellationToken ct = sessionCts.Token;
        try
        {
            // Hoisted so the loop allocates one delegate for the whole session rather than one per tick.
            Func<ValueTask> onTick = () => OnTickOnLoopAsync(ct);
            await foreach (long tick in _tickSource.Ticks(ct).ConfigureAwait(false))
                await _scheduler.InvokeAsync(onTick, ct).ConfigureAwait(false);

        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The core session tick owns essential simulation. Continuing the transport after it dies leaves a client that looks connected but can no longer advance or report movement. Record the LOCAL fault first, cancel this session's work, and close only this session's captured connection. The receive loop owns the single Disconnected publication. Do not call full teardown here: it joins _tickTask and would make this task await itself.
            _logger.LogError(ex, "The core session tick failed; ending the local session.");
            RecordDisconnect(new DisconnectInfo { Reason = CloseReason.Local, Fault = ex });

            try
            {
                sessionCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // External teardown won the race; the recorded fault still remains first-wins.
            }

            try
            {
                await connection.CloseAsync(CloseReason.Local, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception closeError) when (closeError is ObjectDisposedException or ConnectionClosedException)
            {
                _logger.LogDebug(closeError, "The core-tick failure raced completed connection teardown.");
            }
        }
    }

    /// <summary>One tick of the session loop: expire the tick-rate estimate, run the plugin tick queues, run the idle physics step, and send the per-tick packets.</summary>
    /// <remarks>The two sends are awaited in order inside the tick's work item. This makes send failures observable and ensures <c>client_tick_end</c> cannot reach the wire ahead of the movement packet it closes. A send failure is reported while the receive loop records the broken connection.</remarks>
    private async ValueTask OnTickOnLoopAsync(CancellationToken ct)
    {
        // The session's own tick clock, advanced first so everything below dates against the tick it is running in. It is not gated on any feature - only the physics block further down is - because what it dates (today, effect apply stamps) is written by appliers that run whatever the feature set is.
        State.AdvanceSessionTick();

        // A server that stops ticking stops broadcasting game time, so the tick-rate estimate can only expire on our own clock. Without this a paused server (1.21.2+ pause-when-empty) or a hung one would keep reporting the last rate it managed forever.
        State.Server.ExpireStaleTickRate(TimeProvider.System.GetTimestamp(), TimeProvider.System);

        // Vanilla tick behavior checks the profile key for a due refresh once a tick. This call only STARTS a background fetch and is never awaited here: it returns synchronously, does no I/O on the loop, and changes no signing state. The fetched key is installed and announced on the chat send path instead, so the announcement can never be overtaken by a message that was signed with the outgoing key.
        _ = _chatSigning?.BeginRefreshIfDue(ct);

        // Plugin pre-tick, physics idle tick, plugin post-tick. Position cadence. A snapshot, not a live enumeration: RemoveAsync (and the collection's own end-of-session teardown) can mutate the attached-host set from off this loop between ticks.
        foreach (PluginHost host in Plugins.SnapshotHosts())
            host.Scheduler.Ticks.Tick(ex => _logger.LogError(ex, "Plugin {Plugin} tick threw.", host.Id));

        // Placement and terrain feed the era-specific readiness owner below. Older clients continue to consult the current column, protocol 770 has its bounded fallback, and later load-tracker clients retain readiness after the first usable terrain. This prevents initial free fall into an empty world without imposing an incorrect universal own-column gate after loading.
        //
        // The position report itself is no longer gated on movementFree. A movement lease (Navigator.MoveToAsync / NavigateAsync) suppresses the idle physics step for its whole run, but it moves the tracked position on every tick of its own. Reporting that position each tick keeps long navigations below the server's move-distance limit instead of sending one large displacement when the lease ends.
        bool placed = _spawned.Task.IsCompletedSuccessfully && _spawned.Task.Result;
        ReadinessTick readiness = _movementReadiness.Advance(
            _settings.Version.Version.Protocol,
            placed && State.HasWorld,
            State.IsLocalChunkLoaded);

        if (readiness.AnnouncePlayerLoaded && _sink is { } readinessSink)
            await SendTickPacketAsync(
                    () => readinessSink.SendAsync(new ServerboundPlayerLoadedPacket(), CancellationToken.None),
                    "player_loaded")
                .ConfigureAwait(false);

        if (_settings.Features.Physics && readiness.MovementEligible)
        {
            // Breath runs before the movement step and regardless of the lease, so it reads the water state the previous tick's movement left behind; and it ticks on every tick the player is alive, not only the ones where nothing else is steering. The server's own value overwrites this one whenever a set_entity_data for the local player arrives, on this same loop.
            _physics?.TickAirSupply();

            bool controlsSelf = State.Self.CameraEntityId is null;
            bool movementFree = _leases.CurrentOwner is null;
            if (controlsSelf && _physics is { } physics)
            {
                long beforeStep = physics.StepSequence;
                bool navigationActive = _navigator?.HasActiveOperation == true;
                if (navigationActive)
                    _navigator!.TickOnLoop();

                // Planning and interaction acknowledgment are asynchronous controller work, not a reason for the local body to stop falling or colliding. If the controller did not consume this tick, advance the ordinary held/released idle input exactly once.
                if (physics.StepSequence == beforeStep && (movementFree || navigationActive))
                    physics.TickIdle();
            }

            if (controlsSelf && _sink is not null && _physics?.EngineState is { } engineState)
            {
                // SelfState is the integration boundary: explicit rotation and a lease owner's raw position updates intentionally do not mutate the physics engine. Collision and actual sprint remain engine-owned; the server-visible pose comes from tracked self state.
                engineState = engineState with
                {
                    Position = State.Self.Position,
                    Yaw = State.Self.Yaw,
                    Pitch = State.Self.Pitch,
                    OnGround = State.Self.OnGround,
                };
                await _movementReporter.ReportAsync(
                        _physics.LastRequestedInput,
                        engineState,
                        _settings.Options.AutoSendPosition,
                        SendMovementTickPacketAsync)
                    .ConfigureAwait(false);
            }

            // Vanilla's client ticks block entities AFTER ticking entities, and local-player tick is where sendPosition happens. So a piston push lands on the wire on the NEXT tick, and this call sits after the send to keep that order rather than to beat it.
            _physics?.TickPistons();

        }

        // The end-of-tick marker (1.21.2+). Vanilla's client sends it every tick and the server uses the ticks it does NOT arrive in to notice the player stopped moving: without it receivedMovementThisTick latches true after the first movement packet and the server's record of the player's velocity never returns to zero. Sent last in the tick, after any movement, so the frame order matches vanilla's.
        if (_sendsTickEnd && _sink is { } tickSink && State.Self.HasSpawned)
            await SendTickPacketAsync(
                () => tickSink.SendAsync(new ServerboundClientTickEndPacket(), CancellationToken.None),
                "client_tick_end").ConfigureAwait(false);

    }

    private async ValueTask<bool> SendMovementTickPacketAsync(object packet, string name)
    {
        bool sent = true;
        await SendTickPacketAsync(
                async () =>
                {
                    try
                    {
                        await (_sink ?? throw new InvalidOperationException("The client is not connected."))
                            .SendAsync(packet, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        sent = false;
                        throw;
                    }
                },
                name)
            .ConfigureAwait(false);
        return sent;
    }

    /// <summary>Runs one per-tick send and reports a failure instead of letting it disappear. The send is started through a delegate so a SYNCHRONOUS throw (an action the version cannot perform, or the sink vanishing mid-teardown) is caught by the same handler as an asynchronous one.</summary>
    /// <remarks>This deliberately does not end the session or the tick loop. A per-tick send fails because the connection is going away, and the receive loop owns that verdict: it records the real <see cref="DisconnectInfo"/> and publishes <see cref="Events.Disconnected"/>. Ending the tick loop here would race that with a worse-informed answer. What the tick owes is visibility, which is the log record, and it is the assertable difference from a discarded task.</remarks>
    private async ValueTask SendTickPacketAsync(Func<ValueTask> send, string packet)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsExpectedSendTeardown(ex))
        {
            // The ordinary end of every session: the connection went away between the receive loop noticing and this tick running. Reporting it as a failure, with the exception attached, ended a normal quit with a ConnectionClosedException stack trace that reads as a crash. It stays visible, because a dropped frame is still worth a record, but as a note: Debug level and no exception, so no console logger prints a trace for it.
            _logger.LogDebug("The per-tick {Packet} send was dropped: {Reason}", packet, DescribeTeardown(ex));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The per-tick {Packet} send failed.", packet);
        }
    }

    /// <summary>Whether a per-tick send failure is the connection going away rather than a fault worth an alarm.</summary>
    /// <remarks>Every <see cref="CloseReason"/> except <see cref="CloseReason.ProtocolViolation"/> describes a session that ended, including the ones a caller asked for (<see cref="CloseReason.Local"/>, <see cref="CloseReason.Cancelled"/>) and the ones the peer decided (<see cref="CloseReason.SocketEof"/>, <see cref="CloseReason.DisconnectMessage"/>, <see cref="CloseReason.Transferred"/>, <see cref="CloseReason.IdleTimeout"/>). A protocol violation is a real fault and keeps its warning and its stack trace. Cancellation and a disposed connection are the same teardown reached from the other side of the race.</remarks>
    internal static bool IsExpectedSendTeardown(Exception ex) => ex switch
    {
        ConnectionClosedException closed => closed.Reason != CloseReason.ProtocolViolation,
        OperationCanceledException => true,
        ObjectDisposedException => true,
        _ => false,
    };

    /// <summary>Names the teardown in one short phrase for the Debug record.</summary>
    private static string DescribeTeardown(Exception ex) => ex switch
    {
        ConnectionClosedException closed => closed.Reason.ToString(),
        OperationCanceledException => "Cancelled",
        ObjectDisposedException => "Disposed",
        _ => ex.GetType().Name,
    };

    private PluginHost CreateHost(IClientPlugin plugin) =>
        new(plugin.Id, this, _eventBus, Actions, _commands, _leases, _channels, _logger);

    private void OnPacketObserved(PacketObservation obs)
    {
        if (obs.Phase == ProtocolPhase.Play && obs.Flow == PacketFlow.Clientbound)
        {
            TryCapturePlayBrand(obs);
            _channels.DispatchInbound(obs.WireId, obs.RawPayload);
        }

        ForwardFrame(obs);
    }

    private void OnPacketDecodeFailed(PacketDecodeFailure failure)
    {
        Action<PacketDecodeFailure>? observers = PacketDecodeFailed;
        if (observers is null)
            return;

        foreach (Action<PacketDecodeFailure> observer in
                 observers.GetInvocationList().Cast<Action<PacketDecodeFailure>>())
        {
            try
            {
                observer(failure);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "A PacketDecodeFailed handler threw for wire id 0x{WireId:X2}.",
                    failure.WireId);
            }
        }
    }

    /// <summary>Hands one observed frame to the <see cref="PacketFrameObserved"/> subscribers. A throwing handler is logged and swallowed: this runs on the connection's read/write path and a consumer's bug must not tear the session down.</summary>
    private void ForwardFrame(PacketObservation obs)
    {
        Action<PacketObservation>? observers = _frameObservers;
        if (observers is null)
            return;

        try
        {
            observers(obs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A PacketFrameObserved handler threw for wire id 0x{WireId:X2}.", obs.WireId);
        }
    }

    private void AttachOutboundObserver(JavaConnection? connection)
    {
        if (connection is not null)
            connection.OutboundPacketObserved += OnPacketObserved;

    }

    private void DetachOutboundObserver(JavaConnection? connection)
    {
        if (connection is not null)
            connection.OutboundPacketObserved -= OnPacketObserved;

    }

    /// <summary>Captures the server brand from a play-phase custom payload (1.8-1.20.1; on 1.20.2+ the brand only ever arrives during configuration). Play <c>custom_payload</c> is a marker on every version, so there is no decoded packet to apply and the raw frame is the only place to read it. The legacy <c>MC|Brand</c> channel is not a namespaced identifier, so the plugin-channel table cannot carry it.</summary>
    private void TryCapturePlayBrand(PacketObservation obs)
    {
        if (State.Server.Brand is not null)
            return;

        int customPayload = _wire.ClientboundPlay(CustomPayloadChannelId);
        if (customPayload < 0 || obs.WireId != customPayload)
            return;

        if (!ServerBrandPayload.TryReadFromPlayFrame(obs.RawPayload, out string brand))
            return;

        // Observed on the read loop; hop to the session loop so state mutation stays single-threaded.
        _scheduler.Post(() => State.Server.Brand ??= brand);
    }

    private void RecordDisconnect(DisconnectInfo info) =>
        Interlocked.CompareExchange(ref _disconnect, info, comparand: null);

    /// <summary>Replaces <see cref="_spawned"/> with a fresh, incomplete task for the connection <see cref="ConnectAsync"/> is about to start. Called synchronously from <see cref="ConnectAsync"/>, after its status guard and before its first await; see <see cref="Spawned"/> for why that placement is the whole point.</summary>
    private void ArmSpawnedSignal() => _spawned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Transitions <see cref="Status"/> and raises <see cref="StatusChanged"/>. No-ops when <paramref name="next"/> equals the current status, so a caller never has to check first before asking for a status it may already be in. The terminal transition to <see cref="ClientStatus.Disconnected"/> carries whatever <see cref="_disconnect"/> holds at that point.</summary>
    private void SetStatus(ClientStatus next)
    {
        if (Status == next)
            return;

        ClientStatus previous = Status;
        Status = next;

        EventHandler<ClientStatusChangedEventArgs>? handler = StatusChanged;
        if (handler is null)
            return;

        var args = new ClientStatusChangedEventArgs(
            previous, next, next == ClientStatus.Disconnected ? _disconnect : null);

        try
        {
            handler(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A StatusChanged handler threw for the {Previous} -> {Current} transition.", previous, next);
        }
    }

    private async Task TeardownSessionAsync()
    {
        // Covers every teardown, including one that runs before ReceiveLoopAsync ever started (e.g. a caller that disconnects or disposes without having connected at all): that path's own finally completes the same task, and TrySetResult makes the second call here a no-op.
        _spawned.TrySetResult(false);

        CancellationTokenSource? cts = Interlocked.Exchange(ref _sessionCts, null);
        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        Plugins.EndSession();

        await SafeAwait(_receiveTask).ConfigureAwait(false);
        await SafeAwait(_tickTask).ConfigureAwait(false);

        if (_connection is not null)
        {
            _connection.PacketObserved -= OnPacketObserved;
            _connection.PacketDecodeFailed -= OnPacketDecodeFailed;
            DetachOutboundObserver(_connection);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        // A login-query claim belongs to the session it was made for: a responder typically closes over per-session state (a forwarding secret resolved for THIS endpoint, a handshake state machine), and answering the next login out of the last one's closure is the failure this forecloses. A caller that reconnects the same client re-registers; the supervised path builds a fresh client per attempt and never sees this.
        LoginQueries.Clear();

        // Channel HANDLERS survive (they belong to their registration handles, and a plugin's are already gone with Plugins.EndSession above); what resets is the announce gate and the set of channels the departing server had announced.
        _channels.OnSessionEnded();

        _sink = null;
        _chatSigning = null;
        _chatVerifiers = null;
        _chatSignatureCache = null;
        _receiveTask = null;
        _tickTask = null;
        _movementReporter.Reset();
        _movementReadiness.Reset();
        _navigator?.ResetSession();

        // A dialog belongs to the connection that showed it: drop it so a reconnect does not resume with a stale one still reported as open.
        if (State.ResetForSessionEnd())
            await _eventBus.PublishAsync(new Umpk.Client.Events.DialogCleared()).ConfigureAwait(false);

        SetStatus(ClientStatus.Disconnected);
    }

    private static async Task SafeAwait(Task? task)
    {
        if (task is null)
            return;

        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // teardown swallows loop faults; they are logged where they occur
        }
    }

    /// <summary>Adapts <see cref="PacketFrameObserved"/> into <see cref="PacketFrameHandler"/> for <see cref="ObservePackets"/>: converts each <see cref="PacketObservation"/> to a <see cref="PacketFrame"/> and forwards it. Idempotent disposal: disposing twice is a no-op, and a disposed subscription drops any observation still in flight from the moment it un-subscribed.</summary>
    private sealed class PacketFrameSubscription : IDisposable
    {
        private readonly UmpkClient _client;
        private readonly PacketFrameHandler _handler;
        private int _disposed;

        public PacketFrameSubscription(UmpkClient client, PacketFrameHandler handler)
        {
            _client = client;
            _handler = handler;
            _client.PacketFrameObserved += OnObserved;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _client.PacketFrameObserved -= OnObserved;
        }

        private void OnObserved(PacketObservation observation)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            // ForwardFrame (the caller of this delegate, through PacketFrameObserved) already isolates a throwing handler with its own try/catch, so this stays a thin, allocation-free adapter.
            PacketFrame frame = observation.AsFrame();
            _handler(in frame);
        }
    }
}
