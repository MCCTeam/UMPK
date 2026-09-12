using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client;
using Umpk.Client.Appliers;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Protocol.Java;

namespace Umpk.Client.Tests.Support;

/// <summary>Drives the internal applier chain against a real <see cref="ClientState"/> and event bus, without a live connection. Used by applier replay tests to feed decoded packets and assert Umpk.Game state and event publications.</summary>
internal sealed class ApplierHarness
{
    private readonly Dispatcher _dispatcher;
    private readonly ApplierContext _context;
    private readonly List<IPacketSink> _sent = [];
    private readonly JavaVersion _version;

    public ApplierHarness(
        JavaVersion version,
        ClientFeatures? features = null,
        TimeProvider? time = null,
        ClientPolicies? policies = null)
    {
        _version = version;
        Time = time ?? TimeProvider.System;
        Features = (features ?? new ClientFeatures()).Normalized();
        State = new ClientState(Features);
        Events = new EventBus(NullLogger.Instance, TimeSpan.Zero, 256);
        Recorder = new RecordingSink();
        Completions = new Client.Commands.CommandCompletionService(Recorder);

        // Use a real channel manager over the recording sink so the harness exercises the same configuration-phase routing seam as the client.
        Channels = new Client.Plugins.PluginChannelManager(
            Recorder, new WireIndex(version), version.Version.Protocol, NullLogger.Instance);
        _dispatcher = new Dispatcher(ApplierCatalog.Build(Features));
        _context = new ApplierContext
        {
            State = State,
            Events = Events,
            Sink = Recorder,
            Logger = NullLogger.Instance,
            Version = version,
            Features = Features,
            Policies = policies ?? new ClientPolicies(),
            Wire = new WireIndex(version),
            EntityTypes = new EntityTypeResolver(),
            MetadataKeys = Umpk.Data.Java.JavaGameData.EntityMetadataKeys(version.Version.Protocol),
            WorldSetup = SetupWorld,
            PhysicsConditionsDirty = () => ConditionsPushCount++,
            PhysicsPositionDirty = () =>
            {
                PositionResyncCount++;
                PositionResync?.Invoke();
            },
            PhysicsVelocityDirty = () =>
            {
                VelocityPushCount++;
                VelocityPush?.Invoke();
            },
            CommandCompletions = Completions,
            Sequences = Sequences,
            Time = Time,
            RecordDisconnect = info => Disconnect ??= info,
            Cookies = Cookies,
            DispatchConfigurationChannel = Channels.DispatchConfiguration,
            // Built through the same factory UmpkClient uses, so a test cannot pass against a resolver the live client does not actually install (which is precisely how the inert ChatVerifierResolver survived: nothing constructed one on either side).
            ChatVerifierResolver = Internal.PeerChatVerifiers.CreateResolver(
                State, version.Features.ChatSigning, Time, NullLogger.Instance),
            LastSeenTracker = LastSeenTracker,
            LegacyLastSeenCollector = LegacyLastSeenCollector,
            SignatureCache = SignatureCache,
            EnterConfiguration = token =>
            {
                ConfigurationEntries++;
                return EnterConfiguration?.Invoke(token) ?? ValueTask.CompletedTask;
            },
        };
    }

    /// <summary>How many times the applier chain asked to enter the configuration phase. The live client hands this seam the connection hop plus the configuration driver, so counting it is what separates "an acknowledgement was sent" from "the client actually followed through into the phase".</summary>
    public int ConfigurationEntries { get; private set; }

    /// <summary>Optional stand-in for the live configuration driver, so a test can script what happens inside the phase without a connection.</summary>
    public Func<CancellationToken, ValueTask>? EnterConfiguration { get; set; }

    /// <summary>The first disconnect reason an applier recorded, or null. First-wins mirrors the live client, where the receive loop's transport close must not overwrite a reason the applier already saw.</summary>
    public DisconnectInfo? Disconnect { get; private set; }

    /// <summary>The cookie bag the applier chain answers cookie requests from.</summary>
    public CookieStore Cookies { get; } = new();

    /// <summary>What <c>UmpkClient</c>'s receive loop does when the socket goes away: record a reason only if none was recorded yet. Tests use it to reproduce the race between the disconnect frame and the close that follows it.</summary>
    public void RecordTransportClose(DisconnectInfo info) => Disconnect ??= info;

    /// <summary>The 1.19.3+ inbound last-seen tracker the chat applier records signatures into. The harness owns one unconditionally so a test can assert the collection and the standalone-ack overflow.</summary>
    public Protocol.Java.Signing.LastSeenMessagesTracker LastSeenTracker { get; } = new();

    /// <summary>The 1.19.1/1.19.2 inbound last-seen collector the chat applier records (sender, signature) pairs into, so a test can assert the v2 window the outbound signed body has to fold in.</summary>
    public Protocol.Java.Signing.LastSeenMessagesCollector LegacyLastSeenCollector { get; } =
        new(Protocol.Java.Signing.LastSeenMessagesCollector.Window1_19);

    /// <summary>The shared per-connection signature cache the chat applier resolves v3 last-seen cache-id references through (see <c>SignedChatVerification.Verify</c>). The harness owns one unconditionally, same as <see cref="LastSeenTracker"/>, so a test can drive a multi-message sequence and see cache-id entries resolve exactly as the live client would.</summary>
    public Protocol.Java.Signing.MessageSignatureCache SignatureCache { get; } = new();

    /// <summary>The clock the appliers measure with; tests pass a controllable provider.</summary>
    public TimeProvider Time { get; }

    /// <summary>The block-action sequence tracker used by the block-change-ack applier.</summary>
    public SequenceTracker Sequences { get; } = new();

    /// <summary>The very same completion service the applier chain resolves a <c>command_suggestions</c> response into, so a test can start a real pending request and then prove the decoded packet REACHES it, rather than only that it decoded.</summary>
    public Client.Commands.CommandCompletionService Completions { get; }

    public ClientState State { get; }

    public EventBus Events { get; }

    public ClientFeatures Features { get; }

    public RecordingSink Recorder { get; }

    /// <summary>The channel manager the applier chain routes configuration-phase custom payloads through. Exposed so a test can register a handler and assert what the applier actually delivered.</summary>
    public Client.Plugins.PluginChannelManager Channels { get; }

    public int ConditionsPushCount { get; private set; }

    /// <summary>How many times the applier chain asked for a physics position re-seed.</summary>
    public int PositionResyncCount { get; private set; }

    /// <summary>Hook the physics position re-seed. Tests that own a real <c>PhysicsEngineHolder</c> point this at its <c>ResyncPosition</c>, so the harness drives the same seam <c>UmpkClient</c> wires live.</summary>
    public Action? PositionResync { get; set; }

    /// <summary>How many times the applier chain pushed a server-applied velocity at the physics engine.</summary>
    public int VelocityPushCount { get; private set; }

    /// <summary>Hook the physics velocity push. Tests that own a real <c>PhysicsEngineHolder</c> point this at its <c>ApplyVelocity</c>, so the harness drives the same seam <c>UmpkClient</c> wires live. Counting it separately from <see cref="PositionResync"/> distinguishes "the applier wrote a field" from "the applier told the engine".</summary>
    public Action? VelocityPush { get; set; }

    public ValueTask ApplyAsync(object packet) => _dispatcher.DispatchAsync(packet, _context, CancellationToken.None);

    /// <summary>Dispatches like <see cref="ApplyAsync"/> but reports whether any applier OWNED the packet. A packet that falls through the whole chain is unhandled, and that is indistinguishable from a deliberately-dropped one when a test asserts state alone.</summary>
    public async ValueTask<bool> TryApplyAsync(object packet)
    {
        foreach (IApplier applier in ApplierCatalog.Build(Features))
            if (await applier.TryApplyAsync(packet, _context, CancellationToken.None).ConfigureAwait(false))
                return true;

        return false;
    }

    private void SetupWorld(CommonWorldSetup setup)
    {
        if (!Features.Terrain)
            return;

        // Same call UmpkClient.SetupWorld makes, with the same three inputs. A harness that resolved the dimension its own way would let a registry-driven resolution pass here and never run live.
        Game.World.DimensionState dimension = WorldFactory.CreateDimension(
            setup, State.Registries, _version.Version.Protocol);
        Game.Registries.Registry<Game.Registries.BlockDefinition> blocks =
            State.Registries?.Blocks ?? WorldFactory.EmptyBlocks();
        var blockData = new RegistryBlockDataSource(blocks, _version.Version.Protocol < 393);
        var world = new Game.World.World(dimension, blockData, WorldFactory.EmptyBiomes());
        State.InstallWorld(world);
    }
}
