using System.Buffers;
using System.Collections.Immutable;
using System.Threading.Channels;
using Umpk.Client.Bedrock.Actions;
using Umpk.Client.Bedrock.Authentication;
using Umpk.Client.Bedrock.Bootstrap;
using Umpk.Client.Bedrock.Internal;
using Umpk.Client.Bedrock.Internal.Appliers.Actors;
using Umpk.Client.Bedrock.Policies;
using Umpk.Client.Bedrock.State.Actors;
using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Client.Bedrock.State.World;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Umpk.Transport;

namespace Umpk.Client.Bedrock.Session;

internal sealed class BedrockClientSession : IAsyncDisposable
{
    // Session gate orders before inventory, window, tracker, and coordinator gates and is never held across await.
    private readonly Lock _gate = new();
    private readonly BedrockClientSettings _settings;
    private readonly IMessageConnection _transport;
    private readonly BedrockConnection _connection;
    private readonly BedrockConnectionKey _key;
    private readonly BedrockSessionLifetime _lifetime = new();
    private readonly Task _runTask;
    private readonly List<int> _postJoinPacketIds = [];
    private readonly List<BedrockWireEvent> _transcript = [];
    private readonly BedrockActorStore _actors;
    private readonly BedrockInventoryStore _inventory = new();
    private readonly BedrockContainerWindows _containerWindows = new();
    private readonly BedrockItemStackRequestTracker _itemStackTracker = new();
    private readonly BedrockItemStackRequestIdAllocator _itemStackRequestIds = new();
    private readonly Dictionary<int, BedrockItemStackReconciliation> _itemStackReconciliations = new();
    private readonly Dictionary<int, CancellationTokenSource> _itemStackAdmissions = new();
    private readonly Dictionary<int, BedrockCreativeCursorReconciliation> _creativeCursorReconciliations = new();
    private readonly Dictionary<int, BedrockCursorPlaceReconciliation> _cursorPlaceReconciliations = new();
    private bool _cursorPlacePathActivated;
    private bool _cursorPlaceFreshAuthorityRequired;
    private CursorPlaceSourceQualification? _cursorPlaceSourceQualification;
    private object? _cursorPlaceProtectedReceipt;
    private sealed record CursorPlaceSourceQualification(BedrockCreativeCursorOwnership Ownership, object Receipt);

    private static readonly BedrockContainerAddress s_creativeCursorPool = new(124, 0, null);
    private BedrockItemRegistryPacket? _creativeInitialItems;
    private BedrockRegistrySnapshot? _creativeRegistryOwner;
    private bool _creativeRegistryPublicationPending;
    private bool _creativeRegistryRetired;
    private BedrockOpenContainer? _creativeScreen;
    private object? _creativeScreenLifetime;
    private bool _creativeScreenObserved;
    private bool _creativeCursorBaselineQualified;
    private CancellationTokenSource? _normalInventoryOpenAdmission;
    private int _activeInventorySends;
    private TaskCompletionSource? _inventorySendsDrained;
    private readonly BedrockActionCoordinator _actionCoordinator = new();
    private readonly Dictionary<int, HashSet<BedrockContainerAddress>> _inventoryWindows = new();
    private int _unresolvedInventorySlots;
    private int _duplicateContainerCloses;
    private BedrockPlayerHotbarPacket? _lastHotbar;
    private int _hotbarObservations;
    private BedrockCreativeContentPacket? _lastCreativeContent;
    private int _creativeContentObservations;
    private BedrockInventoryTransactionPacket? _lastInventoryTransaction;
    private int _inventoryTransactionObservations;
    private bool _inventoryTornDown;
    // Recovery after respawn, world, or pending-drop close boundaries remains unqualified, so inventory egress retires until reconnect.
    private bool _itemStackEgressRetired;
    private const int MaximumInventoryWindows = 64;
    private const int MaximumAddressesPerInventoryWindow = 8;
    private const int MaximumTranscriptEvents = 4096;
    private const int MaximumPostJoinPacketIds = 4096;
    private const int MaximumCapturedLevelChunks = 3;
    private const int MaximumCapturedPayloadBytes = 1024 * 1024;
    private const int MaximumTerrainQueue = 128;
    private const int MaximumRequestedTerrainColumns = 4;
    private readonly bool _terrainCaptureEnabled;
    private readonly bool _inventoryCaptureEnabled;
    private readonly bool _motionCaptureEnabled;
    private readonly bool _creativeCaptureEnabled;
    private readonly bool _actorCaptureEnabled;
    private readonly bool _respawnCaptureEnabled;
    private readonly List<BedrockMovePlayerPacket> _capturedMovePlayers = [];
    private readonly List<byte[]> _capturedMovePlayerPayloads = [];
    private readonly List<BedrockSetEntityMotionPacket> _capturedSetEntityMotions = [];
    private readonly List<byte[]> _capturedSetEntityMotionPayloads = [];
    private int _movePlayerObservations;
    private int _setEntityMotionObservations;
    private readonly List<BedrockCorrectPlayerMovePredictionPacket> _capturedMovePredictions = [];
    private readonly List<byte[]> _capturedMovePredictionPayloads = [];
    private readonly List<byte[]> _capturedAddItemEntityPayloads = [];
    private readonly List<byte[]> _capturedRespawnPayloads = [];
    private int _movePredictionObservations;
    private bool _hasSentPlayerAuthInput;
    private ulong _lastPlayerAuthInputTick;
    private int _playerAuthInputSends;
    // The egress semaphore is never disposed because late callers may still wait on it after teardown.
    private readonly SemaphoreSlim _playerAuthInputEgress = new(1, 1);
    private readonly CancellationTokenSource _playerAuthInputAdmissionLifetime = new();
    private readonly List<BedrockInventoryContentPacket> _capturedInventoryContents = [];
    private readonly List<BedrockInventorySlotPacket> _capturedInventorySlots = [];
    private readonly List<byte[]> _capturedInventoryContentPayloads = [];
    private readonly List<byte[]> _capturedInventorySlotPayloads = [];
    private readonly List<byte[]> _capturedNormalInventoryOpenPayloads = [];
    private readonly List<BedrockContainerOpenPacket> _capturedContainerOpens = [];
    private readonly List<BedrockContainerClosePacket> _capturedContainerCloses = [];
    private readonly List<BedrockPlayerHotbarPacket> _capturedHotbars = [];
    private readonly List<byte[]> _capturedHotbarPayloads = [];
    private readonly List<BedrockInventoryTransactionPacket> _capturedInventoryTransactions = [];
    private readonly List<byte[]> _capturedInventoryTransactionPayloads = [];
    private readonly List<byte[]> _capturedItemStackRequestPayloads = [];
    private readonly List<byte[]> _capturedItemStackResponsePayloads = [];
    private readonly List<byte[]> _capturedCreativeContentPayloads = [];
    private readonly List<BedrockPacketViolationWarningPacket> _capturedViolations = [];
    private byte[]? _capturedStartGameRemainder;
    private readonly List<LevelChunkPacket> _capturedLevelChunks = [];
    private readonly List<(int X, int Z, int Dimension, uint SubChunkCount)> _capturedLevelChunkEnvelopes = [];
    private const int MaximumCapturedLevelChunkEnvelopes = 130;
    private byte[]? _capturedUpdateBlockPayload;
    private readonly List<byte[]> _capturedPostJoinUpdateBlocks = [];
    private const int MaximumCapturedPostJoinUpdateBlocks = 32;
    private readonly List<byte[]> _capturedSubChunkResponses = [];
    private const int MaximumCapturedSubChunkResponses = 8;
    private readonly List<byte[]> _capturedSubChunkBlocks = [];
    private const int MaximumCapturedSubChunkBlocks = 32;
    private const int MaximumCapturedInventoryPackets = 32;
    private const int MaximumCapturedInventoryPayloadBytes = 256 * 1024;
    private const int MaximumCapturedCreativePackets = 4;
    private const int MaximumCapturedCreativePayloadBytes = 256 * 1024;
    private const int MaximumCapturedMotionPackets = 32;
    private const int MaximumCapturedMotionPayloadBytes = 256 * 1024;
    private const int MaximumCapturedActorPackets = 32;
    private const int MaximumCapturedActorPayloadBytes = 256 * 1024;
    private const int MaximumCapturedRespawnPackets = 8;
    private const int MaximumCapturedRespawnPayloadBytes = 64;
    private const int MaximumPendingPreReadyRespawns = 8;
    private readonly Queue<BedrockRespawnPacket> _pendingPreReadyRespawns = new();
    private readonly Dictionary<string, int> _subChunkResultCounts = [];
    private readonly BedrockWorldStore _world = new();
    private readonly BedrockSubChunkCorrelator _subChunks = new();
    private readonly Channel<TerrainWorkItem> _terrainWork = Channel.CreateBounded<TerrainWorkItem>(
        new BoundedChannelOptions(MaximumTerrainQueue) { SingleReader = true, SingleWriter = true });
    private readonly Task _terrainWorker;
    private int? _terrainDimension;
    private bool? _terrainHashes;
    private ulong? _terrainChecksum;
    private long _terrainRequestGeneration = 1;
    private readonly HashSet<(int Dimension, int X, int Z)> _requestedTerrainColumns = [];
    private readonly HashSet<(int Dimension, int X, int Z)> _pendingTerrainColumns = [];
    private bool _readyFlushQueued;
    // Initial headless completion uses the existing bootstrap predicates; it does not reproduce the native UI lifetime.
    private bool _initialLoadingStarted;
    private bool _initialLoadingEndTransferred;
    private bool _initialLoadingComplete;
    private bool _initialLoadingRetired;
    private int _initialLoadingEpoch;
    private BedrockSessionScope _initialLoadingRetirementScope;
    private CancellationTokenSource? _initialLoadingAdmission;
    private int _terrainWorkDropped;
    private int _terrainStaleDropped;
    private int _terrainApplyRefused;
    private int _terrainApplied;
    private int _terrainRequestsSent;
    private BedrockBootstrapState _bootstrap = BedrockBootstrapState.Initial;
    private BedrockSessionSnapshot _snapshot = new(
        false, false, false, false, false, false, false, false,
        false, false, false, false, false, false, 0L, 0UL, 0, 0, 1, false, null);
    private DateTimeOffset _deadline;
    private string _phase = "network-settings";
    private bool _cacheStatusSent;
    private long _inboundSequence;
    private long _initializationSendSequence = -1;
    private int _rejectedSendAttempts;
    private bool _disposed;

    internal BedrockClientSession(
        BedrockClientSettings settings,
        IMessageConnection transport,
        BedrockConnection connection,
        BedrockConnectionKey key)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _deadline = settings.TimeProvider.GetUtcNow() + settings.Timeouts.NetworkSettings;
        _actors = new BedrockActorStore(settings.TimeProvider);
        _terrainCaptureEnabled = string.Equals(Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_TERRAIN"), "1", StringComparison.Ordinal);
        _inventoryCaptureEnabled = string.Equals(Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_INVENTORY"), "1", StringComparison.Ordinal);
        _motionCaptureEnabled = string.Equals(Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_MOTION"), "1", StringComparison.Ordinal);
        _creativeCaptureEnabled = string.Equals(Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_CREATIVE"), "1", StringComparison.Ordinal);
        _actorCaptureEnabled = string.Equals(Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_ACTORS"), "1", StringComparison.Ordinal);
        _respawnCaptureEnabled = string.Equals(Environment.GetEnvironmentVariable("UMPK_BEDROCK_CAPTURE_RESPAWN"), "1", StringComparison.Ordinal);
        _terrainWorker = ProcessTerrainQueueAsync();
        _runTask = RunAsync();
    }

    internal byte[]? CapturedStartGameRemainder
    {
        get
        {
            lock (_gate)
                return _capturedStartGameRemainder is null ? null : (byte[])_capturedStartGameRemainder.Clone();
        }
    }

    internal IReadOnlyList<LevelChunkPacket> CapturedLevelChunks
    {
        get
        {
            lock (_gate)
                return [.. _capturedLevelChunks];
        }
    }

    internal IReadOnlyList<(int X, int Z, int Dimension, uint SubChunkCount)> CapturedLevelChunkEnvelopes
    {
        get
        {
            lock (_gate)
                return [.. _capturedLevelChunkEnvelopes];
        }
    }

    internal byte[]? CapturedUpdateBlockPayload
    {
        get
        {
            lock (_gate)
                return _capturedUpdateBlockPayload is null ? null : (byte[])_capturedUpdateBlockPayload.Clone();
        }
    }

    internal IReadOnlyList<byte[]> CapturedPostJoinUpdateBlocks
    {
        get
        {
            lock (_gate)
                return _capturedPostJoinUpdateBlocks.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<byte[]> CapturedSubChunkResponses
    {
        get
        {
            lock (_gate)
                return _capturedSubChunkResponses.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<byte[]> CapturedSubChunkBlocks
    {
        get
        {
            lock (_gate)
                return _capturedSubChunkBlocks.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal void ResetSubChunkBlocksCapture()
    {
        lock (_gate)
            _capturedSubChunkBlocks.Clear();
    }

    internal IReadOnlyDictionary<string, int> SubChunkResultCounts
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, int>(_subChunkResultCounts);
        }
    }

    internal IReadOnlyList<BedrockInventoryContentPacket> CapturedInventoryContents
    {
        get
        {
            lock (_gate)
                return [.. _capturedInventoryContents];
        }
    }

    internal IReadOnlyList<BedrockInventorySlotPacket> CapturedInventorySlots
    {
        get
        {
            lock (_gate)
                return [.. _capturedInventorySlots];
        }
    }

    internal IReadOnlyList<byte[]> CapturedInventoryContentPayloads
    {
        get
        {
            lock (_gate)
                return _capturedInventoryContentPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<byte[]> CapturedInventorySlotPayloads
    {
        get
        {
            lock (_gate)
                return _capturedInventorySlotPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<byte[]> CapturedCreativeContentPayloads
    {
        get
        {
            lock (_gate)
                return _capturedCreativeContentPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    /// <summary>Gets bounded defensive copies of actual successful normal inventory opening payloads when inventory capture is enabled.</summary>
    internal IReadOnlyList<byte[]> CapturedNormalInventoryOpenPayloads
    {
        get
        {
            lock (_gate)
                return _capturedNormalInventoryOpenPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<BedrockContainerOpenPacket> CapturedContainerOpens
    {
        get
        {
            lock (_gate)
                return [.. _capturedContainerOpens];
        }
    }

    internal IReadOnlyList<BedrockContainerClosePacket> CapturedContainerCloses
    {
        get
        {
            lock (_gate)
                return [.. _capturedContainerCloses];
        }
    }

    internal IReadOnlyList<BedrockPlayerHotbarPacket> CapturedHotbars
    {
        get
        {
            lock (_gate)
                return [.. _capturedHotbars];
        }
    }

    internal IReadOnlyList<byte[]> CapturedHotbarPayloads
    {
        get
        {
            lock (_gate)
                return _capturedHotbarPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<BedrockInventoryTransactionPacket> CapturedInventoryTransactions
    {
        get
        {
            lock (_gate)
                return [.. _capturedInventoryTransactions];
        }
    }

    internal IReadOnlyList<byte[]> CapturedInventoryTransactionPayloads
    {
        get
        {
            lock (_gate)
                return _capturedInventoryTransactionPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<byte[]> CapturedItemStackRequestPayloads
    {
        get
        {
            lock (_gate)
                return _capturedItemStackRequestPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<byte[]> CapturedItemStackResponsePayloads
    {
        get
        {
            lock (_gate)
                return _capturedItemStackResponsePayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<BedrockPacketViolationWarningPacket> CapturedViolations
    {
        get
        {
            lock (_gate)
                return [.. _capturedViolations];
        }
    }

    internal void ResetInventoryCapture()
    {
        lock (_gate)
        {
            _capturedInventoryContents.Clear();
            _capturedInventorySlots.Clear();
            _capturedInventoryContentPayloads.Clear();
            _capturedInventorySlotPayloads.Clear();
            _capturedContainerOpens.Clear();
            _capturedContainerCloses.Clear();
            _capturedHotbars.Clear();
            _capturedHotbarPayloads.Clear();
            _capturedInventoryTransactions.Clear();
            _capturedInventoryTransactionPayloads.Clear();
            _capturedItemStackRequestPayloads.Clear();
            _capturedItemStackResponsePayloads.Clear();
            _capturedCreativeContentPayloads.Clear();
            _capturedViolations.Clear();
        }
    }

    internal BedrockWorldSnapshot TerrainSnapshot => _world.CaptureSnapshot();

    internal BedrockBlockQuery GetTerrainBlock(int x, int y, int z)
    {
        int dimension;
        lock (_gate)
            dimension = _terrainDimension ?? -1;
        return dimension < 0
            ? new BedrockBlockQuery(BedrockCellAvailability.Absent, null)
            : _world.GetBlock(x, y, z, dimension);
    }

    internal BedrockColumnAvailability GetTerrainColumn(int chunkX, int chunkZ)
    {
        int dimension;
        lock (_gate)
            dimension = _terrainDimension ?? -1;
        return dimension < 0
            ? BedrockColumnAvailability.Absent
            : _world.GetColumnAvailability(chunkX, chunkZ, dimension);
    }

    internal int? TerrainDimension
    {
        get
        {
            lock (_gate)
                return _terrainDimension;
        }
    }

    internal bool? TerrainHashes
    {
        get
        {
            lock (_gate)
                return _terrainHashes;
        }
    }

    internal ulong? TerrainChecksum
    {
        get
        {
            lock (_gate)
                return _terrainChecksum;
        }
    }

    internal int TerrainWorldGeneration => _world.WorldGeneration;

    internal int TerrainWorkDropped
    {
        get
        {
            lock (_gate)
                return _terrainWorkDropped;
        }
    }

    internal int TerrainStaleDropped
    {
        get
        {
            lock (_gate)
                return _terrainStaleDropped;
        }
    }

    internal int TerrainApplyRefused
    {
        get
        {
            lock (_gate)
                return _terrainApplyRefused;
        }
    }

    internal int TerrainApplied
    {
        get
        {
            lock (_gate)
                return _terrainApplied;
        }
    }

    internal int TerrainRequestsSent
    {
        get
        {
            lock (_gate)
                return _terrainRequestsSent;
        }
    }

    internal IReadOnlyList<(int Dimension, int X, int Z)> RequestedTerrainColumns
    {
        get
        {
            lock (_gate)
                return [.. _requestedTerrainColumns];
        }
    }

    internal BedrockSessionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return _snapshot;
        }
    }

    internal Task<BedrockTermination> Completion => _lifetime.Termination;

    internal IReadOnlyList<int> PostJoinPacketIds
    {
        get
        {
            lock (_gate)
                return [.. _postJoinPacketIds];
        }
    }

    internal IReadOnlyList<BedrockWireEvent> WireTranscript
    {
        get
        {
            lock (_gate)
                return [.. _transcript];
        }
    }

    internal int RejectedSendAttempts
    {
        get
        {
            lock (_gate)
                return _rejectedSendAttempts;
        }
    }

    internal IReadOnlyList<BedrockActorSnapshot> ActorSnapshots => _actors.Snapshots;

    internal IReadOnlyList<BedrockActorEvent> ActorEvents => _actors.Events;

    internal IReadOnlyList<BedrockActorWireWitness> ActorWireWitnesses => _actors.WireWitnesses;

    internal int UnknownActorUpdates => _actors.UnknownTargetUpdates;

    internal int StaleActorRemovals => _actors.StaleRemovals;

    internal BedrockInventoryStore InventoryStore => _inventory;

    internal BedrockContainerWindows ContainerWindows => _containerWindows;

    internal int DuplicateContainerCloses
    {
        get
        {
            lock (_gate)
                return _duplicateContainerCloses;
        }
    }

    internal int HotbarObservationCount
    {
        get
        {
            lock (_gate)
                return _hotbarObservations;
        }
    }

    internal BedrockPlayerHotbarPacket? LastHotbar
    {
        get
        {
            lock (_gate)
                return _lastHotbar;
        }
    }

    internal int CreativeContentObservationCount
    {
        get
        {
            lock (_gate)
                return _creativeContentObservations;
        }
    }

    internal BedrockCreativeContentPacket? LastCreativeContent
    {
        get
        {
            lock (_gate)
                return _lastCreativeContent;
        }
    }

    internal int InventoryTransactionObservationCount
    {
        get
        {
            lock (_gate)
                return _inventoryTransactionObservations;
        }
    }

    internal BedrockInventoryTransactionPacket? LastInventoryTransaction
    {
        get
        {
            lock (_gate)
                return _lastInventoryTransaction;
        }
    }

    internal IReadOnlyList<BedrockMovePlayerPacket> CapturedMovePlayers
    {
        get
        {
            lock (_gate)
                return [.. _capturedMovePlayers];
        }
    }

    internal IReadOnlyList<byte[]> CapturedMovePlayerPayloads
    {
        get
        {
            lock (_gate)
                return _capturedMovePlayerPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<BedrockSetEntityMotionPacket> CapturedSetEntityMotions
    {
        get
        {
            lock (_gate)
                return [.. _capturedSetEntityMotions];
        }
    }

    internal IReadOnlyList<byte[]> CapturedSetEntityMotionPayloads
    {
        get
        {
            lock (_gate)
                return _capturedSetEntityMotionPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal int MovePlayerObservationCount
    {
        get
        {
            lock (_gate)
                return _movePlayerObservations;
        }
    }

    internal int SetEntityMotionObservationCount
    {
        get
        {
            lock (_gate)
                return _setEntityMotionObservations;
        }
    }

    internal int MovePredictionObservationCount
    {
        get
        {
            lock (_gate)
                return _movePredictionObservations;
        }
    }

    internal IReadOnlyList<BedrockCorrectPlayerMovePredictionPacket> CapturedMovePredictions
    {
        get
        {
            lock (_gate)
                return [.. _capturedMovePredictions];
        }
    }

    internal IReadOnlyList<byte[]> CapturedMovePredictionPayloads
    {
        get
        {
            lock (_gate)
                return _capturedMovePredictionPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal IReadOnlyList<byte[]> CapturedAddItemEntityPayloads
    {
        get
        {
            lock (_gate)
                return _capturedAddItemEntityPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal void ResetAddItemEntityCapture()
    {
        lock (_gate)
            _capturedAddItemEntityPayloads.Clear();
    }

    internal IReadOnlyList<byte[]> CapturedRespawnPayloads
    {
        get
        {
            lock (_gate)
                return _capturedRespawnPayloads.Select(static payload => (byte[])payload.Clone()).ToArray();
        }
    }

    internal void ResetRespawnCapture()
    {
        lock (_gate)
            _capturedRespawnPayloads.Clear();
    }

    /// <summary>Gets the number of player-auth-input packets this session has sent.</summary>
    internal int PlayerAuthInputSendCount
    {
        get
        {
            lock (_gate)
                return _playerAuthInputSends;
        }
    }

    /// <summary>Clears retained movement and correction evidence and resets their observation counters.</summary>
    /// <remarks>Clearing retained evidence never touches actor state or the simulation.</remarks>
    internal void ResetMotionCapture()
    {
        lock (_gate)
        {
            _capturedMovePlayers.Clear();
            _capturedMovePlayerPayloads.Clear();
            _capturedSetEntityMotions.Clear();
            _capturedSetEntityMotionPayloads.Clear();
            _movePlayerObservations = 0;
            _setEntityMotionObservations = 0;
            _capturedMovePredictions.Clear();
            _capturedMovePredictionPayloads.Clear();
            _movePredictionObservations = 0;
        }
    }

    internal int InventoryWindowCount
    {
        get
        {
            lock (_gate)
                return _inventoryWindows.Count;
        }
    }

    internal BedrockItemStackRequestTracker ItemStackTracker => _itemStackTracker;

    /// <summary>Reads immutable published item names and entity identities; this is decoded registry state, not retained raw registry traffic.</summary>
    internal BedrockRegistrySnapshot PublishedRegistries
    {
        get
        {
            lock (_gate)
                return _connection.Context.Registries;
        }
    }

    internal BedrockActionCoordinator ActionCoordinator => _actionCoordinator;

    /// <summary>Gets the bounded retained operation count for session ownership diagnostics.</summary>
    internal int PendingItemStackReconciliationCount
    {
        get
        {
            lock (_gate)
                return _itemStackReconciliations.Count + _creativeCursorReconciliations.Count + _cursorPlaceReconciliations.Count;
        }
    }

    internal int UnresolvedInventorySlots
    {
        get
        {
            lock (_gate)
                return _unresolvedInventorySlots;
        }
    }

    internal IReadOnlyList<BedrockContainerAddress> GetInventoryWindowAddresses(int windowId)
    {
        lock (_gate)
            return _inventoryWindows.TryGetValue(windowId, out HashSet<BedrockContainerAddress>? addresses) ? [.. addresses] : [];
    }

    /// <summary>Validates the physical player endpoints and retained data shape before any request identity or transport ownership is allocated.</summary>
    // Per-instance test barrier; normal production sessions leave it unset.
    internal Action? BeforeOrdinaryMainInventoryRetention { get; set; }

    private OrdinaryNormalInventoryOwner? _ordinaryNormalInventoryOwner;
    private OrdinaryMainInventoryOperation? _ordinaryMainInventoryOperation;
    private bool _ordinaryRegistryRetired;
    private static readonly TimeSpan s_ordinaryInventoryOpeningTimeout = TimeSpan.FromSeconds(15);

    private sealed record OrdinaryInventoryContext(BedrockRegistrySnapshot Registry, int InventoryGeneration, int TrackerGeneration, int WindowGeneration, int WorldGeneration, int ActorGeneration, long InitialEpoch, ulong SelfRuntimeId);
    private sealed record OrdinaryNormalInventoryOwner(BedrockOpenContainer Window, object ScreenLifetime, OrdinaryInventoryContext Context);
    private enum OrdinaryInventoryRetirement { None, Caller, Deadline, Boundary }
    private sealed class OrdinaryMainInventoryOperation(OrdinaryInventoryContext context, CancellationToken caller)
    {
        internal OrdinaryInventoryContext Context { get; } = context;
        internal CancellationToken Caller { get; } = caller;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal TaskCompletionSource WindowReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Eligible { get; set; } = true;
        internal bool Opening { get; set; } = true;
        internal bool OpenInterruptedByNormalWindow { get; set; }
        internal OrdinaryInventoryRetirement Retirement { get; set; }
        internal OrdinaryNormalInventoryOwner? BoundOwner { get; set; }
        internal int? RequestId { get; set; }
    }
    private sealed class NormalInventoryOpenInterruptedException(Exception inner) : Exception("A real current normal window retired the ungranted owned opener.", inner);

    // These stamps are sampled only under the Session gate. A window getter cannot mint normal ownership.
    private OrdinaryInventoryContext CurrentOrdinaryInventoryContext() => new(PublishedRegistries, _inventory.Generation, _itemStackTracker.Generation, _containerWindows.Generation, _world.WorldGeneration, _actors.WorldGeneration, _initialLoadingEpoch, _snapshot.LocalRuntimeEntityId);
    private bool IsOrdinaryInventoryContextCurrent(OrdinaryInventoryContext context) => !_disposed && !_inventoryTornDown && !_itemStackEgressRetired
        && !_ordinaryRegistryRetired && !_initialLoadingRetired && !_lifetime.Termination.IsCompleted && !_lifetime.Token.IsCancellationRequested
        && _snapshot.IsReady && _initialLoadingComplete && _initialLoadingStarted && _initialLoadingEndTransferred
        && context.SelfRuntimeId != 0 && context.Registry.Generation > 0
        && ReferenceEquals(context.Registry, PublishedRegistries) && context.InventoryGeneration == _inventory.Generation
        && context.TrackerGeneration == _itemStackTracker.Generation && context.InventoryGeneration == context.TrackerGeneration
        && context.WindowGeneration == _containerWindows.Generation && context.WorldGeneration == _world.WorldGeneration
        && context.ActorGeneration == _actors.WorldGeneration && context.InitialEpoch == _initialLoadingEpoch && context.SelfRuntimeId == _snapshot.LocalRuntimeEntityId;

    private OrdinaryNormalInventoryOwner? GetCurrentOrdinaryNormalOwner() => _ordinaryNormalInventoryOwner is { } owner
        && IsOrdinaryInventoryContextCurrent(owner.Context) && ReferenceEquals(owner.ScreenLifetime, _creativeScreenLifetime)
        && _containerWindows.GetWindow(owner.Window.WindowId) == owner.Window ? owner : null;

    private bool IsOrdinaryMainInventoryOwnerCurrent(OrdinaryMainInventoryOperation operation) => ReferenceEquals(_ordinaryMainInventoryOperation, operation)
        && operation.Eligible && IsOrdinaryInventoryContextCurrent(operation.Context)
        && operation.BoundOwner is { } bound && ReferenceEquals(bound, GetCurrentOrdinaryNormalOwner());

    private void RequireNoOrdinaryMainInventoryOperation(OrdinaryMainInventoryOperation? operation = null)
    {
        if (_ordinaryMainInventoryOperation is { } active && !ReferenceEquals(active, operation))
            throw new InvalidOperationException("An ordinary main inventory workflow already owns inventory submission.");
    }
    private void RequireOrdinaryMainInventoryOwner(OrdinaryMainInventoryOperation? operation)
    {
        RequireNoOrdinaryMainInventoryOperation(operation);
        if (operation is not null && !IsOrdinaryMainInventoryOwnerCurrent(operation))
            ThrowOrdinaryMainInventoryRetirement(operation);
    }
    private void ThrowOrdinaryMainInventoryRetirement(OrdinaryMainInventoryOperation operation, Exception? cause = null)
    {
        if (_disposed || _lifetime.Token.IsCancellationRequested || _lifetime.Termination.IsCompleted || (_initialLoadingRetired && _initialLoadingRetirementScope == BedrockSessionScope.Session))
            throw new BedrockSessionException(_snapshot.Termination ?? new(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, cause));
        if (operation.Retirement == OrdinaryInventoryRetirement.Deadline)
            throw new TimeoutException("The current normal inventory window was not confirmed within the opening deadline; the opening may already have transferred.", cause);
        if (operation.Caller.IsCancellationRequested)
            throw new OperationCanceledException("The ordinary main inventory workflow was cancelled before transport capacity was granted.", cause, operation.Caller);
        throw new InvalidOperationException("The ordinary main inventory workflow was retired before transport capacity was granted.", cause);
    }

    private void CancelOrdinaryMainInventoryOperation(OrdinaryMainInventoryOperation operation, OrdinaryInventoryRetirement reason)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_ordinaryMainInventoryOperation, operation) || !operation.Eligible || (reason == OrdinaryInventoryRetirement.Deadline && !operation.Opening))
                return;
            // Eligibility is retired before cancellation can resume an inline transport continuation.
            operation.Eligible = false;
            operation.Retirement = reason;
            operation.Cancellation.Cancel();
        }
    }
    private void RetireOrdinaryMainInventoryOperation()
    {
        _ordinaryNormalInventoryOwner = null;
        if (_ordinaryMainInventoryOperation is { } operation)
            CancelOrdinaryMainInventoryOperation(operation, OrdinaryInventoryRetirement.Boundary);
    }

    private void PublishOrdinaryNormalOwner(BedrockContainerOpenPacket packet)
    {
        _ordinaryNormalInventoryOwner = null;
        OrdinaryInventoryContext context = CurrentOrdinaryInventoryContext();
        if (packet.WindowType == -1 && packet.WindowId is >= 1 and <= 99 && packet.EntityUniqueId == -1
            && _creativeScreenLifetime is { } screen && IsOrdinaryInventoryContextCurrent(context)
            && _containerWindows.GetWindow(packet.WindowId) is { } registered)
            _ordinaryNormalInventoryOwner = new(registered, screen, context);
        if (_ordinaryMainInventoryOperation is not { } operation)
            return;
        if (!operation.Eligible)
            return;
        if (operation.BoundOwner is not null || GetCurrentOrdinaryNormalOwner() is null || !IsOrdinaryInventoryContextCurrent(operation.Context))
            CancelOrdinaryMainInventoryOperation(operation, OrdinaryInventoryRetirement.Boundary);
        else
        {
            operation.OpenInterruptedByNormalWindow = true;
            operation.WindowReady.TrySetResult();
        }
    }

    private BedrockInventorySnapshot ValidateOrdinaryInventoryOperation(BedrockItemStackActionType type, BedrockContainerAddress address, byte source, byte destination, byte count)
    {
        BedrockInventorySnapshot snapshot = GetPlayerInventoryRequestSnapshot(address, source, destination);
        BedrockItemInstance first = snapshot.Items[source], second = snapshot.Items[destination];
        if (first.NetworkId == 0 || first.Count == 0 || first.StackNetworkId is not > 0)
            throw new InvalidOperationException("The inventory request source requires a nonempty authoritative stack identity.");
        if (type == BedrockItemStackActionType.Swap)
        {
            if (second.NetworkId == 0 || second.Count == 0 || second.StackNetworkId is not > 0)
                throw new InvalidOperationException("The inventory swap destination requires a nonempty authoritative stack identity.");
        }
        else
        {
            if (count == 0 || count > first.Count)
                throw new InvalidOperationException("The inventory request count must fit the authoritative source.");
            if (second.NetworkId != 0 || second.Count != 0)
            {
                if (second.NetworkId == 0 || second.Count == 0 || second.StackNetworkId is not > 0)
                    throw new InvalidOperationException("The inventory destination requires consistent authoritative stack identity.");
                if ((int)second.Count + count > byte.MaxValue || !snapshot.HasCompatibleItemData(source, destination))
                    throw new InvalidOperationException("The inventory destination item or response count is unsupported.");
            }
        }
        return snapshot;
    }

    private ValueTask<BedrockItemStackSubmitResult> SendOrdinaryInventoryOperationAsync(BedrockItemStackActionType type, BedrockContainerAddress address, byte source, byte destination, byte count, CancellationToken cancellationToken, OrdinaryMainInventoryOperation? operation) => type switch
    {
        BedrockItemStackActionType.Take => SubmitInventoryTakeCoreAsync(address, source, destination, count, cancellationToken, operation),
        BedrockItemStackActionType.Place => SubmitInventoryPlaceCoreAsync(address, source, destination, count, cancellationToken, operation),
        BedrockItemStackActionType.Swap => SubmitInventorySwapCoreAsync(address, source, destination, cancellationToken, operation),
        _ => throw new InvalidOperationException("The ordinary inventory operation is unsupported."),
    };

    private async ValueTask<BedrockItemStackSubmitResult> SubmitOrdinaryInventoryAsync(BedrockItemStackActionType type, BedrockContainerAddress address, byte source, byte destination, byte count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source <= 8 && destination <= 8)
            return await SendOrdinaryInventoryOperationAsync(type, address, source, destination, count, cancellationToken, null).ConfigureAwait(false);
        OrdinaryMainInventoryOperation operation;
        lock (_gate)
        {
            RequireNoObservedOakDropSend();
            RequireNoOrdinaryMainInventoryOperation();
            _ = ValidateOrdinaryInventoryOperation(type, address, source, destination, count);
            OrdinaryInventoryContext context = CurrentOrdinaryInventoryContext();
            if (!IsOrdinaryInventoryContextCurrent(context))
                throw new InvalidOperationException("The ordinary main inventory workflow requires the current ready player life and registry.");
            if (_activeInventorySends != 0 || _normalInventoryOpenAdmission is not null || _itemStackTracker.PendingCount != 0
                || _itemStackReconciliations.Count != 0 || _creativeCursorReconciliations.Count != 0 || _cursorPlaceReconciliations.Count != 0 || _actionCoordinator.ClaimedCount != 0)
                throw new InvalidOperationException("The ordinary main inventory workflow requires inventory ownership without an older operation.");
            cancellationToken.ThrowIfCancellationRequested();
            operation = new(context, cancellationToken) { BoundOwner = GetCurrentOrdinaryNormalOwner() };
            operation.Opening = operation.BoundOwner is null;
            _ordinaryMainInventoryOperation = operation;
            BeginInventorySend();
        }
        CancellationTokenRegistration callerRegistration = default;
        ITimer? deadline = null;
        try
        {
            callerRegistration = cancellationToken.Register(() => CancelOrdinaryMainInventoryOperation(operation, OrdinaryInventoryRetirement.Caller));
            if (operation.Opening)
            {
                deadline = _settings.TimeProvider.CreateTimer(_ => CancelOrdinaryMainInventoryOperation(operation, OrdinaryInventoryRetirement.Deadline), null, s_ordinaryInventoryOpeningTimeout, Timeout.InfiniteTimeSpan);
                try
                {
                    _ = await SubmitNormalInventoryOpenCoreAsync(operation.Cancellation.Token, operation).ConfigureAwait(false);
                }
                catch (NormalInventoryOpenInterruptedException)
                {
                    // Only a typed real-window interruption permits continuation without a transferred33.
                    lock (_gate)
                    {
                        if (!operation.Eligible || GetCurrentOrdinaryNormalOwner() is null)
                            ThrowOrdinaryMainInventoryRetirement(operation);
                    }
                }
                await operation.WindowReady.Task.WaitAsync(operation.Cancellation.Token).ConfigureAwait(false);
            }
            lock (_gate)
            {
                if (!operation.Eligible || !IsOrdinaryInventoryContextCurrent(operation.Context))
                    ThrowOrdinaryMainInventoryRetirement(operation);
                operation.BoundOwner = GetCurrentOrdinaryNormalOwner();
                if (operation.BoundOwner is null)
                    throw new InvalidOperationException("The actual current normal inventory window is unconfirmed.");
                operation.Opening = false;
                _ = ValidateOrdinaryInventoryOperation(type, address, source, destination, count);
            }
            deadline?.Dispose();
            deadline = null;
            // Re-sample the current receipt/identity in the existing builder, then admit under the same Session gate.
            return await SendOrdinaryInventoryOperationAsync(type, address, source, destination, count, operation.Cancellation.Token, operation).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (operation.Cancellation.IsCancellationRequested)
        {
            lock (_gate)
                ThrowOrdinaryMainInventoryRetirement(operation, exception);
            throw;
        }
        finally
        {
            deadline?.Dispose();
            callerRegistration.Dispose();
            lock (_gate)
            {
                operation.Eligible = false;
                if (ReferenceEquals(_ordinaryMainInventoryOperation, operation))
                    _ordinaryMainInventoryOperation = null;
            }
            operation.Cancellation.Dispose();
            EndInventorySend();
        }
    }

    private static bool HasOrdinaryPlayerMainEndpoint(BedrockItemStackRequestPacket packet)
    {
        static bool Main(BedrockStackRequestSlot slot) => slot.Container.DynamicContainerId is null
            && slot.Container.ContainerId is 28 or 29 && slot.Slot is >= 9 and <= 35;
        foreach (BedrockItemStackRequest request in packet.Requests)
        {
            foreach (BedrockItemStackAction action in request.Actions)
            {
                if (action.Payload is BedrockTakePayload take && (Main(take.Source) || Main(take.Destination))
                    || action.Payload is BedrockPlacePayload place && (Main(place.Source) || Main(place.Destination))
                    || action.Payload is BedrockSwapPayload swap && (Main(swap.Source) || Main(swap.Destination)))
                    return true;
            }
        }
        return false;
    }

    private BedrockInventorySnapshot GetPlayerInventoryRequestSnapshot(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot)
    {
        if (contentAddress.WindowId != 0 || contentAddress.ContainerId != 0 || contentAddress.DynamicContainerId.HasValue)
            throw new InvalidOperationException("The inventory request address is not a supported player inventory container.");
        if (!BedrockPlayerInventoryAddressing.TryGetRequestContainer(sourceSlot, out _)
            || !BedrockPlayerInventoryAddressing.TryGetRequestContainer(destinationSlot, out _))
            throw new InvalidOperationException("The inventory request slot is outside the supported player range.");
        if (sourceSlot == destinationSlot)
            throw new InvalidOperationException("The inventory request source and destination slots must differ.");
        BedrockInventorySnapshot snapshot = _inventory.GetSnapshot(contentAddress)
            ?? throw new InvalidOperationException("The inventory request targets a container without authoritative content.");
        if (snapshot.Items.IsDefault
            || (!snapshot.KnownSlots.IsDefault && snapshot.KnownSlots.Length != snapshot.Items.Length)
            || (!snapshot.ResponseCorrections.IsDefault && snapshot.ResponseCorrections.Length != snapshot.Items.Length))
            throw new InvalidOperationException("The inventory request has malformed authoritative data.");
        if (!BedrockPlayerInventoryAddressing.IsSupportedPhysicalSlot(sourceSlot, snapshot.Items.Length)
            || !BedrockPlayerInventoryAddressing.IsSupportedPhysicalSlot(destinationSlot, snapshot.Items.Length))
            throw new InvalidOperationException("The inventory request slot is outside the authoritative container.");
        if (!snapshot.IsSlotKnown(sourceSlot) || !snapshot.IsSlotKnown(destinationSlot))
            throw new InvalidOperationException("The inventory request requires authoritative data for both affected slots.");
        if (snapshot.Items[sourceSlot] is null || snapshot.Items[destinationSlot] is null)
            throw new InvalidOperationException("The inventory request has malformed authoritative item data.");
        // Both retained endpoint counts must fit the paired response bytes; this does not assert a game item maximum.
        if (snapshot.Items[sourceSlot].Count > byte.MaxValue || snapshot.Items[destinationSlot].Count > byte.MaxValue)
            throw new InvalidOperationException("The inventory request exceeds the supported response count range.");
        return snapshot;
    }

    /// <summary>Validates one player inventory place against authoritative content, admits the single outstanding client request, and sends the protocol-2169 packet.</summary>
    /// <remarks>Physical player slots 0 through 35 are supported when present and known. Main endpoints reuse a current actual normal window or transfer one opening and wait up to 15 managed seconds for its actual normal event; caller and lifecycle cancellation retire that wait. Slot authority is sampled again after opening. The returned result means transferred147, while accepted148 and its receipt-guarded publication remain separate. Response-byte representability is a support boundary; unknown item capacity remains server-decided.</remarks>
    internal ValueTask<BedrockItemStackSubmitResult> SubmitInventoryPlaceAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, byte count, CancellationToken cancellationToken = default)
        => SubmitOrdinaryInventoryAsync(BedrockItemStackActionType.Place, contentAddress, sourceSlot, destinationSlot, count, cancellationToken);

    private async ValueTask<BedrockItemStackSubmitResult> SubmitInventoryPlaceCoreAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, byte count, CancellationToken cancellationToken, OrdinaryMainInventoryOperation? operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BedrockInventorySnapshot snapshot = GetPlayerInventoryRequestSnapshot(contentAddress, sourceSlot, destinationSlot);
        if (count == 0)
            throw new InvalidOperationException("The inventory request count must be positive.");
        BedrockItemInstance sourceItem = snapshot.Items[sourceSlot];
        BedrockItemInstance destinationItem = snapshot.Items[destinationSlot];
        if (sourceItem.NetworkId == 0 || sourceItem.Count == 0)
            throw new InvalidOperationException("The inventory request source slot is empty.");
        if (sourceItem.StackNetworkId is not > 0)
            throw new InvalidOperationException("The inventory request source slot has no authoritative stack identity.");
        if (count > sourceItem.Count)
            throw new InvalidOperationException("The inventory request count exceeds the authoritative source count.");
        int destinationStackId = 0;
        if (destinationItem.NetworkId != 0 || destinationItem.Count != 0)
        {
            if (destinationItem.NetworkId == 0 || destinationItem.Count == 0)
                throw new InvalidOperationException("The inventory request destination has inconsistent item data.");
            if ((int)destinationItem.Count + count > byte.MaxValue)
                throw new InvalidOperationException("The inventory request merge exceeds the supported response count range.");
            if (!snapshot.HasCompatibleItemData(sourceSlot, destinationSlot))
                throw new InvalidOperationException("The inventory request destination slot holds a different item for this operation.");
            if (destinationItem.StackNetworkId is not > 0)
                throw new InvalidOperationException("The inventory request destination slot has no authoritative stack identity.");
            destinationStackId = destinationItem.StackNetworkId.Value;
        }

        (int requestId, int generation) = TryAdmitItemStackRequest(snapshot, sourceSlot, destinationSlot, operation);

        BedrockItemStackRequestPacket packet;
        try
        {
            packet = BedrockItemStackEgress.BuildPlace(requestId, sourceSlot, destinationSlot, count, sourceItem.StackNetworkId.Value, destinationStackId);
        }
        catch
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw;
        }

        bool stale;
        lock (_gate)
            stale = _itemStackEgressRetired || _itemStackTracker.Generation != generation;
        if (stale)
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw new InvalidOperationException("The inventory request path is retired after a lifecycle boundary until reconnect.");
        }

        return await SendItemStackRequestCoreAsync(packet, requestId, generation, cancellationToken, snapshot, null, null, operation).ConfigureAwait(false);
    }

    /// <summary>Validates one player inventory take against authoritative content, admits the single outstanding client request, and sends the protocol-2169 packet.</summary>
    /// <remarks>Physical player slots 0 through 35 are supported when present and known. Main endpoints reuse a current actual normal window or transfer one opening and wait up to 15 managed seconds for its actual normal event; caller and lifecycle cancellation retire that wait. Slot authority is sampled again after opening. The returned result means transferred147, while accepted148 and its receipt-guarded publication remain separate. Response-byte representability is a support boundary; unknown item capacity remains server-decided.</remarks>
    internal ValueTask<BedrockItemStackSubmitResult> SubmitInventoryTakeAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, byte count, CancellationToken cancellationToken = default)
        => SubmitOrdinaryInventoryAsync(BedrockItemStackActionType.Take, contentAddress, sourceSlot, destinationSlot, count, cancellationToken);

    private async ValueTask<BedrockItemStackSubmitResult> SubmitInventoryTakeCoreAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, byte count, CancellationToken cancellationToken, OrdinaryMainInventoryOperation? operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BedrockInventorySnapshot snapshot = GetPlayerInventoryRequestSnapshot(contentAddress, sourceSlot, destinationSlot);
        if (count == 0)
            throw new InvalidOperationException("The inventory request count must be positive.");
        BedrockItemInstance sourceItem = snapshot.Items[sourceSlot];
        BedrockItemInstance destinationItem = snapshot.Items[destinationSlot];
        if (sourceItem.NetworkId == 0 || sourceItem.Count == 0)
            throw new InvalidOperationException("The inventory request source slot is empty.");
        if (sourceItem.StackNetworkId is not > 0)
            throw new InvalidOperationException("The inventory request source slot has no authoritative stack identity.");
        if (count > sourceItem.Count)
            throw new InvalidOperationException("The inventory request count exceeds the authoritative source count.");
        int destinationStackId = 0;
        if (destinationItem.NetworkId != 0 || destinationItem.Count != 0)
        {
            if (destinationItem.NetworkId == 0 || destinationItem.Count == 0)
                throw new InvalidOperationException("The inventory request destination has inconsistent item data.");
            if ((int)destinationItem.Count + count > byte.MaxValue)
                throw new InvalidOperationException("The inventory request merge exceeds the supported response count range.");
            if (!snapshot.HasCompatibleItemData(sourceSlot, destinationSlot))
                throw new InvalidOperationException("The inventory request destination slot holds a different item for this operation.");
            if (destinationItem.StackNetworkId is not > 0)
                throw new InvalidOperationException("The inventory request destination slot has no authoritative stack identity.");
            destinationStackId = destinationItem.StackNetworkId.Value;
        }

        (int requestId, int generation) = TryAdmitItemStackRequest(snapshot, sourceSlot, destinationSlot, operation);

        BedrockItemStackRequestPacket packet;
        try
        {
            packet = BedrockItemStackEgress.BuildTake(requestId, sourceSlot, destinationSlot, count, sourceItem.StackNetworkId.Value, destinationStackId);
        }
        catch
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw;
        }

        bool stale;
        lock (_gate)
            stale = _itemStackEgressRetired || _itemStackTracker.Generation != generation;
        if (stale)
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw new InvalidOperationException("The inventory request path is retired after a lifecycle boundary until reconnect.");
        }

        return await SendItemStackRequestCoreAsync(packet, requestId, generation, cancellationToken, snapshot, null, null, operation).ConfigureAwait(false);
    }

    /// <summary>Validates one player inventory swap against authoritative content, admits the single outstanding client request, and sends the protocol-2169 packet.</summary>
    /// <remarks>Physical player slots 0 through 35 are supported when present and known. Main endpoints reuse a current actual normal window or transfer one opening and wait up to 15 managed seconds for its actual normal event; caller and lifecycle cancellation retire that wait. Slot authority is sampled again after opening. The returned result means transferred147, while accepted148 and its receipt-guarded publication remain separate. Response-byte representability is a support boundary; unknown item capacity remains server-decided.</remarks>
    internal ValueTask<BedrockItemStackSubmitResult> SubmitInventorySwapAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, CancellationToken cancellationToken = default)
        => SubmitOrdinaryInventoryAsync(BedrockItemStackActionType.Swap, contentAddress, sourceSlot, destinationSlot, 0, cancellationToken);

    private async ValueTask<BedrockItemStackSubmitResult> SubmitInventorySwapCoreAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, CancellationToken cancellationToken, OrdinaryMainInventoryOperation? operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BedrockInventorySnapshot snapshot = GetPlayerInventoryRequestSnapshot(contentAddress, sourceSlot, destinationSlot);
        BedrockItemInstance sourceItem = snapshot.Items[sourceSlot];
        BedrockItemInstance destinationItem = snapshot.Items[destinationSlot];
        if (sourceItem.NetworkId == 0 || sourceItem.Count == 0)
            throw new InvalidOperationException("The inventory request source slot is empty.");
        if (sourceItem.StackNetworkId is not > 0)
            throw new InvalidOperationException("The inventory request source slot has no authoritative stack identity.");
        if (destinationItem.NetworkId == 0 || destinationItem.Count == 0)
            throw new InvalidOperationException("The inventory request destination slot is empty for this operation.");
        if (destinationItem.StackNetworkId is not > 0)
            throw new InvalidOperationException("The inventory request destination slot has no authoritative stack identity.");

        (int requestId, int generation) = TryAdmitItemStackRequest(snapshot, sourceSlot, destinationSlot, operation);

        BedrockItemStackRequestPacket packet;
        try
        {
            packet = BedrockItemStackEgress.BuildSwap(requestId, sourceSlot, destinationSlot, sourceItem.StackNetworkId.Value, destinationItem.StackNetworkId.Value);
        }
        catch
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw;
        }

        bool stale;
        lock (_gate)
            stale = _itemStackEgressRetired || _itemStackTracker.Generation != generation;
        if (stale)
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw new InvalidOperationException("The inventory request path is retired after a lifecycle boundary until reconnect.");
        }

        return await SendItemStackRequestCoreAsync(packet, requestId, generation, cancellationToken, snapshot, null, null, operation).ConfigureAwait(false);
    }

    /// <summary>Retains and emits only a supplied observed oak-planks Take64 request against actual registry, catalog, normal screen and empty UI cursor owners.</summary>
    /// <remarks>This internal witness path does not build a request or qualify a generic creative maximum-stack policy.</remarks>
    internal async ValueTask<BedrockItemStackSubmitResult> SubmitObservedCreativeCursorRequestAsync(BedrockCreativeEntry entry, BedrockItemStackRequestPacket observedRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(observedRequest);
        cancellationToken.ThrowIfCancellationRequested();
        BedrockCreativeCursorReconciliation retained;
        lock (_gate)
        {
            RequireNoOrdinaryMainInventoryOperation();
            RequireNoObservedOakDropSend();
            if (!_snapshot.IsReady || _disposed || _inventoryTornDown || _itemStackEgressRetired)
                throw new InvalidOperationException("The observed creative cursor request requires a live ready inventory path.");
            if (_itemStackTracker.PendingCount != 0 || _inventory.Generation != _itemStackTracker.Generation)
                throw new InvalidOperationException("The observed creative cursor request requires the current inventory generation and no outstanding request.");
            BedrockCreativeCursorOwnership ownership = GetCreativeCursorOwnership()
                ?? throw new InvalidOperationException("The creative registry, catalog, or normal inventory screen is unqualified.");
            BedrockItemState[] matching = _creativeInitialItems!.Items.Where(item => item.RuntimeId == entry.Item.NetworkId).ToArray();
            if (matching.Length != 1 || matching[0].Name != "minecraft:oak_planks" || matching[0].ComponentBased || matching[0].Version != 2
                || matching[0].Components is not NbtCompound { IsEmpty: true } || entry.Item.MetadataValue != 0
                || entry.Item.ExtraData.Length != 10 || entry.Item.ExtraData.Any(static value => value != 0))
                throw new InvalidOperationException("Only the supplied observed vanilla oak-planks definition and item representation are qualified.");
            BedrockInventorySnapshot before = _inventory.GetSnapshot(s_creativeCursorPool)
                ?? throw new InvalidOperationException("The actual UI cursor pool is unavailable.");
            if (!_creativeCursorBaselineQualified)
                throw new InvalidOperationException("The creative cursor requires a fresh authoritative UI content baseline after owner retirement.");
            retained = BedrockCreativeCursorReconciliation.Create(before, entry, matching[0], observedRequest, ownership);
            if (!_inventory.MatchesSnapshot(before, retained.AffectedSlots, ownership.InventoryGeneration))
                throw new InvalidOperationException("The creative cursor baseline changed before admission.");
            if (!_itemStackRequestIds.TryAllocate(out int requestId))
                throw new InvalidOperationException("The inventory request identity range is exhausted until reconnect.");
            if (requestId != retained.RequestId)
                throw new InvalidOperationException("The supplied creative request does not match the next allocated identity; its allocated value remains retired.");
            if (!_actionCoordinator.TryClaim(requestId, BedrockActionEmission.Standalone))
                throw new InvalidOperationException("The creative action is already claimed.");
            if (!_itemStackTracker.TryRegister(requestId, ownership.InventoryGeneration))
            {
                _actionCoordinator.Release(requestId);
                throw new InvalidOperationException("The creative request could not be registered.");
            }
            _creativeCursorReconciliations.Add(requestId, retained);
        }
        return await SendItemStackRequestAsync(retained.Packet, retained.RequestId, retained.Generation, cancellationToken, creative: retained).ConfigureAwait(false);
    }

    /// <summary>Retains only the supplied observed oak cursor Place64 into empty hotbar slot zero under two real container owners.</summary>
    internal async ValueTask<BedrockItemStackSubmitResult> SubmitObservedCreativeCursorPlaceAsync(BedrockCreativeEntry entry, BedrockItemStackRequestPacket observedRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(observedRequest);
        cancellationToken.ThrowIfCancellationRequested();
        BedrockCursorPlaceReconciliation retained;
        lock (_gate)
        {
            RequireNoOrdinaryMainInventoryOperation();
            RequireNoObservedOakDropSend();
            if (!_snapshot.IsReady || !_initialLoadingComplete || _initialLoadingRetired || _disposed || _inventoryTornDown || _itemStackEgressRetired
                || _lifetime.Token.IsCancellationRequested || _lifetime.Termination.IsCompleted || _activeInventorySends != 0)
                throw new InvalidOperationException("The cursor transfer requires a live ready inventory path without an older active send.");
            if (_itemStackTracker.PendingCount != 0 || _inventory.Generation != _itemStackTracker.Generation)
                throw new InvalidOperationException("The cursor transfer requires the current inventory generation and no outstanding request.");
            BedrockCreativeCursorOwnership ownership = GetCreativeCursorOwnership()
                ?? throw new InvalidOperationException("The actual cursor transfer owners are unqualified.");
            BedrockItemState[] matching = _creativeInitialItems!.Items.Where(item => item.RuntimeId == entry.Item.NetworkId).ToArray();
            if (matching.Length != 1)
                throw new InvalidOperationException("The cursor transfer entry has no unique actual registry definition.");
            BedrockInventorySnapshot cursor = _inventory.GetSnapshot(s_creativeCursorPool)
                ?? throw new InvalidOperationException("The actual cursor pool is unavailable.");
            BedrockInventorySnapshot player = _inventory.GetSnapshot(new(0, 0, null))
                ?? throw new InvalidOperationException("The actual player inventory is unavailable.");
            retained = BedrockCursorPlaceReconciliation.Create(cursor, player, entry, matching[0], observedRequest, ownership);
            if (!_inventory.MatchesCursorPlaceSnapshots(cursor, player, ownership.InventoryGeneration))
                throw new InvalidOperationException("The affected cursor transfer baselines changed before admission.");
            if (_cursorPlacePathActivated && (_cursorPlaceFreshAuthorityRequired || _cursorPlaceSourceQualification is not { } qualification
                || !SameCursorPlaceOwnership(qualification.Ownership, ownership) || !ReferenceEquals(qualification.Receipt, cursor.AuthorityReceipts[0])))
                throw new InvalidOperationException("The cursor transfer requires a fresh authoritative receipt belonging to the current owners.");
            if (!_itemStackRequestIds.TryAllocate(out int requestId))
                throw new InvalidOperationException("The inventory request identity range is exhausted until reconnect.");
            if (requestId != retained.RequestId)
                throw new InvalidOperationException("The supplied cursor transfer does not match the next allocated identity; its allocated value remains retired.");
            if (!_actionCoordinator.TryClaim(requestId, BedrockActionEmission.Standalone))
                throw new InvalidOperationException("The cursor transfer action is already claimed.");
            if (!_itemStackTracker.TryRegister(requestId, ownership.InventoryGeneration))
            {
                _actionCoordinator.Release(requestId);
                throw new InvalidOperationException("The cursor transfer request could not be registered.");
            }
            _cursorPlaceReconciliations.Add(requestId, retained);
            // Only successful transfer registration activates this separate qualification path.
            _cursorPlacePathActivated = true;
            _cursorPlaceFreshAuthorityRequired = false;
            _cursorPlaceSourceQualification = new(ownership, cursor.AuthorityReceipts[0]);
        }
        return await SendItemStackRequestAsync(retained.Packet, retained.RequestId, retained.Generation, cancellationToken, cursorPlace: retained).ConfigureAwait(false);
    }

    private BedrockCreativeCursorOwnership? GetCreativeCursorOwnership()
    {
        if (_creativeRegistryRetired || _creativeRegistryPublicationPending || _creativeInitialItems is null || _creativeRegistryOwner is null
            || !ReferenceEquals(_creativeRegistryOwner, _connection.Context.Registries) || _lastCreativeContent is null
            || _creativeScreen is not { } screen || _creativeScreenLifetime is null)
            return null;
        return new(_inventory.Generation, _creativeRegistryOwner, _lastCreativeContent, screen, _creativeScreenLifetime);
    }

    // Called under the Session gate. Retirement removes ownership before cancellation can finish a send inline.
    private void RetireCreativeCursorQualification()
    {
        RetireObservedOakDropOwner();
        RetireCursorPlaceRequests();
        if (_cursorPlacePathActivated)
        {
            _cursorPlaceSourceQualification = null;
            _cursorPlaceFreshAuthorityRequired = true;
        }
        _creativeCursorBaselineQualified = false;
        BedrockInventorySnapshot? before = _inventory.GetSnapshot(s_creativeCursorPool);
        if (before is { } cursor && cursor.Items.Length > 0
            && !(_cursorPlacePathActivated && cursor.IsSlotKnown(0) && !cursor.AuthorityReceipts.IsDefault
                && ReferenceEquals(cursor.AuthorityReceipts[0], _cursorPlaceProtectedReceipt)))
            _inventory.ApplyResponseUpdates(cursor, [], [0], _inventory.Generation);
        foreach ((int requestId, BedrockCreativeCursorReconciliation retained) in _creativeCursorReconciliations.ToArray())
        {
            _creativeCursorReconciliations.Remove(requestId);
            _itemStackTracker.TryCancel(requestId, retained.Generation);
            _actionCoordinator.Release(requestId);
            if (_itemStackAdmissions.TryGetValue(requestId, out CancellationTokenSource? admission))
                admission.Cancel();
        }
    }

    // Actual replacement data stays known; only the operation built from a different affected cursor baseline is retired.
    private void RetireChangedCreativeCursorRequests()
    {
        foreach ((int requestId, BedrockCreativeCursorReconciliation retained) in _creativeCursorReconciliations.ToArray())
        {
            if (_inventory.MatchesSnapshot(retained.Before, retained.AffectedSlots, retained.Generation))
                continue;
            _creativeCursorReconciliations.Remove(requestId);
            _itemStackTracker.TryCancel(requestId, retained.Generation);
            _actionCoordinator.Release(requestId);
            if (_itemStackAdmissions.TryGetValue(requestId, out CancellationTokenSource? admission))
                admission.Cancel();
        }
    }

    private void RetireCursorPlaceRequests()
    {
        foreach ((int requestId, BedrockCursorPlaceReconciliation retained) in _cursorPlaceReconciliations.ToArray())
            RetireCursorPlaceRequest(requestId, retained);
    }

    private void RetireChangedCursorPlaceRequests()
    {
        foreach ((int requestId, BedrockCursorPlaceReconciliation retained) in _cursorPlaceReconciliations.ToArray())
        {
            if (!_inventory.MatchesCursorPlaceSnapshots(retained.CursorBefore, retained.PlayerBefore, retained.Generation))
                RetireCursorPlaceRequest(requestId, retained);
        }
    }

    private void RetireCursorPlaceRequest(int requestId, BedrockCursorPlaceReconciliation retained)
    {
        _cursorPlaceReconciliations.Remove(requestId);
        _inventory.InvalidateCursorPlaceSlots(retained.CursorBefore, retained.PlayerBefore, retained.Generation);
        _cursorPlaceSourceQualification = null;
        _cursorPlaceFreshAuthorityRequired = true;
        _itemStackTracker.TryCancel(requestId, retained.Generation);
        _actionCoordinator.Release(requestId);
        if (_itemStackAdmissions.TryGetValue(requestId, out CancellationTokenSource? admission))
            admission.Cancel();
    }

    // Called only by actual affected inbound publications, never by a getter or a preserved-known-data check.
    private void RefreshCursorPlaceSourceQualification(bool protectReceipt)
    {
        if (!_cursorPlacePathActivated)
            return;
        BedrockInventorySnapshot? snapshot = _inventory.GetSnapshot(s_creativeCursorPool);
        if (snapshot is not { } cursor || !cursor.IsSlotKnown(0) || cursor.AuthorityReceipts.IsDefault)
        {
            _cursorPlaceSourceQualification = null;
            _cursorPlaceFreshAuthorityRequired = true;
            _cursorPlaceProtectedReceipt = null;
            return;
        }
        if (protectReceipt)
            _cursorPlaceProtectedReceipt = cursor.AuthorityReceipts[0];
        if (GetCreativeCursorOwnership() is { } ownership)
        {
            _cursorPlaceSourceQualification = new(ownership, cursor.AuthorityReceipts[0]);
            _cursorPlaceFreshAuthorityRequired = false;
        }
        else
        {
            _cursorPlaceSourceQualification = null;
            _cursorPlaceFreshAuthorityRequired = true;
        }
    }

    private static bool SameCursorPlaceOwnership(BedrockCreativeCursorOwnership first, BedrockCreativeCursorOwnership second) =>
        first.InventoryGeneration == second.InventoryGeneration && ReferenceEquals(first.Registry, second.Registry)
        && ReferenceEquals(first.Catalog, second.Catalog) && first.Screen == second.Screen && ReferenceEquals(first.ScreenLifetime, second.ScreenLifetime);

    private void ClearCursorPlaceQualification()
    {
        _cursorPlacePathActivated = false;
        _cursorPlaceSourceQualification = null;
        _cursorPlaceProtectedReceipt = null;
        _cursorPlaceFreshAuthorityRequired = false;
    }

    /// <summary>Binds the retirement flag, coordinator claim, and tracker registration under the session gate and returns the never-reused odd negative identifier with its admitted generation.</summary>
    /// <remarks>The shared session gate prevents a lifecycle or close boundary from slipping between those checks, and only one inventory request stays outstanding while issued identifiers are never reused.</remarks>
    private (int RequestId, int Generation) TryAdmitItemStackRequest(BedrockInventorySnapshot before, byte sourceSlot, byte destinationSlot, OrdinaryMainInventoryOperation? operation = null)
    {
        lock (_gate)
        {
            RequireNoObservedOakDropSend();
            RequireOrdinaryMainInventoryOwner(operation);
            if (_disposed || _inventoryTornDown || _lifetime.Termination.IsCompleted || _lifetime.Token.IsCancellationRequested)
                throw new InvalidOperationException("The inventory request requires a live session.");
            if (!_snapshot.IsReady)
                throw new InvalidOperationException("The inventory request requires a ready session.");
            if (_itemStackEgressRetired)
                throw new InvalidOperationException("The inventory request path is retired after a lifecycle boundary until reconnect.");
            if (_inventory.Generation != _itemStackTracker.Generation)
                throw new InvalidOperationException("The inventory request generation is stale.");
            if (_itemStackTracker.PendingCount != 0)
                throw new InvalidOperationException("Only one inventory request stays outstanding.");
            int generation = _itemStackTracker.Generation;
            if (!_inventory.MatchesSnapshot(before, [sourceSlot, destinationSlot], generation))
                throw new InvalidOperationException("The inventory request content changed before admission.");
            if (!_itemStackRequestIds.TryAllocate(out int requestId))
                throw new InvalidOperationException("The inventory request identity range is exhausted until reconnect.");
            if (!_actionCoordinator.TryClaim(requestId, BedrockActionEmission.Standalone))
                throw new InvalidOperationException($"The inventory action {requestId} is already claimed.");
            if (!_itemStackTracker.TryRegister(requestId, generation))
            {
                _actionCoordinator.Release(requestId);
                throw new InvalidOperationException($"The inventory request {requestId} could not be registered. Only one inventory request stays outstanding.");
            }

            if (operation is not null)
                operation.RequestId = requestId;
            return (requestId, generation);
        }
    }

    private void ReleaseItemStackRequest(int requestId, int generation)
    {
        lock (_gate)
        {
            _itemStackReconciliations.Remove(requestId);
            _creativeCursorReconciliations.Remove(requestId);
            _cursorPlaceReconciliations.Remove(requestId);
            _itemStackAdmissions.Remove(requestId);
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
        }
    }

    private bool _observedOakDropOpportunityConsumed;
    private ObservedOakDropSend? _observedOakDropSend;
    private ObservedOakDropReceiptOwner? _observedOakDropReceiptOwner;
    private sealed record ObservedOakDropReceiptOwner(BedrockContainerAddress Address, object AuthorityReceipt, int Generation, BedrockCreativeCursorOwnership Ownership);

    private sealed class ObservedOakDropSend(BedrockObservedOakDropPlan plan, BedrockCreativeCursorOwnership ownership, int initialEpoch, ulong runtimeId, int actorWorld, CancellationTokenSource cancellation)
    {
        internal BedrockObservedOakDropPlan Plan { get; } = plan;
        internal BedrockCreativeCursorOwnership Ownership { get; } = ownership;
        internal int InitialEpoch { get; } = initialEpoch;
        internal ulong RuntimeId { get; } = runtimeId;
        internal int ActorWorld { get; } = actorWorld;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal bool Eligible { get; set; } = true;
        internal BedrockTermination? TerminalRetirement { get; set; }
    }

    internal bool ObservedOakDropSendActive { get { lock (_gate) return _observedOakDropSend is not null; } }
    internal bool ObservedOakDropPredictionEligible { get { lock (_gate) return _observedOakDropSend is { Eligible: true }; } }
    internal bool ObservedOakDropReceiptRetained { get { lock (_gate) return _observedOakDropReceiptOwner is not null; } }

    /// <summary>Sends only the reviewed first legacy minus-four oak Drop-one and reports transfer independently of server acceptance.</summary>
    internal async ValueTask<BedrockObservedOakDropSubmitResult> SubmitObservedOakDropOneAsync(BedrockCreativeEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        ObservedOakDropSend send;
        lock (_gate)
        {
            RequireNoOrdinaryMainInventoryOperation();
            if (_disposed || _inventoryTornDown || _itemStackEgressRetired || _initialLoadingRetired
                || _lifetime.Termination.IsCompleted || _lifetime.Token.IsCancellationRequested
                || !_snapshot.IsReady || !_initialLoadingComplete || !_initialLoadingStarted || !_initialLoadingEndTransferred
                || _snapshot.LocalRuntimeEntityId == 0)
                throw new InvalidOperationException("The fixed oak Drop requires the live initial ready player life and world.");
            if (_observedOakDropSend is not null || _activeInventorySends != 0 || _normalInventoryOpenAdmission is not null
                || _itemStackTracker.PendingCount != 0 || _itemStackReconciliations.Count != 0
                || _creativeCursorReconciliations.Count != 0 || _cursorPlaceReconciliations.Count != 0
                || _actionCoordinator.ClaimedCount != 0 || _inventory.Generation != _itemStackTracker.Generation)
                throw new InvalidOperationException("The fixed oak Drop requires current inventory ownership without an older active operation.");
            if (_observedOakDropOpportunityConsumed)
                throw new InvalidOperationException("The fixed oak Drop opportunity is consumed until reconnect.");
            BedrockCreativeCursorOwnership ownership = GetCreativeCursorOwnership()
                ?? throw new InvalidOperationException("The fixed oak Drop registry, catalog or actual normal screen is unqualified.");
            BedrockItemState[] definitions = _creativeInitialItems!.Items.Where(item => item.RuntimeId == entry.Item.NetworkId).ToArray();
            if (definitions.Length != 1)
                throw new InvalidOperationException("The fixed oak Drop entry requires one actual item definition.");
            BedrockInventorySnapshot before = _inventory.GetSnapshot(new(0, 0, null))
                ?? throw new InvalidOperationException("The fixed oak Drop actual player source is absent.");
            BedrockObservedOakDropPlan plan = BedrockObservedOakDropPlan.Create(before, entry, definitions[0], ownership);
            if (!plan.IsSourceCurrent(before, _inventory.Generation))
                throw new InvalidOperationException("The fixed oak Drop source receipt changed before admission.");
            cancellationToken.ThrowIfCancellationRequested();
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token, _playerAuthInputAdmissionLifetime.Token);
            if (!_actionCoordinator.TryClaim(-4, BedrockActionEmission.Standalone))
            {
                cancellation.Dispose();
                throw new InvalidOperationException("The fixed oak Drop identity is already owned.");
            }
            send = new(plan, ownership, _initialLoadingEpoch, _snapshot.LocalRuntimeEntityId, _actors.WorldGeneration, cancellation);
            _observedOakDropOpportunityConsumed = true;
            _observedOakDropSend = send;
            BeginInventorySend();
        }

        try
        {
            byte[] payload = BedrockInventoryTransactionCodecs.Encode(send.Plan.Packet);
            lock (_gate)
            {
                BedrockInventorySnapshot? source = _inventory.GetSnapshot(send.Plan.Address);
                if (!IsObservedOakDropOwnerCurrent(send) || source is not { } current
                    || !send.Plan.IsSourceCurrent(current, _inventory.Generation))
                {
                    RetireObservedOakDropSend();
                    throw new InvalidOperationException("The fixed oak Drop source or owner retired before output admission.");
                }
            }

            var member = new BedrockBatchMember(new BedrockPacketHeader(30, new BedrockSubclientId(0), new BedrockSubclientId(0)), payload);
            MessageAdmissionResult admission;
            try
            {
                admission = await _connection.SendBatchAsync([member], send.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested || send.TerminalRetirement is not null)
            {
                throw new BedrockSessionException(send.TerminalRetirement
                    ?? new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The fixed oak Drop was cancelled before transport capacity was granted.", exception, cancellationToken);
            }
            catch (OperationCanceledException exception) when (send.Cancellation.IsCancellationRequested)
            {
                throw new InvalidOperationException("The fixed oak Drop owner or source retired before transport capacity was granted.", exception);
            }
            if (!admission.OwnershipTransferred)
            {
                lock (_gate) _rejectedSendAttempts++;
                throw new InvalidOperationException($"The fixed oak Drop was rejected with {admission.Status}.");
            }

            // No post-grant cancellation check: actual transfer owns accounting and its returned payload through the shared drain.
            RecordOutbound(30);
            lock (_gate)
            {
                BedrockObservedOakDropDisposition disposition;
                BedrockInventorySnapshot? source = _inventory.GetSnapshot(send.Plan.Address);
                // A newer real receipt wins even when an owner boundary also occurred. Absent data is owner retirement, not supersession.
                if (source is { } current && current.AuthorityReceipts.Length > 0
                    && !ReferenceEquals(current.AuthorityReceipts[0], send.Plan.AuthorityReceipt))
                    disposition = BedrockObservedOakDropDisposition.TransferredButSuperseded;
                else if (IsObservedOakDropOwnerCurrent(send) && source is { } beforePublication
                    && send.Plan.IsSourceCurrent(beforePublication, _inventory.Generation)
                    && _inventory.TryPublishTentativeSlot(send.Plan.Before, 0, send.Plan.TentativeRemainder, -4, send.Plan.Generation))
                {
                    _observedOakDropReceiptOwner = new(send.Plan.Address, send.Plan.AuthorityReceipt, send.Plan.Generation, send.Ownership);
                    disposition = BedrockObservedOakDropDisposition.TransferredWithTentativeRemainder;
                }
                else
                {
                    // Only this actual transferred plan may invalidate unchanged carried-forward authority under the current generation.
                    _inventory.TryMarkTransferredRetiredPlayerSlotUnavailable(send.Plan.Before, 0, _inventory.Generation);
                    disposition = BedrockObservedOakDropDisposition.TransferredButOwnerRetired;
                }
                return new(payload, send.Plan.Generation, send.Plan.Address, send.Plan.AuthorityReceipt, disposition);
            }
        }
        finally
        {
            lock (_gate)
            {
                // Owner retirement also holds this gate, so it cannot cancel the holder after its CTS is disposed.
                send.Cancellation.Dispose();
                if (ReferenceEquals(_observedOakDropSend, send))
                {
                    _observedOakDropSend = null;
                    _actionCoordinator.Release(-4);
                }
                EndInventorySend();
            }
        }
    }

    // Called only under the Session gate; retirement changes eligibility before CTS cancellation can resume a waiter inline.
    private void RetireObservedOakDropSend(bool cancelSend = true)
    {
        if (_observedOakDropSend is not { } send || !send.Eligible)
            return;
        send.Eligible = false;
        if (cancelSend)
            send.Cancellation.Cancel();
    }

    private void RetireObservedOakDropOwner(BedrockTermination? terminal = null, bool cancelSend = true)
    {
        if (terminal is not null && _observedOakDropSend is { } send)
            send.TerminalRetirement ??= terminal;
        ObservedOakDropReceiptOwner? owner = _observedOakDropReceiptOwner;
        _observedOakDropReceiptOwner = null;
        if (owner is not null)
            _inventory.TryRetireTentativeSlot(owner.Address, 0, owner.AuthorityReceipt, _inventory.Generation);
        RetireObservedOakDropSend(cancelSend);
    }

    // Actual affected 49/50 replaces the receipt before this call; getters and responses cannot retire this owner.
    private void RetireObservedOakDropSource()
    {
        _observedOakDropReceiptOwner = null;
        RetireObservedOakDropSend();
    }

    private bool IsObservedOakDropOwnerCurrent(ObservedOakDropSend send) =>
        ReferenceEquals(_observedOakDropSend, send) && send.Eligible && !_disposed && !_inventoryTornDown && !_itemStackEgressRetired
        && !_initialLoadingRetired && !_lifetime.Token.IsCancellationRequested && !_lifetime.Termination.IsCompleted
        && _initialLoadingEpoch == send.InitialEpoch && _snapshot.LocalRuntimeEntityId == send.RuntimeId
        && _actors.WorldGeneration == send.ActorWorld && GetCreativeCursorOwnership() is { } current && send.Plan.IsCurrent(current);

    private void RequireNoObservedOakDropSend()
    {
        if (_observedOakDropSend is not null)
            throw new InvalidOperationException("Inventory submissions are unavailable while the fixed oak Drop send is active.");
    }

    // Caller and lifecycle cancellation affect capacity waits; a granted transport reservation commits without a cancellation point.
    private void CancelItemStackAdmissions()
    {
        foreach (CancellationTokenSource admission in _itemStackAdmissions.Values)
            admission.Cancel();
        _itemStackAdmissions.Clear();
    }

    /// <summary>Sends a request whose action ID and response tracker entry were already admitted, releasing both when the connection does not take ownership of the packet.</summary>
    internal ValueTask<BedrockItemStackSubmitResult> SendItemStackRequestAsync(BedrockItemStackRequestPacket packet, int requestId, int generation, CancellationToken cancellationToken, BedrockInventorySnapshot? before = null, BedrockCreativeCursorReconciliation? creative = null, BedrockCursorPlaceReconciliation? cursorPlace = null)
        => SendItemStackRequestCoreAsync(packet, requestId, generation, cancellationToken, before, creative, cursorPlace, null);

    private async ValueTask<BedrockItemStackSubmitResult> SendItemStackRequestCoreAsync(BedrockItemStackRequestPacket packet, int requestId, int generation, CancellationToken cancellationToken, BedrockInventorySnapshot? before, BedrockCreativeCursorReconciliation? creative, BedrockCursorPlaceReconciliation? cursorPlace, OrdinaryMainInventoryOperation? operation)
    {
        CancellationTokenSource admissionCancellation;
        lock (_gate)
        {
            // A duplicate numeric sender cannot release its incumbent's registration or claim.
            if (_itemStackAdmissions.ContainsKey(requestId))
                throw new InvalidOperationException("The inventory request already has an active send.");
            if (_itemStackReconciliations.ContainsKey(requestId)
                || (_creativeCursorReconciliations.TryGetValue(requestId, out BedrockCreativeCursorReconciliation? retainedCreative) && !ReferenceEquals(retainedCreative, creative))
                || (_cursorPlaceReconciliations.TryGetValue(requestId, out BedrockCursorPlaceReconciliation? retainedCursor) && !ReferenceEquals(retainedCursor, cursorPlace)))
                throw new InvalidOperationException("The inventory request already has an owned send or response retention.");
            if (_ordinaryMainInventoryOperation is { } activeOperation && !ReferenceEquals(activeOperation, operation))
            {
                // This pre-admission refusal owns no registration; the raw caller retains its own claim.
                throw new InvalidOperationException("An ordinary main inventory workflow already owns inventory submission.");
            }
            if (operation is not null && !IsOrdinaryMainInventoryOwnerCurrent(operation))
            {
                ReleaseItemStackRequest(requestId, generation);
                ThrowOrdinaryMainInventoryRetirement(operation);
            }
            if (operation is null && creative is null && cursorPlace is null && HasOrdinaryPlayerMainEndpoint(packet))
            {
                if (before is { } unqualified)
                {
                    // Preserve the existing malformed-retained-input cleanup contract after protecting every incumbent.
                    try
                    {
                        BedrockItemStackReconciliation validation = BedrockItemStackReconciliation.Create(unqualified, packet, generation);
                        if (!_inventory.MatchesSnapshot(unqualified, validation.AffectedSlots, generation))
                            throw new InvalidOperationException("The retained inventory request became stale before sending.");
                    }
                    catch (InvalidOperationException)
                    {
                        ReleaseItemStackRequest(requestId, generation);
                        throw;
                    }
                }
                // A valid before snapshot proves content, not a private workflow or actual current normal window.
                throw new InvalidOperationException("Player main inventory requests require the owned Take, Place or Swap workflow.");
            }

            if (_observedOakDropSend is { } activeDrop)
            {
                // A duplicate active raw sender keeps its incumbent registration; a numeric collision must not release Drop's claim.
                if (_itemStackAdmissions.ContainsKey(requestId))
                    throw new InvalidOperationException("The inventory request already has an active send.");
                if (requestId == activeDrop.Plan.LegacyRequestId)
                {
                    _itemStackTracker.TryCancel(requestId, generation);
                    _itemStackReconciliations.Remove(requestId);
                    _creativeCursorReconciliations.Remove(requestId);
                    _cursorPlaceReconciliations.Remove(requestId);
                }
                else
                    ReleaseItemStackRequest(requestId, generation);
                RequireNoObservedOakDropSend();
            }
            if (_disposed || _inventoryTornDown || _itemStackEgressRetired || generation != _itemStackTracker.Generation)
            {
                ReleaseItemStackRequest(requestId, generation);
                throw new InvalidOperationException("The inventory request path is retired or its generation is stale.");
            }
            if (creative is not null && (!_creativeCursorReconciliations.TryGetValue(requestId, out BedrockCreativeCursorReconciliation? registered)
                || !ReferenceEquals(registered, creative) || GetCreativeCursorOwnership() is not { } ownership || !creative.IsCurrent(ownership)
                || !_inventory.MatchesSnapshot(creative.Before, creative.AffectedSlots, generation)))
            {
                ReleaseItemStackRequest(requestId, generation);
                throw new InvalidOperationException("The retained creative cursor ownership changed before sending.");
            }
            if (cursorPlace is not null && (!_cursorPlaceReconciliations.TryGetValue(requestId, out BedrockCursorPlaceReconciliation? registeredPlace)
                || !ReferenceEquals(registeredPlace, cursorPlace) || GetCreativeCursorOwnership() is not { } placeOwner || !cursorPlace.IsCurrent(placeOwner)
                || !_inventory.MatchesCursorPlaceSnapshots(cursorPlace.CursorBefore, cursorPlace.PlayerBefore, generation)))
            {
                ReleaseItemStackRequest(requestId, generation);
                throw new InvalidOperationException("The cursor transfer ownership changed before sending.");
            }
            if (_itemStackAdmissions.ContainsKey(requestId))
                throw new InvalidOperationException("The inventory request already has an active send.");
            // Raw requests have no retained affected-slot qualification, so any unavailable retained slot blocks admission.
            if (before is null && creative is null && cursorPlace is null && !_inventory.CanAdmitRawItemStackRequest(generation))
            {
                ReleaseItemStackRequest(requestId, generation);
                throw new InvalidOperationException("The raw inventory request requires all retained slots to be authoritative.");
            }
            admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            _itemStackAdmissions.Add(requestId, admissionCancellation);
            BeginInventorySend();
        }

        try
        {
            byte[] payload = BedrockItemStackCodecs.Encode(packet);
            cancellationToken.ThrowIfCancellationRequested();
            if (before is { } retained)
            {
                BedrockItemStackReconciliation reconciliation = BedrockItemStackReconciliation.Create(retained, packet, generation);
                if (operation is not null)
                    BeforeOrdinaryMainInventoryRetention?.Invoke();
                lock (_gate)
                {
                    if (operation is not null && !IsOrdinaryMainInventoryOwnerCurrent(operation))
                        ThrowOrdinaryMainInventoryRetirement(operation);
                    if (_itemStackEgressRetired || _inventoryTornDown || generation != _itemStackTracker.Generation
                        || !_inventory.MatchesSnapshot(retained, reconciliation.AffectedSlots, generation))
                        throw new InvalidOperationException("The retained inventory request became stale before sending.");
                    _itemStackReconciliations.Add(requestId, reconciliation);
                }
            }

            var member = new BedrockBatchMember(
                new BedrockPacketHeader(147, new BedrockSubclientId(0), new BedrockSubclientId(0)),
                payload);
            MessageAdmissionResult admission;
            try
            {
                admission = await _connection.SendBatchAsync([member], admissionCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
            {
                throw new BedrockSessionException(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The inventory request was cancelled before transport capacity was granted.", exception, cancellationToken);
            }
            catch (OperationCanceledException exception) when (admissionCancellation.IsCancellationRequested)
            {
                throw new InvalidOperationException("The inventory request was retired before transport capacity was granted.", exception);
            }

            if (!admission.OwnershipTransferred)
            {
                lock (_gate)
                    _rejectedSendAttempts++;
                throw new InvalidOperationException($"The inventory request was rejected with {admission.Status}.");
            }

            RecordOutbound(147);
            CaptureItemStackRequestPayload(payload);
            return new BedrockItemStackSubmitResult(requestId, (byte[])payload.Clone());
        }
        catch
        {
            ReleaseItemStackRequest(requestId, generation);
            throw;
        }
        finally
        {
            lock (_gate)
                _itemStackAdmissions.Remove(requestId);
            admissionCancellation.Dispose();
            // The send owns this count through result construction, accounting and its admission resource disposal, independently of response retention.
            EndInventorySend();
        }
    }

    // Called under the Session gate. Response retention is independent of the transport/result lifetime.
    private void BeginInventorySend()
    {
        if (_activeInventorySends++ == 0)
            _inventorySendsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void EndInventorySend()
    {
        lock (_gate)
        {
            if (--_activeInventorySends == 0)
                _inventorySendsDrained!.TrySetResult();
        }
    }

    /// <summary>Transfers the qualified normal-self inventory interaction and returns its actual target and payload, independently of any server screen event.</summary>
    internal ValueTask<(ulong TargetRuntimeId, byte[] OutboundPayload)> SubmitNormalInventoryOpenAsync(CancellationToken cancellationToken = default)
        => SubmitNormalInventoryOpenCoreAsync(cancellationToken, null);

    private async ValueTask<(ulong TargetRuntimeId, byte[] OutboundPayload)> SubmitNormalInventoryOpenCoreAsync(CancellationToken cancellationToken, OrdinaryMainInventoryOperation? operation)
    {
        CancellationTokenSource admissionCancellation;
        ulong runtimeId;
        lock (_gate)
        {
            RequireNoObservedOakDropSend();
            RequireNoOrdinaryMainInventoryOperation(operation);
            if (_disposed || _inventoryTornDown || _lifetime.Token.IsCancellationRequested || _lifetime.Termination.IsCompleted || _initialLoadingRetired)
                throw new InvalidOperationException("The normal inventory opening requires a live initial player life and world.");
            if (!_snapshot.IsReady || !_initialLoadingComplete || !_initialLoadingStarted || !_initialLoadingEndTransferred)
                throw new InvalidOperationException("The normal inventory opening requires completed headless readiness.");
            runtimeId = _snapshot.LocalRuntimeEntityId;
            if (runtimeId == 0)
                throw new InvalidOperationException("The normal inventory opening requires the current own runtime identity.");
            if (_normalInventoryOpenAdmission is not null)
                throw new InvalidOperationException("Only one normal inventory opening send may be active.");
            cancellationToken.ThrowIfCancellationRequested();
            admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token, _playerAuthInputAdmissionLifetime.Token);
            _normalInventoryOpenAdmission = admissionCancellation;
            BeginInventorySend();
        }

        try
        {
            var output = new ArrayBufferWriter<byte>();
            var writer = new BedrockWriter(output);
            writer.WriteByte(6);
            writer.WriteUInt64VarInt(runtimeId);
            writer.WriteBoolean(false);
            byte[] payload = output.WrittenSpan.ToArray();
            var member = new BedrockBatchMember(new BedrockPacketHeader(33, new BedrockSubclientId(0), new BedrockSubclientId(0)), payload);
            MessageAdmissionResult admission = await _connection.SendBatchAsync([member], admissionCancellation.Token).ConfigureAwait(false);
            if (!admission.OwnershipTransferred)
            {
                lock (_gate)
                    _rejectedSendAttempts++;
                throw new InvalidOperationException($"The normal inventory opening was rejected with {admission.Status}.");
            }

            // A granted token-free commit owns this result; no late screen or life event is reconstructed here.
            lock (_gate)
            {
                RecordOutbound(33);
                if (_inventoryCaptureEnabled && payload.Length <= MaximumCapturedInventoryPayloadBytes
                    && _capturedNormalInventoryOpenPayloads.Count < MaximumCapturedInventoryPackets)
                    _capturedNormalInventoryOpenPayloads.Add((byte[])payload.Clone());
            }
            return (runtimeId, (byte[])payload.Clone());
        }
        catch (OperationCanceledException exception) when (admissionCancellation.IsCancellationRequested)
        {
            lock (_gate)
            {
                if (_lifetime.Token.IsCancellationRequested || _lifetime.Termination.IsCompleted || (_initialLoadingRetired && _initialLoadingRetirementScope == BedrockSessionScope.Session))
                    throw new BedrockSessionException(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
                if (operation is not null && !operation.Eligible)
                    ThrowOrdinaryMainInventoryRetirement(operation, exception);
                if (operation is not null && operation.OpenInterruptedByNormalWindow && GetCurrentOrdinaryNormalOwner() is not null)
                    throw new NormalInventoryOpenInterruptedException(exception);
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("The normal inventory opening was cancelled before transport capacity was granted.", exception, cancellationToken);
                throw new InvalidOperationException("The normal inventory opening was retired before transport capacity was granted.", exception);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_normalInventoryOpenAdmission, admissionCancellation))
                    _normalInventoryOpenAdmission = null;
            }
            admissionCancellation.Dispose();
            EndInventorySend();
        }
    }

    // Called under the Session gate. The active send retains the singleton until its own finally, even after grant.
    private void CancelNormalInventoryOpenAdmission()
    {
        CancellationTokenSource? admission = _normalInventoryOpenAdmission;
        admission?.Cancel();
    }

    /// <summary>Encodes and sends one movement-only protocol-2169 player-auth-input packet through serialized egress and records its payload for evidence.</summary>
    /// <remarks>Present-but-content tails are rejected rather than encoded, ticks must increase strictly with an explicit wrap from ulong.MaxValue to zero, the session must be ready and live, and sending records no server acceptance.</remarks>
    internal async ValueTask<BedrockPlayerAuthInputSubmitResult> SubmitPlayerAuthInputAsync(BedrockPlayerAuthInputPacket input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload;
        try
        {
            payload = BedrockPlayerAuthInputCodecs.Encode(input);
        }
        catch (BedrockCodecException exception)
        {
            throw new InvalidOperationException($"The player auth input is not a supported movement-only packet: {exception.Message}.", exception);
        }

        CancellationTokenSource admissionCancellation;
        lock (_gate)
        {
            RequirePlayerAuthInputSession();
            admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _playerAuthInputAdmissionLifetime.Token);
        }

        using (admissionCancellation)
        {
            bool enteredEgress = false;
            try
            {
                await _playerAuthInputEgress.WaitAsync(admissionCancellation.Token).ConfigureAwait(false);
                enteredEgress = true;
                lock (_gate)
                {
                    admissionCancellation.Token.ThrowIfCancellationRequested();
                    RequirePlayerAuthInputSession();
                    RequirePlayerAuthInputTick(input.Tick);
                }

                var member = new BedrockBatchMember(
                    new BedrockPacketHeader(144, new BedrockSubclientId(0), new BedrockSubclientId(0)),
                    payload);
                // Cancellation ends at the transport reservation grant; its token-free commit owns the eventual result.
                MessageAdmissionResult admission = await _connection.SendBatchAsync([member], admissionCancellation.Token).ConfigureAwait(false);
                if (!admission.OwnershipTransferred)
                {
                    lock (_gate)
                        _rejectedSendAttempts++;
                    throw new InvalidOperationException($"The player auth input was rejected with {admission.Status}.");
                }

                RecordOutbound(144);
                lock (_gate)
                {
                    _hasSentPlayerAuthInput = true;
                    _lastPlayerAuthInputTick = input.Tick;
                    _playerAuthInputSends++;
                }

                return new BedrockPlayerAuthInputSubmitResult(input.Tick, (byte[])payload.Clone());
            }
            catch (OperationCanceledException exception) when (admissionCancellation.IsCancellationRequested)
            {
                lock (_gate)
                {
                    // Cancellation callbacks can resume inline before the lifetime completion task is published.
                    if (_lifetime.Termination.IsCompleted || (_initialLoadingRetired && _initialLoadingRetirementScope == BedrockSessionScope.Session))
                        throw new BedrockSessionException(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException("The player auth input was cancelled before transport capacity was granted.", exception, cancellationToken);
                    throw new InvalidOperationException("The player auth input was retired before transport capacity was granted.", exception);
                }
            }
            finally
            {
                if (enteredEgress)
                    _playerAuthInputEgress.Release();
            }
        }
    }

    private void RequirePlayerAuthInputSession()
    {
        if (!_snapshot.IsReady)
            throw new InvalidOperationException("The player auth input requires a ready session.");
        if (_disposed || _lifetime.Termination.IsCompleted || _lifetime.Token.IsCancellationRequested || (_initialLoadingRetired && _initialLoadingRetirementScope == BedrockSessionScope.Session))
            throw new InvalidOperationException("The player auth input requires a live session.");
        if (_initialLoadingRetired)
            throw new InvalidOperationException("Movement recovery after player-life or world retirement is not supported.");
    }

    /// <summary>Requires each protocol-2169 auth-input tick to exceed the previous one with an explicit wrap from ulong.MaxValue to zero, where the first send establishes the baseline.</summary>
    private void RequirePlayerAuthInputTick(ulong tick)
    {
        if (!_hasSentPlayerAuthInput)
            return;
        if (_lastPlayerAuthInputTick == ulong.MaxValue)
        {
            if (tick != 0)
                throw new InvalidOperationException("The player auth input tick must wrap to zero after ulong.MaxValue.");
        }
        else if (tick <= _lastPlayerAuthInputTick)
        {
            throw new InvalidOperationException($"The player auth input tick {tick} must exceed {_lastPlayerAuthInputTick}.");
        }
    }

    internal async ValueTask<BedrockSessionSnapshot> WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        Task ready = _lifetime.Ready;
        if (ready.IsCompletedSuccessfully)
            return Snapshot;

        await Task.WhenAny(ready, _lifetime.Termination).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (ready.IsCompletedSuccessfully)
            return Snapshot;

        throw new BedrockSessionException(await _lifetime.Termination.ConfigureAwait(false));
    }

    internal async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        TryCompleteSession(new BedrockTermination(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session));
        try
        {
            await _transport.CloseAsync(_settings.Timeouts.CloseDrain, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _runTask.ConfigureAwait(false);
        _terrainWork.Writer.TryComplete();
        await _terrainWorker.ConfigureAwait(false);
        DrainAbandonedTerrainWork();
        TeardownInventory();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        TryCompleteSession(new BedrockTermination(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session, "The session was disposed."));
        // Terminal retirement cancels capacity waiters; all granted inventory requests and opening interactions finish accounting before owned teardown.
        Task inventorySendsDrained;
        lock (_gate)
            inventorySendsDrained = _activeInventorySends == 0 ? Task.CompletedTask : _inventorySendsDrained!.Task;
        await inventorySendsDrained.ConfigureAwait(false);
        // Waiting movement admissions are canceled first; a granted token-free commit finishes accounting before owned teardown.
        await _playerAuthInputEgress.WaitAsync().ConfigureAwait(false);
        _playerAuthInputEgress.Release();
        _transport.Abort(MessageConnectionTermination.Cancelled());
        _terrainWork.Writer.TryComplete();
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await _terrainWorker.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        DrainAbandonedTerrainWork();
        TeardownInventory();
        await _transport.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
        _key.Dispose();
        _playerAuthInputAdmissionLifetime.Dispose();
        _lifetime.Dispose();
    }

    private bool TryCompleteSession(BedrockTermination termination)
    {
        // Terminal ownership and initial readiness publication share the session gate.
        lock (_gate)
        {
            return _lifetime.TryComplete(termination, beforeCompletion: () =>
            {
                RetireInitialLoading(BedrockSessionScope.Session);
                TeardownInventory();
                _snapshot = _snapshot with { Termination = termination };
            }, beforeCancellation: () =>
            {
                // The lifetime CAS already chose this exact winner. Its following linked token cancellation retires capacity waiters.
                RetireObservedOakDropOwner(termination, cancelSend: false);
            });
        }
    }

    private async Task RunAsync()
    {
        try
        {
            ApplyEvent(BedrockBootstrapEvent.Connected());
            await SendAsync(193, BedrockBootstrapCodecs.Encode(new RequestNetworkSettingsPacket(2169))).ConfigureAwait(false);
            while (true)
            {
                List<object> batch = [];
                bool received = await ReceiveBatchAsync(batch).ConfigureAwait(false);
                if (!received)
                    throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.PeerClosed, BedrockSessionScope.Session));

                for (int index = 0; index < batch.Count; index++)
                    await ApplyInboundAsync(batch[index], batch.Count - index - 1).ConfigureAwait(false);
            }
        }
        catch (BedrockJoinTerminalException exception)
        {
            TryCompleteSession(exception.Termination);
        }
        catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
        {
            TryCompleteSession(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
        }
        catch (BedrockCodecException exception)
        {
            TryCompleteSession(new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, exception.Message, exception));
        }
        catch (Exception exception)
        {
            TryCompleteSession(new BedrockTermination(BedrockSessionEndReason.TransportFailure, BedrockSessionScope.Session, exception.Message, exception));
        }
        finally
        {
            lock (_gate)
                _snapshot = _snapshot with { Termination = _lifetime.Termination.IsCompletedSuccessfully ? _lifetime.Termination.Result : _snapshot.Termination };
        }
    }

    private async Task<bool> ReceiveBatchAsync(List<object> batch)
    {
        TimeProvider timeProvider = _settings.TimeProvider;
        TimeSpan remaining = _deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.Timeout, BedrockSessionScope.Session, $"The join phase '{_phase}' timed out."));

        IBedrockBatchConsumer consumer;
        bool disconnectObserved = false;
        lock (_gate)
            consumer = _snapshot.IsReady
                ? new BedrockActorObservationConsumer(header => RecordInbound(header.PacketId), (member, context) =>
                {
                    // Framing has already been validated; bodies after a valid disconnect have no further semantic application.
                    if (disconnectObserved)
                        return;
                    if (member.Header.PacketId == 5)
                    {
                        if (Bedrock2169CodecRegistry.Decode(member, context) is not DisconnectPacket disconnect)
                            throw new BedrockCodecException("The disconnect packet did not decode to its declared form.");
                        batch.Add(new ObservedDisconnect(disconnect));
                        disconnectObserved = true;
                        return;
                    }

                    ApplyPostJoinObservation(member, context);
                })
                : new BedrockPacketDispatcher((packet, _) => batch.Add(packet), ObserveAdmittedPreReady);

        Task<bool> receiveTask = _connection.ReceiveAndDispatchAsync(consumer, _lifetime.Token).AsTask();
        try
        {
            bool received = await receiveTask.WaitAsync(remaining, timeProvider).ConfigureAwait(false);
            if (consumer is BedrockPacketDispatcher)
            {
                lock (_gate)
                {
                    BedrockItemRegistryPacket? actual = batch.OfType<BedrockItemRegistryPacket>().LastOrDefault();
                    if (_snapshot.IsReady && actual is not null)
                    {
                        _creativeRegistryRetired = true;
                        RetireCreativeCursorQualification();
                    }
                    else if (!_snapshot.IsReady)
                    {
                        _creativeInitialItems = actual ?? _creativeInitialItems;
                        // Connection has completed every publication in this batch; the callback's pre-publication context cannot supply this owner.
                        _creativeRegistryOwner = _connection.Context.Registries;
                    }
                    _creativeRegistryPublicationPending = false;
                }
            }
            return received;
        }
        catch (TimeoutException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.Timeout, BedrockSessionScope.Session, $"The join phase '{_phase}' timed out.", exception));
        }
    }

    private async ValueTask ApplyInboundAsync(object packet, int remainingAfterCurrent)
    {
        if (packet is ObservedDisconnect observed)
            packet = observed.Packet;
        else
            RecordInbound(InboundPacketId(packet));
        switch (packet)
        {
            case NetworkSettingsPacket networkSettings:
                ApplyEvent(BedrockBootstrapEvent.NetworkSettingsAccepted());
                await InstallCompressionAsync(networkSettings).ConfigureAwait(false);
                await SendLoginAsync().ConfigureAwait(false);
                break;
            case ServerToClientHandshakePacket handshake:
                ApplyEvent(BedrockBootstrapEvent.ServerHandshakeValidated());
                await InstallEncryptionAsync(handshake).ConfigureAwait(false);
                break;
            case PlayStatusPacket status:
                await ApplyPlayStatusAsync(status, remainingAfterCurrent).ConfigureAwait(false);
                break;
            case ResourcePacksInfoPacket info:
                ApplyEvent(BedrockBootstrapEvent.ResourcePackOfferReceived());
                await RespondToResourceInfoAsync(info).ConfigureAwait(false);
                break;
            case ResourcePackStackPacket stack:
                await RespondToResourceStackAsync(stack).ConfigureAwait(false);
                break;
            case StartGamePacket start:
                BedrockStartGameTrailer startTrailer = BedrockStartGameTrailerCodecs.DecodeTrailer(start.Remainder);
                ImmutableArray<BedrockBootstrapEffect> startEffects = ApplyEvent(BedrockBootstrapEvent.StartGame(start.RuntimeEntityId));
                lock (_gate)
                    _snapshot = _snapshot with { LocalEntityId = start.EntityId, LocalRuntimeEntityId = start.RuntimeEntityId };
                _actors.SetLocalPlayer(start.EntityId, start.RuntimeEntityId);
                ApplyStartGameTerrain(startTrailer);
                CaptureStartGame(start);
                await SendInitialLoadingAsync(BedrockLoadingScreenType.StartLoadingScreen).ConfigureAwait(false);
                await EmitInitializationAsync(startEffects, remainingAfterCurrent).ConfigureAwait(false);
                break;
            case BedrockItemRegistryPacket:
                ApplyEvent(BedrockBootstrapEvent.ItemRegistryPublished());
                break;
            case AvailableEntityIdentifiersPacket:
                ApplyEvent(BedrockBootstrapEvent.EntityRegistryPublished());
                break;
            case BiomeDefinitionListPacket:
                ApplyEvent(BedrockBootstrapEvent.BiomeRegistryPublished());
                break;
            case ChunkRadiusUpdatePacket radius:
                if (radius.Radius is < 0 or > 64)
                    throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, $"The chunk radius {radius.Radius} is outside the supported range."));
                break;
            case BedrockAddEntityPacket
                or BedrockAddPlayerPacket
                or BedrockAddItemEntityPacket
                or BedrockRemoveEntityPacket
                or BedrockMovePlayerPacket
                or BedrockMobEffectPacket
                or BedrockUpdateAttributesPacket
                or BedrockMobEquipmentPacket
                or BedrockMobArmorEquipmentPacket
                or BedrockSetEntityDataPacket
                or BedrockSetEntityMotionPacket
                or BedrockSetEntityLinkPacket
                or BedrockPlayerLocationPacket:
                ApplyActorObservation(packet, null);
                break;
            case LevelChunkPacket chunk:
                CaptureLevelChunk(chunk);
                EnqueueTerrain(new LevelChunkWorkItem(chunk, _world.WorldGeneration));
                OnLevelChunk();
                break;
            case BedrockUpdateBlockPacket update:
                CaptureUpdateBlockDecoded(update);
                EnqueueTerrain(new UpdateBlockWorkItem(update, _world.WorldGeneration));
                break;
            case BedrockUpdateSubChunkBlocksPacket subChunkBlocks:
                EnqueueTerrain(new UpdateSubChunkBlocksWorkItem(subChunkBlocks, _world.WorldGeneration));
                break;
            case BedrockInventoryContentPacket content:
                ApplyInventoryContent(content);
                break;
            case BedrockInventorySlotPacket slot:
                ApplyInventorySlot(slot);
                break;
            case BedrockContainerOpenPacket open:
                ApplyContainerOpen(open);
                break;
            case BedrockContainerClosePacket close:
                ApplyContainerClose(close);
                break;
            case BedrockPlayerHotbarPacket hotbar:
                ApplyPlayerHotbar(hotbar);
                break;
            case BedrockInventoryTransactionPacket transaction:
                ObserveInventoryTransaction(transaction);
                break;
            case BedrockItemStackRequestPacket:
                throw new BedrockJoinTerminalException(new BedrockTermination(
                    BedrockSessionEndReason.ProtocolFailure,
                    BedrockSessionScope.Session,
                    "Packet 147 is serverbound but was received by the client."));
            case BedrockItemStackResponsePacket response:
                ApplyItemStackResponse(response);
                break;
            case BedrockCreativeContentPacket creative:
                ApplyCreativeContent(creative);
                break;
            case BedrockToleratedPacket tolerated:
                if (tolerated.PacketId == 45)
                    ApplyPendingPreReadyRespawn();
                break;
            case DisconnectPacket disconnect:
                throw new BedrockJoinTerminalException(new BedrockTermination(
                    BedrockSessionEndReason.RemoteDisconnect,
                    BedrockSessionScope.Session,
                    SanitizeDisconnect(disconnect)));
            case BedrockPacketViolationWarningPacket violation:
                lock (_gate)
                {
                    if (_capturedViolations.Count < MaximumCapturedInventoryPackets)
                        _capturedViolations.Add(violation);
                }

                if (violation.Severity != BedrockViolationSeverity.Warning)
                {
                    throw new BedrockJoinTerminalException(new BedrockTermination(
                        BedrockSessionEndReason.ProtocolFailure,
                        BedrockSessionScope.Session,
                        $"The server reported a {violation.Severity} violation for packet {violation.PacketId}: {violation.Reason}."));
                }

                break;
            default:
                throw new BedrockJoinTerminalException(new BedrockTermination(
                    BedrockSessionEndReason.ProtocolFailure,
                    BedrockSessionScope.Session,
                    $"The join path does not handle packet {packet.GetType().Name}."));
        }

        MaybeAcceptInitialization(InboundPacketId(packet));
        await CompleteInitialHeadlessLoadingAsync().ConfigureAwait(false);
        RefreshSnapshot();
    }

    private async ValueTask ApplyPlayStatusAsync(PlayStatusPacket status, int remainingAfterCurrent)
    {
        switch (status.Status)
        {
            case PlayStatusCode.LoginSuccess:
                ApplyEvent(BedrockBootstrapEvent.LoginSucceeded());
                SetDeadline(_settings.Timeouts.ResourceNegotiation, "resource-negotiation");
                break;
            case PlayStatusCode.PlayerSpawn:
                ImmutableArray<BedrockBootstrapEffect> spawnEffects = ApplyEvent(BedrockBootstrapEvent.PlayerSpawnAuthorized());
                await EmitInitializationAsync(spawnEffects, remainingAfterCurrent).ConfigureAwait(false);
                break;
            case PlayStatusCode.FailedClient:
            case PlayStatusCode.FailedSpawn:
            case PlayStatusCode.FailedInvalidTenant:
            case PlayStatusCode.FailedVanillaEducation:
            case PlayStatusCode.FailedEducationVanilla:
            case PlayStatusCode.FailedServerFull:
            case PlayStatusCode.FailedEditorVanillaMismatch:
            case PlayStatusCode.FailedVanillaEditorMismatch:
                throw new BedrockJoinTerminalException(new BedrockTermination(
                    BedrockSessionEndReason.RemoteDisconnect,
                    BedrockSessionScope.Session,
                    $"The server rejected the join with status {status.Status}."));
            default:
                throw new BedrockJoinTerminalException(new BedrockTermination(
                    BedrockSessionEndReason.ProtocolFailure,
                    BedrockSessionScope.Session,
                    $"The PlayStatus value {(int)status.Status} is not supported."));
        }
    }

    private async ValueTask InstallCompressionAsync(NetworkSettingsPacket networkSettings)
    {
        if (networkSettings.CompressionAlgorithm != BedrockCompressionAlgorithm.Deflate)
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure,
                BedrockSessionScope.Session,
                $"The compression algorithm {networkSettings.CompressionAlgorithm} is not supported."));

        await _connection.EnableCompressionAsync(
            new BedrockCompressionSettings(networkSettings.CompressionThreshold, networkSettings.CompressionAlgorithm),
            _lifetime.Token).ConfigureAwait(false);
    }

    private async ValueTask SendLoginAsync()
    {
        BedrockClientSkinProfile skin = _settings.SkinProfile ?? BedrockDefaultSkin.Profile;
        var request = new BedrockCredentialRequest(
            _settings.CredentialProvider.Mode,
            _settings.Protocol,
            $"{_settings.Host}:{_settings.Port}",
            null);
        SetDeadline(_settings.Timeouts.Credentials, "credentials");
        BedrockLoginArtifactResult artifacts;
        try
        {
            var factory = new BedrockLoginArtifactFactory(_settings.TimeProvider);
            artifacts = await factory.CreateAsync(request, _settings.CredentialProvider, _key, skin, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
        }

        if (!artifacts.IsSuccess)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure,
                BedrockSessionScope.Session,
                artifacts.Failure?.Message ?? "Credential acquisition failed."));
        }

        ApplyEvent(BedrockBootstrapEvent.CredentialsReady());
        await SendAsync(1, BedrockBootstrapCodecs.Encode(new LoginPacket(2169, artifacts.Artifacts!.IdentityEnvelope, artifacts.Artifacts.ClientDataToken))).ConfigureAwait(false);
        SetDeadline(_settings.Timeouts.LoginSuccess, "login-success");
    }

    private async ValueTask InstallEncryptionAsync(ServerToClientHandshakePacket handshake)
    {
        BedrockHandshakeResult result;
        try
        {
            result = BedrockHandshakeProcessor.Process(handshake.Token, _key);
        }
        catch (BedrockAuthenticationException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, exception.Message, exception));
        }

        using (result)
        {
            (BedrockEncryptionState send, BedrockEncryptionState receive) = result.CreateEncryptionStates();
            try
            {
                await _connection.EnableEncryptionAsync(send, receive, _lifetime.Token).ConfigureAwait(false);
            }
            catch
            {
                send.Dispose();
                receive.Dispose();
                throw;
            }
        }

        await SendAsync(4, BedrockBootstrapCodecs.Encode(new ClientToServerHandshakePacket())).ConfigureAwait(false);
    }

    private async ValueTask RespondToResourceInfoAsync(ResourcePacksInfoPacket info)
    {
        BedrockResourcePackDecision decision = await DecidePacksAsync(info.MustAccept, info.Packs.Count, info.Packs.Select(static pack => (pack.PackId.ToString(), (long)pack.Size))).ConfigureAwait(false);
        ResourcePackResponseStatus status = decision.Kind switch
        {
            BedrockResourcePackDecisionKind.Complete => ResourcePackResponseStatus.Completed,
            BedrockResourcePackDecisionKind.RefuseOptionalContent => ResourcePackResponseStatus.Refused,
            _ => throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, decision.Reason ?? "The resource-pack offer is not supported.")),
        };
        await SendAsync(8, BedrockJoinCodecs.Encode(new ResourcePackClientResponsePacket(status, []))).ConfigureAwait(false);
        await SendCacheAndRadiusAsync().ConfigureAwait(false);
        ApplyEvent(BedrockBootstrapEvent.ResourceNegotiationCompleted());
    }

    private async ValueTask RespondToResourceStackAsync(ResourcePackStackPacket stack)
    {
        BedrockResourcePackDecision decision = await DecidePacksAsync(stack.MustAccept, stack.Packs.Count, stack.Packs.Select(static pack => (pack.PackId, 0L))).ConfigureAwait(false);
        ResourcePackResponseStatus status = decision.Kind switch
        {
            BedrockResourcePackDecisionKind.Complete => ResourcePackResponseStatus.Completed,
            BedrockResourcePackDecisionKind.RefuseOptionalContent => ResourcePackResponseStatus.Refused,
            _ => throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, decision.Reason ?? "The resource-pack stack is not supported.")),
        };
        await SendAsync(8, BedrockJoinCodecs.Encode(new ResourcePackClientResponsePacket(status, []))).ConfigureAwait(false);
        bool completed;
        lock (_gate)
            completed = _bootstrap.ResourcesComplete;
        if (completed)
            return;

        await SendCacheAndRadiusAsync().ConfigureAwait(false);
        ApplyEvent(BedrockBootstrapEvent.ResourceNegotiationCompleted());
    }

    private async ValueTask<BedrockResourcePackDecision> DecidePacksAsync(bool required, int count, IEnumerable<(string PackId, long Size)> packs)
    {
        var descriptors = new List<BedrockResourcePackDescriptor>();
        foreach ((string packId, long size) in packs)
            descriptors.Add(new BedrockResourcePackDescriptor(packId, Math.Max(0, size), []));

        try
        {
            return await _settings.ResourcePackPolicy.DecideAsync(
                new BedrockResourcePackOffer(required, descriptors), _lifetime.Token).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, exception.Message, exception));
        }
    }

    private async ValueTask SendCacheAndRadiusAsync()
    {
        if (!_cacheStatusSent)
        {
            _cacheStatusSent = true;
            await SendAsync(129, BedrockBootstrapCodecs.Encode(new ClientCacheStatusPacket(false))).ConfigureAwait(false);
        }

        await SendAsync(69, BedrockBootstrapCodecs.Encode(new RequestChunkRadiusPacket(
            _settings.RequestedChunkRadius,
            checked((byte)Math.Min(_settings.RequestedChunkRadius, 32))))).ConfigureAwait(false);
        SetDeadline(_settings.Timeouts.Ready, "ready");
    }

    private async ValueTask EmitInitializationAsync(ImmutableArray<BedrockBootstrapEffect> effects, int remainingAfterCurrent)
    {
        foreach (BedrockBootstrapEffect effect in effects)
        {
            if (effect.Kind == BedrockBootstrapEffectKind.SendLocalPlayerInitialized)
            {
                await SendInitialBootstrapAsync(113, BedrockBootstrapCodecs.Encode(new SetLocalPlayerAsInitializedPacket(effect.RuntimeEntityId))).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_initialLoadingRetired || _lifetime.Token.IsCancellationRequested)
                        throw InitialLoadingRetired();
                    RequireInitialLoadingDeadline();
                    _initializationSendSequence = _inboundSequence + remainingAfterCurrent;
                }
            }
        }
    }

    private void ApplyActorObservation(object packet, byte[]? payload)
    {
        bool registryInstalled;
        int worldGeneration;
        lock (_gate)
        {
            registryInstalled = _bootstrap.EntityRegistryAvailable;
            worldGeneration = _actors.WorldGeneration;
        }

        try
        {
            BedrockActorObservationApplier.Apply(_actors, packet, payload, worldGeneration, registryInstalled);
        }
        catch (InvalidOperationException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure,
                BedrockSessionScope.Session,
                $"The actor observation cannot be retained: {exception.Message}.",
                exception));
        }

        if (packet is BedrockMovePlayerPacket move)
        {
            CaptureMovePlayer(move);
            lock (_gate)
                _movePlayerObservations++;
        }
        else if (packet is BedrockSetEntityMotionPacket motion)
        {
            CaptureSetEntityMotion(motion);
            lock (_gate)
                _setEntityMotionObservations++;
        }

        RefreshSnapshot();
    }

    private void ApplyInventoryContent(BedrockInventoryContentPacket packet)
    {
        CaptureInventoryContent(packet);
        try
        {
            lock (_gate)
            {
                NoteInventoryWindow(packet.Container);
                _inventory.ApplyContent(packet, _inventory.Generation);
                if (packet.Container == new BedrockContainerAddress(0, 0, null))
                    RetireObservedOakDropSource();
                if (packet.Container == s_creativeCursorPool)
                {
                    _creativeCursorBaselineQualified = packet.Items.Length == 54 && packet.Items[0].Equals(new BedrockItemInstance(0, 0, 0, null, 0, []));
                    RetireChangedCreativeCursorRequests();
                }
                if (packet.Container == s_creativeCursorPool || packet.Container == new BedrockContainerAddress(0, 0, null))
                    RetireChangedCursorPlaceRequests();
                if (packet.Container == s_creativeCursorPool)
                    RefreshCursorPlaceSourceQualification(protectReceipt: true);
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure,
                BedrockSessionScope.Session,
                $"The inventory content cannot be retained: {exception.Message}.",
                exception));
        }

        RefreshSnapshot();
    }

    private void ApplyInventorySlot(BedrockInventorySlotPacket packet)
    {
        CaptureInventorySlot(packet);
        try
        {
            BedrockContainerAddress? address;
            lock (_gate)
            {
                if (packet.Container.HasValue)
                {
                    NoteInventoryWindow(packet.Container.Value);
                    address = packet.Container.Value;
                }
                else if (_inventoryWindows.TryGetValue(packet.WindowId, out HashSet<BedrockContainerAddress>? addresses) && addresses.Count == 1)
                {
                    address = null;
                    foreach (BedrockContainerAddress candidate in addresses)
                    {
                        address = candidate;
                        break;
                    }
                }
                else
                {
                    address = null;
                }
            }

            if (!address.HasValue)
            {
                CountUnresolvedInventorySlot();
                RefreshSnapshot();
                return;
            }

            lock (_gate)
            {
                _inventory.ApplySlot(packet, address.Value, _inventory.Generation);
                if ((packet.Container ?? address.Value) == new BedrockContainerAddress(0, 0, null) && packet.Slot == 0)
                    RetireObservedOakDropSource();
                if ((packet.Container ?? address.Value) == s_creativeCursorPool)
                    RetireChangedCreativeCursorRequests();
                if ((packet.Container ?? address.Value) == s_creativeCursorPool || (packet.Container ?? address.Value) == new BedrockContainerAddress(0, 0, null))
                    RetireChangedCursorPlaceRequests();
                if ((packet.Container ?? address.Value) == s_creativeCursorPool && packet.Slot == 0)
                    RefreshCursorPlaceSourceQualification(protectReceipt: true);
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure,
                BedrockSessionScope.Session,
                $"The inventory slot cannot be retained: {exception.Message}.",
                exception));
        }

        RefreshSnapshot();
    }

    private void NoteInventoryWindow(BedrockContainerAddress address)
    {
        if (!_inventoryWindows.TryGetValue(address.WindowId, out HashSet<BedrockContainerAddress>? addresses))
        {
            if (_inventoryWindows.Count >= MaximumInventoryWindows)
                throw new InvalidOperationException("The inventory session holds too many windows.");
            addresses = [];
            _inventoryWindows[address.WindowId] = addresses;
        }

        if (!addresses.Contains(address) && addresses.Count >= MaximumAddressesPerInventoryWindow)
            throw new InvalidOperationException($"The inventory window {address.WindowId} holds too many container addresses.");
        addresses.Add(address);
    }

    private void CountUnresolvedInventorySlot()
    {
        lock (_gate)
            _unresolvedInventorySlots++;
    }

    private void ApplyContainerOpen(BedrockContainerOpenPacket packet)
    {
        CaptureContainerOpen(packet);
        lock (_gate)
        {
            bool normal = packet.WindowType == -1 && packet.WindowId is >= 1 and <= 99;
            if (_creativeScreenObserved || !normal)
                RetireCreativeCursorQualification();
            _creativeScreenObserved = true;
            _creativeScreen = normal ? new(packet.WindowId, packet.WindowType, packet.X, packet.Y, packet.Z, packet.EntityUniqueId) : null;
            _creativeScreenLifetime = normal ? new object() : null;
            _containerWindows.NoteOpen(packet, _containerWindows.Generation);
            PublishOrdinaryNormalOwner(packet);
            CancelNormalInventoryOpenAdmission();
        }
        RefreshSnapshot();
    }

    private void ApplyContainerClose(BedrockContainerClosePacket packet)
    {
        CaptureContainerClose(packet);
        // Close binds pending detection, pending clear, coordinator retirement, and egress retirement under the session gate so a concurrent submit cannot slip between them.
        lock (_gate)
        {
            bool droppedPending = _itemStackTracker.PendingCount > 0;
            if (droppedPending)
                _itemStackEgressRetired = true;
            bool known = _containerWindows.TryClose(packet.WindowId, _containerWindows.Generation);
            _inventoryWindows.Remove(packet.WindowId);
            _inventory.RemoveWindow(packet.WindowId);
            if (packet.WindowId == 0)
                RetireObservedOakDropOwner();
            bool retireCreative = _creativeScreen?.WindowId == packet.WindowId || _creativeCursorReconciliations.Count != 0 || _cursorPlaceReconciliations.Count != 0;
            if (_creativeScreen?.WindowId == packet.WindowId)
            {
                _creativeScreen = null;
                _creativeScreenLifetime = null;
            }
            RetireOrdinaryMainInventoryOperation();
            if (retireCreative)
                RetireCreativeCursorQualification();
            _itemStackTracker.ClearPending();
            CancelItemStackAdmissions();
            _itemStackReconciliations.Clear();
            _actionCoordinator.Invalidate();
            if (!known)
                _duplicateContainerCloses++;
            CancelNormalInventoryOpenAdmission();
        }

        RefreshSnapshot();
    }

    private void ApplyPlayerHotbar(BedrockPlayerHotbarPacket packet)
    {
        CaptureHotbar(packet);
        lock (_gate)
        {
            _lastHotbar = packet;
            _hotbarObservations++;
        }

        RefreshSnapshot();
    }

    private void ApplyCreativeContent(BedrockCreativeContentPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (_lastCreativeContent is not null && !ReferenceEquals(_lastCreativeContent, packet))
                RetireCreativeCursorQualification();
            _lastCreativeContent = packet;
            _creativeContentObservations++;
        }

        RefreshSnapshot();
    }

    private void ObserveInventoryTransaction(BedrockInventoryTransactionPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        CaptureInventoryTransaction(packet);
        lock (_gate)
        {
            _lastInventoryTransaction = packet;
            _inventoryTransactionObservations++;
        }

        RefreshSnapshot();
    }

    private void ApplyItemStackResponse(BedrockItemStackResponsePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        try
        {
            lock (_gate)
            {
                IReadOnlyList<BedrockItemStackResponseOutcome> outcomes = _itemStackTracker.ApplyResponses(packet.Responses, _itemStackTracker.Generation);
                for (int index = 0; index < packet.Responses.Length; index++)
                {
                    BedrockItemStackResponse response = packet.Responses[index];
                    if (outcomes[index] is not (BedrockItemStackResponseOutcome.Accepted or BedrockItemStackResponseOutcome.Rejected))
                        continue;
                    if (_itemStackReconciliations.Remove(response.RequestId, out BedrockItemStackReconciliation? retained)
                        && outcomes[index] == BedrockItemStackResponseOutcome.Accepted)
                        _inventory.ApplyResponseUpdates(retained.Before, retained.Reconcile(response), retained.AffectedSlots, retained.Generation);
                    if (_creativeCursorReconciliations.Remove(response.RequestId, out BedrockCreativeCursorReconciliation? creative)
                        && outcomes[index] == BedrockItemStackResponseOutcome.Accepted && GetCreativeCursorOwnership() is { } current)
                    {
                        BedrockCreativeCursorReconciliationResult result = creative.Reconcile(response, current);
                        if (result.IsEligible && creative.IsCurrent(current))
                        {
                            _inventory.ApplyResponseUpdates(creative.Before, result.Updates, creative.AffectedSlots, creative.Generation);
                            RefreshCursorPlaceSourceQualification(protectReceipt: false);
                        }
                    }
                    if (_cursorPlaceReconciliations.Remove(response.RequestId, out BedrockCursorPlaceReconciliation? place)
                        && outcomes[index] == BedrockItemStackResponseOutcome.Accepted && GetCreativeCursorOwnership() is { } placeOwner)
                    {
                        BedrockCursorPlaceReconciliationResult result = place.Reconcile(response, placeOwner);
                        if (result.IsEligible && place.IsCurrent(placeOwner)
                            && _inventory.TryApplyCursorPlaceResponse(place.CursorBefore, place.PlayerBefore, result.CursorUpdates, result.PlayerUpdates, place.Generation))
                            RefreshCursorPlaceSourceQualification(protectReceipt: true);
                    }
                    _actionCoordinator.Release(response.RequestId);
                }
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.ProtocolFailure,
                BedrockSessionScope.Session,
                $"The item stack response cannot be retained: {exception.Message}.",
                exception));
        }

        RefreshSnapshot();
    }

    private void ObserveAdmittedPreReady(BedrockBatchMember member)
    {
        if (member.Header.PacketId == 162)
        {
            lock (_gate)
            {
                _creativeRegistryPublicationPending = true;
                if (_snapshot.IsReady)
                {
                    _creativeRegistryRetired = true;
                    RetireCreativeCursorQualification();
                }
            }
        }
        if (member.Header.PacketId == 48)
            CaptureHotbarPayload(member.Payload);
        if (member.Header.PacketId == 49)
            CaptureInventoryContentPayload(member.Payload);
        if (member.Header.PacketId == 50)
            CaptureInventorySlotPayload(member.Payload);
        if (member.Header.PacketId == 145)
            CaptureCreativeContentPayload(member.Payload);
        if (member.Header.PacketId == 30)
            CaptureInventoryTransactionPayload(member.Payload);
        if (member.Header.PacketId == 148)
            CaptureItemStackResponsePayload(member.Payload);
        if (member.Header.PacketId == 19)
            CaptureMovePlayerPayload(member.Payload);
        if (member.Header.PacketId == 40)
            CaptureSetEntityMotionPayload(member.Payload);
        if (member.Header.PacketId == 15)
            CaptureAddItemEntityPayload(member.Payload);
        if (member.Header.PacketId == 45)
            ObservePreReadyRespawn(member.Payload);
    }

    /// <summary>Decodes packet 45 before readiness and queues it in arrival order so one dequeued coordinate per ready transition can update the local actor after StartGame without seeding physics.</summary>
    private void ObservePreReadyRespawn(byte[] payload)
    {
        BedrockRespawnPacket packet;
        try
        {
            packet = BedrockRespawnCodecs.Decode(payload);
        }
        catch (BedrockCodecException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, $"Packet 45 (respawn) with {payload.Length} payload bytes failed to decode: {exception.Message}.", exception));
        }

        CaptureRespawnPayload(payload);
        lock (_gate)
        {
            if (_pendingPreReadyRespawns.Count >= MaximumPendingPreReadyRespawns)
                throw new BedrockCodecException($"The pre-ready respawn batch exceeds {MaximumPendingPreReadyRespawns} pending packets.");
            _pendingPreReadyRespawns.Enqueue(packet);
        }
    }

    private void ApplyPendingPreReadyRespawn()
    {
        BedrockRespawnPacket? packet = null;
        lock (_gate)
        {
            if (_pendingPreReadyRespawns.Count > 0)
                packet = _pendingPreReadyRespawns.Dequeue();
        }

        if (packet is null)
            return;
        ApplyRespawnPosition(packet);
    }

    private void ObservePostJoinRespawn(byte[] payload, BedrockCodecLimits limits)
    {
        BedrockRespawnPacket packet;
        try
        {
            packet = BedrockRespawnCodecs.Decode(payload, limits);
        }
        catch (BedrockCodecException exception)
        {
            throw new BedrockCodecException($"Packet 45 (respawn) with {payload.Length} payload bytes failed to decode: {exception.Message}.", exception);
        }

        CaptureRespawnPayload(payload);
        ApplyRespawnPosition(packet);
    }

    private void ApplyRespawnPosition(BedrockRespawnPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.State != 1)
            return;
        int worldGeneration = _actors.WorldGeneration;
        _actors.ApplyRespawnPosition(packet.Position, worldGeneration);
        RefreshSnapshot();
    }

    // Respawn ends the player life: transient container and request work is dropped, item-stack egress retires until reconnect, and the world stays current.
    internal void HandleRespawn()
    {
        RetireInitialLoading(BedrockSessionScope.PlayerLife);
        _actors.InvalidatePlayerLife();
        InvalidateInventoryTransient();
        RefreshSnapshot();
    }

    // A world replacement retires actors, terrain, transient inventory work, and item-stack egress together so late packets cannot land in the next world.
    internal void HandleWorldReplacement()
    {
        RetireInitialLoading(BedrockSessionScope.World);
        _actors.InvalidateWorld();
        _world.InvalidateWorld();
        lock (_gate)
        {
            _terrainRequestGeneration++;
            _subChunks.Invalidate(_terrainRequestGeneration);
            _requestedTerrainColumns.Clear();
            _pendingTerrainColumns.Clear();
        }

        InvalidateInventoryTransient();
        RefreshSnapshot();
    }

    private void InvalidateInventoryTransient()
    {
        // Lifecycle retirement holds the session gate across window retention, egress retirement, and every component invalidation so admission cannot interleave.
        lock (_gate)
        {
            RetireOrdinaryMainInventoryOperation();
            List<int> doomedWindows = [];
            foreach ((int windowId, HashSet<BedrockContainerAddress> addresses) in _inventoryWindows)
            {
                addresses.RemoveWhere(static address => address.WindowId != 0 || address.DynamicContainerId.HasValue);
                if (addresses.Count == 0)
                    doomedWindows.Add(windowId);
            }

            foreach (int windowId in doomedWindows)
                _inventoryWindows.Remove(windowId);
            _itemStackEgressRetired = true;
            RetireCreativeCursorQualification();
            _creativeScreen = null;
            _creativeScreenLifetime = null;
            ClearCursorPlaceQualification();
            _inventory.InvalidateTransient();
            _containerWindows.Invalidate();
            _itemStackTracker.Invalidate();
            CancelItemStackAdmissions();
            _itemStackReconciliations.Clear();
            _actionCoordinator.Invalidate();
        }
    }

    private void TeardownInventory()
    {
        lock (_gate)
        {
            RetireOrdinaryMainInventoryOperation();
            if (_inventoryTornDown)
                return;
            _inventoryTornDown = true;
            RetireCreativeCursorQualification();
            _creativeScreen = null;
            _creativeScreenLifetime = null;
            _creativeInitialItems = null;
            _creativeRegistryOwner = null;
            _creativeRegistryRetired = true;
            _inventoryWindows.Clear();
            _unresolvedInventorySlots = 0;
            _duplicateContainerCloses = 0;
            _lastHotbar = null;
            _hotbarObservations = 0;
            _lastCreativeContent = null;
            _creativeContentObservations = 0;
            _lastInventoryTransaction = null;
            _inventoryTransactionObservations = 0;
            ClearCursorPlaceQualification();
            _inventory.Invalidate();
            _containerWindows.Invalidate();
            _itemStackTracker.Invalidate();
            CancelItemStackAdmissions();
            _itemStackReconciliations.Clear();
            _actionCoordinator.Invalidate();
        }
    }

    private void ApplyPostJoinObservation(BedrockBatchMember member, BedrockSessionCodecContext context)
    {
        if (member.Header.PacketId == 162)
        {
            _ = BedrockItemRegistryCodec.Decode(member.Payload, context.Limits);
            lock (_gate)
            {
                // The post-Ready consumer does not publish replacement registry owners; qualification therefore remains retired until reconnect.
                _ordinaryRegistryRetired = true;
                RetireOrdinaryMainInventoryOperation();
                _creativeRegistryRetired = true;
                RetireCreativeCursorQualification();
            }
            return;
        }
        if (member.Header.PacketId == 21)
        {
            CaptureUpdateBlockPayload(member.Payload);
            lock (_gate)
            {
                if (_terrainCaptureEnabled
                    && _capturedPostJoinUpdateBlocks.Count < MaximumCapturedPostJoinUpdateBlocks
                    && member.Payload.Length <= MaximumCapturedPayloadBytes)
                    _capturedPostJoinUpdateBlocks.Add((byte[])member.Payload.Clone());
            }

            EnqueueTerrain(new UpdateBlockWorkItem(BedrockUpdateBlockCodecs.DecodeUpdateBlock(member.Payload, context.Limits), _world.WorldGeneration));
            return;
        }

        if (member.Header.PacketId == 58)
        {
            if (Bedrock2169CodecRegistry.Decode(member, context) is not LevelChunkPacket chunk)
                throw new BedrockCodecException("The level chunk packet did not decode to its declared form.");
            CaptureLevelChunk(chunk);
            EnqueueTerrain(new LevelChunkWorkItem(chunk, _world.WorldGeneration));
            lock (_gate)
                _snapshot = _snapshot with { LevelChunksObserved = _snapshot.LevelChunksObserved + 1 };
            return;
        }

        if (member.Header.PacketId == 174)
        {
            long requestGeneration;
            lock (_gate)
            {
                requestGeneration = _terrainRequestGeneration;
                if (_terrainCaptureEnabled
                    && _capturedSubChunkResponses.Count < MaximumCapturedSubChunkResponses
                    && member.Payload.Length <= MaximumCapturedPayloadBytes)
                    _capturedSubChunkResponses.Add((byte[])member.Payload.Clone());
            }

            EnqueueTerrain(new SubChunkResponseWorkItem((byte[])member.Payload.Clone(), _world.WorldGeneration, requestGeneration));
            return;
        }

        if (member.Header.PacketId == 172)
        {
            BedrockUpdateSubChunkBlocksPacket subChunkBlocks = BedrockUpdateSubChunkBlocksCodecs.Decode(member.Payload, context.Limits);
            lock (_gate)
            {
                if (_terrainCaptureEnabled
                    && _capturedSubChunkBlocks.Count < MaximumCapturedSubChunkBlocks
                    && member.Payload.Length <= MaximumCapturedPayloadBytes)
                    _capturedSubChunkBlocks.Add((byte[])member.Payload.Clone());
            }

            EnqueueTerrain(new UpdateSubChunkBlocksWorkItem(subChunkBlocks, _world.WorldGeneration));
            return;
        }

        if (member.Header.PacketId == 49)
        {
            CaptureInventoryContentPayload(member.Payload);
            ApplyInventoryContent(BedrockInventoryCodecs.DecodeContent(member.Payload, context.Limits));
            return;
        }

        if (member.Header.PacketId == 50)
        {
            CaptureInventorySlotPayload(member.Payload);
            ApplyInventorySlot(BedrockInventoryCodecs.DecodeSlot(member.Payload, context.Limits));
            return;
        }

        if (member.Header.PacketId == 46)
        {
            if (Bedrock2169CodecRegistry.Decode(member, context) is not BedrockContainerOpenPacket open)
                throw new BedrockCodecException("The container open packet did not decode to its declared form.");
            ApplyContainerOpen(open);
            return;
        }

        if (member.Header.PacketId == 47)
        {
            if (Bedrock2169CodecRegistry.Decode(member, context) is not BedrockContainerClosePacket close)
                throw new BedrockCodecException("The container close packet did not decode to its declared form.");
            ApplyContainerClose(close);
            return;
        }

        if (member.Header.PacketId == 48)
        {
            CaptureHotbarPayload(member.Payload);
            if (Bedrock2169CodecRegistry.Decode(member, context) is not BedrockPlayerHotbarPacket hotbar)
                throw new BedrockCodecException("The player hotbar packet did not decode to its declared form.");
            ApplyPlayerHotbar(hotbar);
            return;
        }

        if (member.Header.PacketId == 30)
        {
            CaptureInventoryTransactionPayload(member.Payload);
            if (Bedrock2169CodecRegistry.Decode(member, context) is not BedrockInventoryTransactionPacket transaction)
                throw new BedrockCodecException("The inventory transaction packet did not decode to its declared form.");
            ObserveInventoryTransaction(transaction);
            return;
        }

        if (member.Header.PacketId == 147)
            throw new BedrockCodecException("Packet 147 is serverbound but was received by the client.");

        if (member.Header.PacketId == 145)
        {
            CaptureCreativeContentPayload(member.Payload);
            ApplyCreativeContent(BedrockCreativeContentCodecs.Decode(member.Payload, context.Limits));
            return;
        }

        if (member.Header.PacketId == 148)
        {
            CaptureItemStackResponsePayload(member.Payload);
            if (Bedrock2169CodecRegistry.Decode(member, context) is not BedrockItemStackResponsePacket itemStackResponse)
                throw new BedrockCodecException("The item stack response packet did not decode to its declared form.");
            ApplyItemStackResponse(itemStackResponse);
            return;
        }

        if (member.Header.PacketId == 156)
        {
            if (Bedrock2169CodecRegistry.Decode(member, context) is not BedrockPacketViolationWarningPacket violation)
                throw new BedrockCodecException("The violation warning packet did not decode to its declared form.");
            lock (_gate)
            {
                if (_capturedViolations.Count < MaximumCapturedInventoryPackets)
                    _capturedViolations.Add(violation);
            }

            if (violation.Severity != BedrockViolationSeverity.Warning)
                throw new BedrockCodecException($"The server reported a {violation.Severity} violation for packet {violation.PacketId}: {violation.Reason}.");
            return;
        }

        if (member.Header.PacketId == 161)
        {
            CaptureMovePredictionPayload(member.Payload);
            ObserveMovePrediction(BedrockPlayerAuthInputCodecs.DecodePrediction(member.Payload, context.Limits));
            return;
        }

        if (member.Header.PacketId == 19)
            CaptureMovePlayerPayload(member.Payload);
        if (member.Header.PacketId == 40)
            CaptureSetEntityMotionPayload(member.Payload);
        if (member.Header.PacketId == 15)
            CaptureAddItemEntityPayload(member.Payload);
        if (member.Header.PacketId == 45)
        {
            ObservePostJoinRespawn(member.Payload, context.Limits);
            return;
        }

        if (!BedrockActorObservationApplier.IsActorPacket(member.Header.PacketId))
            return;
        object packet = Bedrock2169CodecRegistry.Decode(member, context);
        ApplyActorObservation(packet, member.Payload);
    }

    private void ApplyStartGameTerrain(BedrockStartGameTrailer trailer)
    {
        lock (_gate)
        {
            _terrainDimension ??= trailer.Dimension;
            _terrainHashes = trailer.BlockNetworkIdsAreHashes;
            _terrainChecksum = trailer.BlockPaletteChecksum;
        }
    }

    private void EnqueueTerrain(TerrainWorkItem item)
    {
        lock (_gate)
        {
            if (!_terrainWork.Writer.TryWrite(item))
                _terrainWorkDropped++;
        }
    }

    private async Task ProcessTerrainQueueAsync()
    {
        try
        {
            await foreach (TerrainWorkItem item in _terrainWork.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                try
                {
                    await ProcessTerrainItemAsync(item).ConfigureAwait(false);
                }
                catch (BedrockCodecException exception)
                {
                    TryCompleteSession(new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, exception.Message, exception));
                    return;
                }
                catch (InvalidOperationException exception)
                {
                    TryCompleteSession(new BedrockTermination(
                        BedrockSessionEndReason.ProtocolFailure,
                        BedrockSessionScope.Session,
                        $"The terrain observation cannot be retained: {exception.Message}.",
                        exception));
                    return;
                }
                catch (BedrockJoinTerminalException exception)
                {
                    TryCompleteSession(exception.Termination);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.Token.IsCancellationRequested)
        {
        }
    }

    private async ValueTask ProcessTerrainItemAsync(TerrainWorkItem item)
    {
        switch (item)
        {
            case LevelChunkWorkItem chunk:
                await ApplyLevelChunkAsync(chunk).ConfigureAwait(false);
                break;
            case SubChunkResponseWorkItem response:
                ApplySubChunkResponse(response);
                break;
            case UpdateBlockWorkItem update:
                ApplyUpdateBlock(update);
                break;
            case UpdateSubChunkBlocksWorkItem subChunkBlocks:
                ApplyUpdateSubChunkBlocks(subChunkBlocks);
                break;
            case ReadyFlushWorkItem:
                await FlushPendingTerrainRequestsAsync().ConfigureAwait(false);
                break;
            default:
                throw new BedrockCodecException($"The terrain work item {item.GetType().Name} is not supported.");
        }
    }

    private async ValueTask ApplyLevelChunkAsync(LevelChunkWorkItem item)
    {
        if (item.WorldGeneration != _world.WorldGeneration)
        {
            CountTerrainStale();
            return;
        }

        if (item.Packet.CacheEnabled || item.Packet.BlobHashes.Count != 0)
            throw new BedrockCodecException("The cache-enabled level chunk payload carries blob references instead of sub-chunk sections and is not supported.");
        if (item.Packet.SubChunkCount > BedrockLevelChunkAssembly.MaxSubChunkCount)
            throw new BedrockCodecException($"The level chunk section count {item.Packet.SubChunkCount} exceeds the supported maximum {BedrockLevelChunkAssembly.MaxSubChunkCount}.");
        if (item.Packet.Payload.Length > BedrockWorldStore.MaxSinglePayloadBytes)
            throw new InvalidOperationException("The level chunk payload exceeds the retained byte budget.");
        if (!BedrockDimensionTable.TryGet(item.Packet.Dimension, out _, out int height))
            throw new BedrockCodecException($"The chunk dimension {item.Packet.Dimension} is not a known Bedrock dimension.");
        int blockCount = checked((int)item.Packet.SubChunkCount);
        int biomeCount = blockCount > 0 ? blockCount : BedrockDimensionTable.SectionCount(height);
        BedrockLevelChunkTrailer trailer = BedrockLevelChunkTrailerCodecs.DecodeTrailer(item.Packet.Payload, blockCount, biomeCount);
        if (!_world.TryApplyTrailer(item.Packet.X, item.Packet.Z, item.Packet.Dimension, item.WorldGeneration, trailer, item.Packet.Payload.Length))
        {
            CountTerrainStale();
            return;
        }

        CountTerrainApplied();
        if (trailer.BlockSections.Count == 0)
            NotePendingTerrainColumn(item.Packet.Dimension, item.Packet.X, item.Packet.Z);
        await FlushPendingTerrainRequestsAsync().ConfigureAwait(false);
    }

    private void ApplySubChunkResponse(SubChunkResponseWorkItem item)
    {
        if (item.WorldGeneration != _world.WorldGeneration)
        {
            CountTerrainStale();
            return;
        }

        BedrockSubChunkResponsePacket response = BedrockSubChunkCodecs.DecodeResponse(item.Payload);
        BedrockSubChunkMatchResult match;
        lock (_gate)
            match = _subChunks.Match(response, item.RequestGeneration);
        lock (_gate)
        {
            foreach (BedrockSubChunkEntry entry in response.Entries)
            {
                string name = entry.Result.ToString();
                _subChunkResultCounts[name] = _subChunkResultCounts.TryGetValue(name, out int count) ? count + 1 : 1;
            }

            if (match.Unsolicited.Count != 0)
                _subChunkResultCounts["Unsolicited"] = _subChunkResultCounts.TryGetValue("Unsolicited", out int unsolicited) ? unsolicited + match.Unsolicited.Count : match.Unsolicited.Count;
        }

        if (match.Stale || match.Matched.Count == 0)
        {
            if (match.Stale)
                CountTerrainStale();
            return;
        }

        if (!_world.TryApplySubChunkEntries(response.Center, response.Dimension, item.WorldGeneration, item.RequestGeneration, match.Matched))
        {
            CountTerrainStale();
            return;
        }

        CountTerrainApplied();
    }

    private void ApplyUpdateBlock(UpdateBlockWorkItem item)
    {
        if (item.WorldGeneration != _world.WorldGeneration)
        {
            CountTerrainStale();
            return;
        }

        int dimension;
        lock (_gate)
            dimension = _terrainDimension ?? -1;
        if (dimension < 0)
            throw new BedrockCodecException("The update block arrived before the world dimension was known.");
        if (!_world.TryApplyUpdateBlock(item.Packet.X, item.Packet.Y, item.Packet.Z, dimension, item.WorldGeneration, item.Packet.BlockRuntimeId, item.Packet.Layer))
        {
            CountTerrainRefused();
            return;
        }

        CountTerrainApplied();
    }

    private void ApplyUpdateSubChunkBlocks(UpdateSubChunkBlocksWorkItem item)
    {
        if (item.WorldGeneration != _world.WorldGeneration)
        {
            CountTerrainStale();
            return;
        }

        int dimension;
        lock (_gate)
            dimension = _terrainDimension ?? -1;
        if (dimension < 0)
            throw new BedrockCodecException("The sub-chunk blocks arrived before the world dimension was known.");
        BedrockSubChunkBlocksApplyResult result = _world.TryApplySubChunkBlocks(dimension, item.WorldGeneration, item.Packet);
        AddTerrainTally(result.Applied, result.Refused);
    }

    private void NotePendingTerrainColumn(int dimension, int chunkX, int chunkZ)
    {
        lock (_gate)
        {
            if (_requestedTerrainColumns.Contains((dimension, chunkX, chunkZ)))
                return;
            if (_requestedTerrainColumns.Count + _pendingTerrainColumns.Count >= MaximumRequestedTerrainColumns)
                return;
            _pendingTerrainColumns.Add((dimension, chunkX, chunkZ));
        }
    }

    private async ValueTask FlushPendingTerrainRequestsAsync()
    {
        List<(int Dimension, int X, int Z)> due;
        long generation;
        lock (_gate)
        {
            if (!_snapshot.IsReady || _pendingTerrainColumns.Count == 0)
                return;
            due = [.. _pendingTerrainColumns];
            _pendingTerrainColumns.Clear();
            generation = _terrainRequestGeneration;
        }

        var batch = new List<BedrockSubChunkRequestPacket>(due.Count);
        foreach ((int dimension, int x, int z) in due)
        {
            BedrockSubChunkRequestPacket? request = BuildTerrainColumnRequest(dimension, x, z);
            if (request is null)
            {
                CountTerrainRefused();
                continue;
            }

            lock (_gate)
            {
                if (_requestedTerrainColumns.Contains((dimension, x, z)))
                    continue;
                if (!_subChunks.TryRegister(request, generation))
                {
                    CountTerrainRefused();
                    continue;
                }

                _requestedTerrainColumns.Add((dimension, x, z));
            }

            batch.Add(request);
        }

        if (batch.Count == 0)
            return;
        _world.NoteSubChunkRequest(generation);
        foreach (BedrockSubChunkRequestPacket request in batch)
        {
            await SendAsync(175, BedrockSubChunkCodecs.EncodeRequest(request)).ConfigureAwait(false);
            lock (_gate)
                _terrainRequestsSent++;
        }
    }

    private static BedrockSubChunkRequestPacket? BuildTerrainColumnRequest(int dimension, int chunkX, int chunkZ)
    {
        if (!BedrockDimensionTable.TryGet(dimension, out int minY, out int height))
            return null;
        int count = BedrockDimensionTable.SectionCount(height);
        if (count is <= 0 or > 64)
            return null;
        int minSection = BedrockDimensionTable.MinSectionY(minY);
        int centerY = minSection + count / 2;
        var offsets = new BedrockSubChunkOffset[count];
        for (int index = 0; index < offsets.Length; index++)
            offsets[index] = new BedrockSubChunkOffset(0, minSection + index - centerY, 0);
        return new BedrockSubChunkRequestPacket(dimension, offsets, new BedrockSubChunkPosition(chunkX, centerY, chunkZ));
    }

    private void DrainAbandonedTerrainWork()
    {
        lock (_gate)
        {
            while (_terrainWork.Reader.TryRead(out _))
                _terrainWorkDropped++;
        }
    }

    private void CountTerrainStale()
    {
        lock (_gate)
            _terrainStaleDropped++;
    }

    private void CountTerrainRefused()
    {
        lock (_gate)
            _terrainApplyRefused++;
    }

    private void CountTerrainApplied()
    {
        lock (_gate)
            _terrainApplied++;
    }

    private void AddTerrainTally(int applied, int refused)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(applied);
        ArgumentOutOfRangeException.ThrowIfNegative(refused);
        lock (_gate)
        {
            _terrainApplied += applied;
            _terrainApplyRefused += refused;
        }
    }

    private void CaptureInventoryContent(BedrockInventoryContentPacket packet)
    {
        if (!_inventoryCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedInventoryContents.Count < MaximumCapturedInventoryPackets)
                _capturedInventoryContents.Add(packet);
        }
    }

    private void CaptureInventorySlot(BedrockInventorySlotPacket packet)
    {
        if (!_inventoryCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedInventorySlots.Count < MaximumCapturedInventoryPackets)
                _capturedInventorySlots.Add(packet);
        }
    }

    private void CaptureInventoryContentPayload(byte[] payload)
    {
        if (!_inventoryCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedInventoryPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedInventoryContentPayloads.Count < MaximumCapturedInventoryPackets)
                _capturedInventoryContentPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureInventorySlotPayload(byte[] payload)
    {
        if (!_inventoryCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedInventoryPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedInventorySlotPayloads.Count < MaximumCapturedInventoryPackets)
                _capturedInventorySlotPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureCreativeContentPayload(byte[] payload)
    {
        if (!_creativeCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedCreativePayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedCreativeContentPayloads.Count < MaximumCapturedCreativePackets)
                _capturedCreativeContentPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureContainerOpen(BedrockContainerOpenPacket packet)
    {
        if (!_inventoryCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedContainerOpens.Count < MaximumCapturedInventoryPackets)
                _capturedContainerOpens.Add(packet);
        }
    }

    private void CaptureContainerClose(BedrockContainerClosePacket packet)
    {
        if (!_inventoryCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedContainerCloses.Count < MaximumCapturedInventoryPackets)
                _capturedContainerCloses.Add(packet);
        }
    }

    private void CaptureHotbar(BedrockPlayerHotbarPacket packet)
    {
        if (!_inventoryCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedHotbars.Count < MaximumCapturedInventoryPackets)
                _capturedHotbars.Add(packet);
        }
    }

    private void CaptureHotbarPayload(byte[] payload)
    {
        if (!_inventoryCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedInventoryPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedHotbarPayloads.Count < MaximumCapturedInventoryPackets)
                _capturedHotbarPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureInventoryTransaction(BedrockInventoryTransactionPacket packet)
    {
        if (!_inventoryCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedInventoryTransactions.Count < MaximumCapturedInventoryPackets)
                _capturedInventoryTransactions.Add(packet);
        }
    }

    private void CaptureInventoryTransactionPayload(byte[] payload)
    {
        if (!_inventoryCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedInventoryPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedInventoryTransactionPayloads.Count < MaximumCapturedInventoryPackets)
                _capturedInventoryTransactionPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureItemStackRequestPayload(byte[] payload)
    {
        if (!_inventoryCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedInventoryPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedItemStackRequestPayloads.Count < MaximumCapturedInventoryPackets)
                _capturedItemStackRequestPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureItemStackResponsePayload(byte[] payload)
    {
        if (!_inventoryCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedInventoryPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedItemStackResponsePayloads.Count < MaximumCapturedInventoryPackets)
                _capturedItemStackResponsePayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureMovePlayer(BedrockMovePlayerPacket packet)
    {
        if (!_motionCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedMovePlayers.Count < MaximumCapturedMotionPackets)
                _capturedMovePlayers.Add(packet);
        }
    }

    private void CaptureMovePlayerPayload(byte[] payload)
    {
        if (!_motionCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedMotionPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedMovePlayerPayloads.Count < MaximumCapturedMotionPackets)
                _capturedMovePlayerPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureSetEntityMotion(BedrockSetEntityMotionPacket packet)
    {
        if (!_motionCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedSetEntityMotions.Count < MaximumCapturedMotionPackets)
                _capturedSetEntityMotions.Add(packet);
        }
    }

    private void CaptureSetEntityMotionPayload(byte[] payload)
    {
        if (!_motionCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedMotionPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedSetEntityMotionPayloads.Count < MaximumCapturedMotionPackets)
                _capturedSetEntityMotionPayloads.Add((byte[])payload.Clone());
        }
    }

    /// <summary>Retains one decoded protocol-2169 correction packet as optional evidence without touching actor state or the simulation.</summary>
    /// <remarks>Retention applies only when motion capture is enabled and keeps at most 32 packets.</remarks>
    private void CaptureMovePrediction(BedrockCorrectPlayerMovePredictionPacket packet)
    {
        if (!_motionCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedMovePredictions.Count < MaximumCapturedMotionPackets)
                _capturedMovePredictions.Add(packet);
        }
    }

    private void CaptureMovePredictionPayload(byte[] payload)
    {
        if (!_motionCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedMotionPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedMovePredictionPayloads.Count < MaximumCapturedMotionPackets)
                _capturedMovePredictionPayloads.Add((byte[])payload.Clone());
        }
    }

    private void CaptureAddItemEntityPayload(byte[] payload)
    {
        if (!_actorCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedActorPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedAddItemEntityPayloads.Count < MaximumCapturedActorPackets)
                _capturedAddItemEntityPayloads.Add((byte[])payload.Clone());
        }
    }

    /// <summary>Retains a small optional copy of raw respawn bytes for exact-version evidence without affecting packet handling.</summary>
    private void CaptureRespawnPayload(byte[] payload)
    {
        if (!_respawnCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedRespawnPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedRespawnPayloads.Count < MaximumCapturedRespawnPackets)
                _capturedRespawnPayloads.Add((byte[])payload.Clone());
        }
    }

    /// <summary>Counts one decoded protocol-2169 correction packet and refreshes the snapshot without reconciling actor state or the simulation.</summary>
    /// <remarks>The packet is observed and counted only; no position, velocity, or simulation value changes here.</remarks>
    private void ObserveMovePrediction(BedrockCorrectPlayerMovePredictionPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        CaptureMovePrediction(packet);
        lock (_gate)
            _movePredictionObservations++;
        RefreshSnapshot();
    }

    private void CaptureStartGame(StartGamePacket packet)
    {
        if (!_terrainCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedStartGameRemainder is not null)
                return;
            if (packet.Remainder.Length > BedrockCodecLimits.Default.MaxByteArrayBytes)
                return;
            _capturedStartGameRemainder = (byte[])packet.Remainder.Clone();
        }
    }

    private void CaptureLevelChunk(LevelChunkPacket packet)
    {
        if (!_terrainCaptureEnabled)
            return;
        lock (_gate)
        {
            if (_capturedLevelChunkEnvelopes.Count < MaximumCapturedLevelChunkEnvelopes)
                _capturedLevelChunkEnvelopes.Add((packet.X, packet.Z, packet.Dimension, packet.SubChunkCount));
            if (_capturedLevelChunks.Count >= MaximumCapturedLevelChunks)
                return;
            if (packet.Payload.Length > MaximumCapturedPayloadBytes)
                return;
            _capturedLevelChunks.Add(new LevelChunkPacket(packet.X, packet.Z, packet.Dimension, packet.SubChunkCount, packet.HighestSubchunkCount, packet.CacheEnabled, packet.BlobHashes, packet.Payload));
        }
    }

    private void CaptureUpdateBlockDecoded(BedrockUpdateBlockPacket packet)
    {
        if (!_terrainCaptureEnabled)
            return;
        byte[] encoded = BedrockUpdateBlockCodecs.EncodeUpdateBlock(packet);
        CaptureUpdateBlockPayload(encoded);
    }

    private void CaptureUpdateBlockPayload(byte[] payload)
    {
        if (!_terrainCaptureEnabled)
            return;
        if (payload.Length > MaximumCapturedPayloadBytes)
            return;
        lock (_gate)
        {
            if (_capturedUpdateBlockPayload is not null)
                return;
            _capturedUpdateBlockPayload = (byte[])payload.Clone();
        }
    }

    private void OnLevelChunk()
    {
        lock (_gate)
            _snapshot = _snapshot with { LevelChunksObserved = _snapshot.LevelChunksObserved + 1 };
    }

    private void MaybeAcceptInitialization(int packetId)
    {
        if (packetId is 121 or 156)
            return;

        bool sent;
        bool accepted;
        ulong runtimeId;
        bool afterSend;
        lock (_gate)
        {
            sent = _bootstrap.LocalPlayerInitializationSent;
            accepted = _bootstrap.LocalPlayerInitializationAccepted;
            runtimeId = _bootstrap.LocalRuntimeEntityId;
            afterSend = _initializationSendSequence >= 0 && _inboundSequence > _initializationSendSequence;
        }

        if (sent && !accepted && afterSend)
            ApplyEvent(BedrockBootstrapEvent.LocalPlayerInitializationAccepted(runtimeId));
    }

    private ImmutableArray<BedrockBootstrapEffect> ApplyEvent(BedrockBootstrapEvent @event)
    {
        BedrockBootstrapState state;
        lock (_gate)
            state = _bootstrap;
        BedrockBootstrapTransition transition = BedrockBootstrapPolicy.Apply(state, @event);
        if (transition.Failure is BedrockBootstrapFailure failure)
        {
            throw new BedrockJoinTerminalException(failure.Code == BedrockBootstrapFailureCode.RemoteDisconnect
                ? new BedrockTermination(BedrockSessionEndReason.RemoteDisconnect, BedrockSessionScope.Session, failure.Message)
                : new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, failure.Message));
        }

        lock (_gate)
            _bootstrap = transition.State;

        RefreshSnapshot();
        return transition.Effects;
    }

    // End transfer completes the local headless bootstrap; it does not acknowledge remote protection clearing or input acceptance.
    private async ValueTask CompleteInitialHeadlessLoadingAsync()
    {
        lock (_gate)
        {
            if (!_bootstrap.IsReady || _initialLoadingComplete)
                return;
        }

        await SendInitialLoadingAsync(BedrockLoadingScreenType.EndLoadingScreen).ConfigureAwait(false);
        lock (_gate)
        {
            if (_initialLoadingRetired || _lifetime.Token.IsCancellationRequested)
                throw InitialLoadingRetired();
            RequireInitialLoadingDeadline();
            _initialLoadingComplete = true;
            RefreshSnapshot();
            _lifetime.MarkReady();
            if (!_readyFlushQueued)
            {
                _readyFlushQueued = true;
                EnqueueTerrain(new ReadyFlushWorkItem());
            }
        }
    }

    private ValueTask SendInitialLoadingAsync(BedrockLoadingScreenType type) =>
        SendInitialBootstrapAsync(312, BedrockLoadingScreenCodecs.Encode(new BedrockLoadingScreenPacket(type, null)), type);

    private async ValueTask SendInitialBootstrapAsync(int packetId, byte[] payload, BedrockLoadingScreenType? loadingType = null)
    {
        using var admissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        int epoch;
        lock (_gate)
        {
            if (_initialLoadingRetired || _lifetime.Token.IsCancellationRequested)
                throw InitialLoadingRetired();
            if (_initialLoadingAdmission is not null
                || (loadingType == BedrockLoadingScreenType.StartLoadingScreen && _initialLoadingStarted)
                || (loadingType == BedrockLoadingScreenType.EndLoadingScreen && (!_initialLoadingStarted || _initialLoadingEndTransferred))
                || (loadingType is null && (!_initialLoadingStarted || _initialLoadingEndTransferred)))
                throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, "The initial bootstrap send is outside its owned lifetime."));
            RequireInitialLoadingDeadline();
            epoch = _initialLoadingEpoch;
            _initialLoadingAdmission = admissionCancellation;
        }

        var member = new BedrockBatchMember(
            new BedrockPacketHeader(packetId, new BedrockSubclientId(0), new BedrockSubclientId(0)),
            payload);
        MessageAdmissionResult admission;
        ITimer? deadlineTimer = null;
        try
        {
            // Timeout publishes the terminal state without abandoning a token-free commit that already owns transport capacity.
            TimeSpan remaining = _deadline - _settings.TimeProvider.GetUtcNow();
            deadlineTimer = _settings.TimeProvider.CreateTimer(
                static state =>
                {
                    var (session, pendingAdmission) = ((BedrockClientSession, CancellationTokenSource))state!;
                    session.ExpireInitialLoading(pendingAdmission);
                },
                (this, admissionCancellation),
                remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                Timeout.InfiniteTimeSpan);
            admission = await _connection.SendBatchAsync([member], admissionCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
        {
            lock (_gate)
                throw InitialLoadingRetired(exception);
        }
        catch (OperationCanceledException exception) when (admissionCancellation.IsCancellationRequested)
        {
            lock (_gate)
                throw InitialLoadingRetired(exception);
        }
        finally
        {
            lock (_gate)
                _initialLoadingAdmission = null;
            deadlineTimer?.Dispose();
        }

        if (!admission.OwnershipTransferred)
        {
            lock (_gate)
                _rejectedSendAttempts++;
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.TransportFailure, BedrockSessionScope.Session, $"The initial bootstrap packet {packetId} was rejected with {admission.Status}."));
        }

        RecordOutbound(packetId);
        lock (_gate)
        {
            // Transferred bytes remain committed even when a lifecycle transition won the race with the returned result.
            if (loadingType == BedrockLoadingScreenType.StartLoadingScreen)
                _initialLoadingStarted = true;
            else if (loadingType == BedrockLoadingScreenType.EndLoadingScreen)
                _initialLoadingEndTransferred = true;
            if (epoch != _initialLoadingEpoch || _initialLoadingRetired || _lifetime.Token.IsCancellationRequested)
                throw InitialLoadingRetired();
            RequireInitialLoadingDeadline();
        }
    }

    private void ExpireInitialLoading(CancellationTokenSource pendingAdmission)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_initialLoadingAdmission, pendingAdmission) || _initialLoadingComplete)
                return;
            if (_settings.TimeProvider.GetUtcNow() >= _deadline)
                TryCompleteSession(InitialLoadingTimeout());
        }
    }

    private void RequireInitialLoadingDeadline()
    {
        if (_settings.TimeProvider.GetUtcNow() < _deadline)
            return;
        TryCompleteSession(InitialLoadingTimeout());
        throw InitialLoadingRetired();
    }

    private BedrockTermination InitialLoadingTimeout() =>
        new(BedrockSessionEndReason.Timeout, BedrockSessionScope.Session, $"The join phase '{_phase}' timed out during its initial bootstrap send.");

    private void RetireInitialLoading(BedrockSessionScope scope)
    {
        lock (_gate)
        {
            if (_initialLoadingRetired)
                return;
            _initialLoadingRetired = true;
            _initialLoadingRetirementScope = scope;
            RetireObservedOakDropOwner();
            RetireOrdinaryMainInventoryOperation();
            _playerAuthInputAdmissionLifetime.Cancel();
            _initialLoadingEpoch++;
            _initialLoadingAdmission?.Cancel();
        }
    }

    private BedrockJoinTerminalException InitialLoadingRetired(Exception? cause = null) =>
        new(_snapshot.Termination ?? new BedrockTermination(
            _lifetime.Token.IsCancellationRequested ? BedrockSessionEndReason.Cancelled : BedrockSessionEndReason.ProtocolFailure,
            _initialLoadingRetired ? _initialLoadingRetirementScope : BedrockSessionScope.Session,
            "The initial loading lifetime ended before readiness was published.",
            cause));

    private async ValueTask SendAsync(int packetId, byte[] payload)
    {
        var member = new BedrockBatchMember(
            new BedrockPacketHeader(packetId, new BedrockSubclientId(0), new BedrockSubclientId(0)),
            payload);
        MessageAdmissionResult admission;
        try
        {
            admission = await _connection.SendBatchAsync([member], _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
        }

        if (!admission.OwnershipTransferred)
        {
            lock (_gate)
                _rejectedSendAttempts++;
            throw new BedrockJoinTerminalException(new BedrockTermination(
                BedrockSessionEndReason.TransportFailure,
                BedrockSessionScope.Session,
                $"The join packet {packetId} was rejected with {admission.Status}."));
        }

        RecordOutbound(packetId);
    }

    private void RecordInbound(int packetId)
    {
        lock (_gate)
        {
            _inboundSequence++;
            if (_snapshot.IsReady && _postJoinPacketIds.Count < MaximumPostJoinPacketIds)
                _postJoinPacketIds.Add(packetId);
            if (_transcript.Count < MaximumTranscriptEvents)
                _transcript.Add(new BedrockWireEvent(BedrockWireDirection.Inbound, packetId, _settings.TimeProvider.GetUtcNow()));
        }
    }

    private void RecordOutbound(int packetId)
    {
        lock (_gate)
        {
            if (_transcript.Count < MaximumTranscriptEvents)
                _transcript.Add(new BedrockWireEvent(BedrockWireDirection.Outbound, packetId, _settings.TimeProvider.GetUtcNow()));
        }
    }

    private static int InboundPacketId(object packet) => packet switch
    {
        NetworkSettingsPacket => 143,
        ServerToClientHandshakePacket => 3,
        PlayStatusPacket => 2,
        DisconnectPacket => 5,
        ResourcePacksInfoPacket => 6,
        ResourcePackStackPacket => 7,
        StartGamePacket => 11,
        BedrockAddPlayerPacket => 12,
        BedrockAddEntityPacket => 13,
        BedrockRemoveEntityPacket => 14,
        BedrockAddItemEntityPacket => 15,
        BedrockMovePlayerPacket => 19,
        BedrockMobEffectPacket => 28,
        BedrockUpdateAttributesPacket => 29,
        BedrockMobEquipmentPacket => 31,
        BedrockMobArmorEquipmentPacket => 32,
        BedrockSetEntityDataPacket => 39,
        BedrockSetEntityMotionPacket => 40,
        BedrockSetEntityLinkPacket => 41,
        BedrockUpdateBlockPacket => 21,
        BedrockUpdateSubChunkBlocksPacket => 172,
        BedrockItemRegistryPacket => 162,
        AvailableEntityIdentifiersPacket => 119,
        BiomeDefinitionListPacket => 122,
        ChunkRadiusUpdatePacket => 70,
        LevelChunkPacket => 58,
        BedrockPlayerLocationPacket => 326,
        BedrockPacketViolationWarningPacket => 156,
        BedrockInventoryContentPacket => 49,
        BedrockInventorySlotPacket => 50,
        BedrockItemStackRequestPacket => 147,
        BedrockItemStackResponsePacket => 148,
        BedrockContainerOpenPacket => 46,
        BedrockContainerClosePacket => 47,
        BedrockPlayerHotbarPacket => 48,
        BedrockInventoryTransactionPacket => 30,
        BedrockCreativeContentPacket => 145,
        BedrockCorrectPlayerMovePredictionPacket => 161,
        BedrockToleratedPacket tolerated => tolerated.PacketId,
        _ => -1,
    };

    private void SetDeadline(TimeSpan budget, string phase)
    {
        _phase = phase;
        _deadline = _settings.TimeProvider.GetUtcNow() + budget;
    }

    private void RefreshSnapshot()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                TransportConnected = _bootstrap.TransportConnected,
                CompressionInstalled = _bootstrap.CompressionInstalled,
                LoginSent = _bootstrap.LoginSent,
                EncryptionInstalled = _bootstrap.EncryptionInstalled,
                IdentityAccepted = _bootstrap.IdentityAccepted,
                ResourcesComplete = _bootstrap.ResourcesComplete,
                WorldKnown = _bootstrap.WorldKnown,
                SpawnAuthorized = _bootstrap.SpawnAuthorized,
                ItemRegistryAvailable = _bootstrap.ItemRegistryAvailable,
                BiomeRegistryAvailable = _bootstrap.BiomeRegistryAvailable,
                EntityRegistryAvailable = _bootstrap.EntityRegistryAvailable,
                LocalPlayerInitializationSent = _bootstrap.LocalPlayerInitializationSent,
                LocalPlayerInitializationAccepted = _bootstrap.LocalPlayerInitializationAccepted,
                IsReady = _bootstrap.IsReady && _initialLoadingComplete,
                ActorCount = _actors.ActorCount,
                ActorWorldGeneration = _actors.WorldGeneration,
                ActorObservationsAvailable = _bootstrap.EntityRegistryAvailable,
            };
        }
    }

    private static string SanitizeDisconnect(DisconnectPacket packet)
    {
        string message = packet.HideDisconnectReason ? $"reason {packet.Reason}" : (packet.Message ?? $"reason {packet.Reason}");
        string sanitized = new(message.Where(static character => !char.IsControl(character)).Take(256).ToArray());
        return sanitized.Length == 0 ? $"The peer disconnected with reason {packet.Reason}." : sanitized;
    }

    private sealed class BedrockJoinTerminalException(BedrockTermination termination) : Exception(termination.Detail)
    {
        internal BedrockTermination Termination { get; } = termination;
    }

    // A typed semantic obligation applied after Connection dispatch returns, whose actual header was already observed once.
    private sealed record ObservedDisconnect(DisconnectPacket Packet);

    private abstract record TerrainWorkItem(int WorldGeneration);

    private sealed record LevelChunkWorkItem(LevelChunkPacket Packet, int WorldGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record SubChunkResponseWorkItem(byte[] Payload, int WorldGeneration, long RequestGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record UpdateBlockWorkItem(BedrockUpdateBlockPacket Packet, int WorldGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record UpdateSubChunkBlocksWorkItem(BedrockUpdateSubChunkBlocksPacket Packet, int WorldGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record ReadyFlushWorkItem() : TerrainWorkItem(-1);
}
