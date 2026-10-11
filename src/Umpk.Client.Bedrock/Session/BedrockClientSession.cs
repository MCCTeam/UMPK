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
    // The wire carries no request generation, so item-stack egress retires at a respawn, world, or pending-drop close boundary until reconnect.
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
    private readonly List<BedrockInventoryContentPacket> _capturedInventoryContents = [];
    private readonly List<BedrockInventorySlotPacket> _capturedInventorySlots = [];
    private readonly List<byte[]> _capturedInventoryContentPayloads = [];
    private readonly List<byte[]> _capturedInventorySlotPayloads = [];
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

    internal BedrockActionCoordinator ActionCoordinator => _actionCoordinator;

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

    /// <summary>Validates one hotbar place against authoritative content, admits the single outstanding client request, and sends the protocol-2169 packet.</summary>
    /// <remarks>Hotbar slots 0 through 8 are the current client support scope; the request remains claimed until its response or a failed send releases it.</remarks>
    internal async ValueTask<BedrockItemStackSubmitResult> SubmitInventoryPlaceAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, byte count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contentAddress.WindowId != 0 || contentAddress.ContainerId != 0 || contentAddress.DynamicContainerId.HasValue)
            throw new InvalidOperationException("The inventory request address is not a supported player inventory container.");
        if (sourceSlot > BedrockItemStackEgress.MaxHotbarSlot || destinationSlot > BedrockItemStackEgress.MaxHotbarSlot)
            throw new InvalidOperationException("The inventory request slot is outside the supported hotbar range.");
        if (sourceSlot == destinationSlot)
            throw new InvalidOperationException("The inventory request source and destination slots must differ.");
        if (count == 0)
            throw new InvalidOperationException("The inventory request count must be positive.");
        BedrockInventorySnapshot? snapshot = _inventory.GetSnapshot(contentAddress);
        if (snapshot is null)
            throw new InvalidOperationException("The inventory request targets a container without authoritative content.");
        if (sourceSlot >= snapshot.Value.Items.Length || destinationSlot >= snapshot.Value.Items.Length)
            throw new InvalidOperationException("The inventory request slot is outside the authoritative container.");
        BedrockItemInstance sourceItem = snapshot.Value.Items[sourceSlot];
        BedrockItemInstance destinationItem = snapshot.Value.Items[destinationSlot];
        if (sourceItem.NetworkId == 0 || sourceItem.Count == 0)
            throw new InvalidOperationException("The inventory request source slot is empty.");
        if (!sourceItem.StackNetworkId.HasValue)
            throw new InvalidOperationException("The inventory request source slot has no authoritative stack identity.");
        if (count > sourceItem.Count)
            throw new InvalidOperationException("The inventory request count exceeds the authoritative source count.");
        int destinationStackId = 0;
        if (destinationItem.NetworkId != 0 || destinationItem.Count != 0)
        {
            if (destinationItem.NetworkId != sourceItem.NetworkId || destinationItem.MetadataValue != sourceItem.MetadataValue || destinationItem.BlockRuntimeId != sourceItem.BlockRuntimeId || !destinationItem.ExtraData.AsSpan().SequenceEqual(sourceItem.ExtraData))
                throw new InvalidOperationException("The inventory request destination slot holds a different item for this operation.");
            if (!destinationItem.StackNetworkId.HasValue)
                throw new InvalidOperationException("The inventory request destination slot has no authoritative stack identity.");
            destinationStackId = destinationItem.StackNetworkId.Value;
        }

        int requestId = BedrockItemStackEgress.ClientRequestId;
        int generation = TryAdmitItemStackRequest(requestId);

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

        return await SendItemStackRequestAsync(packet, requestId, generation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates one hotbar take against authoritative content, admits the single outstanding client request, and sends the protocol-2169 packet.</summary>
    /// <remarks>Hotbar slots 0 through 8 are the current client support scope; the request remains claimed until its response or a failed send releases it.</remarks>
    internal async ValueTask<BedrockItemStackSubmitResult> SubmitInventoryTakeAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, byte count, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contentAddress.WindowId != 0 || contentAddress.ContainerId != 0 || contentAddress.DynamicContainerId.HasValue)
            throw new InvalidOperationException("The inventory request address is not a supported player inventory container.");
        if (sourceSlot > BedrockItemStackEgress.MaxHotbarSlot || destinationSlot > BedrockItemStackEgress.MaxHotbarSlot)
            throw new InvalidOperationException("The inventory request slot is outside the supported hotbar range.");
        if (sourceSlot == destinationSlot)
            throw new InvalidOperationException("The inventory request source and destination slots must differ.");
        if (count == 0)
            throw new InvalidOperationException("The inventory request count must be positive.");
        BedrockInventorySnapshot? snapshot = _inventory.GetSnapshot(contentAddress);
        if (snapshot is null)
            throw new InvalidOperationException("The inventory request targets a container without authoritative content.");
        if (sourceSlot >= snapshot.Value.Items.Length || destinationSlot >= snapshot.Value.Items.Length)
            throw new InvalidOperationException("The inventory request slot is outside the authoritative container.");
        BedrockItemInstance sourceItem = snapshot.Value.Items[sourceSlot];
        BedrockItemInstance destinationItem = snapshot.Value.Items[destinationSlot];
        if (sourceItem.NetworkId == 0 || sourceItem.Count == 0)
            throw new InvalidOperationException("The inventory request source slot is empty.");
        if (!sourceItem.StackNetworkId.HasValue)
            throw new InvalidOperationException("The inventory request source slot has no authoritative stack identity.");
        if (count > sourceItem.Count)
            throw new InvalidOperationException("The inventory request count exceeds the authoritative source count.");
        int destinationStackId = 0;
        if (destinationItem.NetworkId != 0 || destinationItem.Count != 0)
        {
            if (destinationItem.NetworkId != sourceItem.NetworkId || destinationItem.MetadataValue != sourceItem.MetadataValue || destinationItem.BlockRuntimeId != sourceItem.BlockRuntimeId || !destinationItem.ExtraData.AsSpan().SequenceEqual(sourceItem.ExtraData))
                throw new InvalidOperationException("The inventory request destination slot holds a different item for this operation.");
            if (!destinationItem.StackNetworkId.HasValue)
                throw new InvalidOperationException("The inventory request destination slot has no authoritative stack identity.");
            destinationStackId = destinationItem.StackNetworkId.Value;
        }

        int requestId = BedrockItemStackEgress.ClientRequestId;
        int generation = TryAdmitItemStackRequest(requestId);

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

        return await SendItemStackRequestAsync(packet, requestId, generation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Validates one hotbar swap against authoritative content, admits the single outstanding client request, and sends the protocol-2169 packet.</summary>
    /// <remarks>Hotbar slots 0 through 8 are the current client support scope; the request remains claimed until its response or a failed send releases it.</remarks>
    internal async ValueTask<BedrockItemStackSubmitResult> SubmitInventorySwapAsync(BedrockContainerAddress contentAddress, byte sourceSlot, byte destinationSlot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contentAddress.WindowId != 0 || contentAddress.ContainerId != 0 || contentAddress.DynamicContainerId.HasValue)
            throw new InvalidOperationException("The inventory request address is not a supported player inventory container.");
        if (sourceSlot > BedrockItemStackEgress.MaxHotbarSlot || destinationSlot > BedrockItemStackEgress.MaxHotbarSlot)
            throw new InvalidOperationException("The inventory request slot is outside the supported hotbar range.");
        if (sourceSlot == destinationSlot)
            throw new InvalidOperationException("The inventory request source and destination slots must differ.");
        BedrockInventorySnapshot? snapshot = _inventory.GetSnapshot(contentAddress);
        if (snapshot is null)
            throw new InvalidOperationException("The inventory request targets a container without authoritative content.");
        if (sourceSlot >= snapshot.Value.Items.Length || destinationSlot >= snapshot.Value.Items.Length)
            throw new InvalidOperationException("The inventory request slot is outside the authoritative container.");
        BedrockItemInstance sourceItem = snapshot.Value.Items[sourceSlot];
        BedrockItemInstance destinationItem = snapshot.Value.Items[destinationSlot];
        if (sourceItem.NetworkId == 0 || sourceItem.Count == 0)
            throw new InvalidOperationException("The inventory request source slot is empty.");
        if (!sourceItem.StackNetworkId.HasValue)
            throw new InvalidOperationException("The inventory request source slot has no authoritative stack identity.");
        if (destinationItem.NetworkId == 0 || destinationItem.Count == 0)
            throw new InvalidOperationException("The inventory request destination slot is empty for this operation.");
        if (!destinationItem.StackNetworkId.HasValue)
            throw new InvalidOperationException("The inventory request destination slot has no authoritative stack identity.");

        int requestId = BedrockItemStackEgress.ClientRequestId;
        int generation = TryAdmitItemStackRequest(requestId);

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

        return await SendItemStackRequestAsync(packet, requestId, generation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Binds the retirement flag, coordinator claim, and tracker registration under the session gate and returns the admitted generation.</summary>
    /// <remarks>The shared session gate prevents a lifecycle or close boundary from slipping between those checks, and the client-owned negative request identifier permits only one outstanding inventory request.</remarks>
    private int TryAdmitItemStackRequest(int requestId)
    {
        lock (_gate)
        {
            if (!_snapshot.IsReady)
                throw new InvalidOperationException("The inventory request requires a ready session.");
            if (_itemStackEgressRetired)
                throw new InvalidOperationException("The inventory request path is retired after a lifecycle boundary until reconnect.");
            if (_inventory.Generation != _itemStackTracker.Generation)
                throw new InvalidOperationException("The inventory request generation is stale.");
            if (!_actionCoordinator.TryClaim(requestId, BedrockActionEmission.Standalone))
                throw new InvalidOperationException($"The inventory action {requestId} is already claimed.");
            int generation = _itemStackTracker.Generation;
            if (!_itemStackTracker.TryRegister(requestId, generation))
            {
                _actionCoordinator.Release(requestId);
                throw new InvalidOperationException($"The inventory request {requestId} could not be registered. Only one inventory request stays outstanding.");
            }

            return generation;
        }
    }

    /// <summary>Sends a request whose action ID and response tracker entry were already admitted, releasing both when the connection does not take ownership of the packet.</summary>
    internal async ValueTask<BedrockItemStackSubmitResult> SendItemStackRequestAsync(BedrockItemStackRequestPacket packet, int requestId, int generation, CancellationToken cancellationToken)
    {
        byte[] payload;
        try
        {
            payload = BedrockItemStackCodecs.Encode(packet);
        }
        catch
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw;
        }

        var member = new BedrockBatchMember(
            new BedrockPacketHeader(147, new BedrockSubclientId(0), new BedrockSubclientId(0)),
            payload);
        MessageAdmissionResult admission;
        try
        {
            admission = await _connection.SendBatchAsync([member], _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw new BedrockSessionException(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
        }
        catch
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            throw;
        }

        if (!admission.OwnershipTransferred)
        {
            _itemStackTracker.TryCancel(requestId, generation);
            _actionCoordinator.Release(requestId);
            lock (_gate)
                _rejectedSendAttempts++;
            throw new InvalidOperationException($"The inventory request was rejected with {admission.Status}.");
        }

        RecordOutbound(147);
        CaptureItemStackRequestPayload(payload);
        return new BedrockItemStackSubmitResult(requestId, (byte[])payload.Clone());
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

        await _playerAuthInputEgress.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (!_snapshot.IsReady)
                    throw new InvalidOperationException("The player auth input requires a ready session.");
                if (_lifetime.Termination.IsCompleted)
                    throw new InvalidOperationException("The player auth input requires a live session.");
                RequirePlayerAuthInputTick(input.Tick);
            }

            var member = new BedrockBatchMember(
                new BedrockPacketHeader(144, new BedrockSubclientId(0), new BedrockSubclientId(0)),
                payload);
            MessageAdmissionResult admission;
            try
            {
                admission = await _connection.SendBatchAsync([member], _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
            {
                throw new BedrockSessionException(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
            }

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
        finally
        {
            _playerAuthInputEgress.Release();
        }
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
        _lifetime.TryComplete(new BedrockTermination(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session));
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
        _lifetime.TryComplete(new BedrockTermination(BedrockSessionEndReason.LocalDisconnect, BedrockSessionScope.Session, "The session was disposed."));
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
        _lifetime.Dispose();
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
            _lifetime.TryComplete(exception.Termination);
        }
        catch (OperationCanceledException exception) when (_lifetime.Token.IsCancellationRequested)
        {
            _lifetime.TryComplete(new BedrockTermination(BedrockSessionEndReason.Cancelled, BedrockSessionScope.Session, null, exception));
        }
        catch (BedrockCodecException exception)
        {
            _lifetime.TryComplete(new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, exception.Message, exception));
        }
        catch (Exception exception)
        {
            _lifetime.TryComplete(new BedrockTermination(BedrockSessionEndReason.TransportFailure, BedrockSessionScope.Session, exception.Message, exception));
        }
        finally
        {
            lock (_gate)
                _snapshot = _snapshot with { Termination = _lifetime.Termination.IsCompletedSuccessfully ? _lifetime.Termination.Result : null };
        }
    }

    private async Task<bool> ReceiveBatchAsync(List<object> batch)
    {
        TimeProvider timeProvider = _settings.TimeProvider;
        TimeSpan remaining = _deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.Timeout, BedrockSessionScope.Session, $"The join phase '{_phase}' timed out."));

        IBedrockBatchConsumer consumer;
        lock (_gate)
            consumer = _snapshot.IsReady
                ? new BedrockActorObservationConsumer(header => RecordInbound(header.PacketId), ApplyPostJoinObservation)
                : new BedrockPacketDispatcher((packet, _) => batch.Add(packet), ObserveAdmittedPreReady);

        Task<bool> receiveTask = _connection.ReceiveAndDispatchAsync(consumer, _lifetime.Token).AsTask();
        try
        {
            return await receiveTask.WaitAsync(remaining, timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new BedrockJoinTerminalException(new BedrockTermination(BedrockSessionEndReason.Timeout, BedrockSessionScope.Session, $"The join phase '{_phase}' timed out.", exception));
        }
    }

    private async ValueTask ApplyInboundAsync(object packet, int remainingAfterCurrent)
    {
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
                await SendAsync(113, BedrockBootstrapCodecs.Encode(new SetLocalPlayerAsInitializedPacket(effect.RuntimeEntityId))).ConfigureAwait(false);
                lock (_gate)
                    _initializationSendSequence = _inboundSequence + remainingAfterCurrent;
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
                NoteInventoryWindow(packet.Container);
            _inventory.ApplyContent(packet, _inventory.Generation);
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

            _inventory.ApplySlot(packet, address.Value, _inventory.Generation);
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
        _containerWindows.NoteOpen(packet, _containerWindows.Generation);
        RefreshSnapshot();
    }

    private void ApplyContainerClose(BedrockContainerClosePacket packet)
    {
        CaptureContainerClose(packet);
        // Close binds pending detection, pending clear, coordinator retirement, and egress retirement under the session gate so a concurrent submit cannot slip between them.
        lock (_gate)
        {
            bool known = _containerWindows.TryClose(packet.WindowId, _containerWindows.Generation);
            _inventoryWindows.Remove(packet.WindowId);
            _inventory.RemoveWindow(packet.WindowId);
            bool droppedPending = _itemStackTracker.PendingCount > 0;
            _itemStackTracker.ClearPending();
            _actionCoordinator.Invalidate();
            if (droppedPending)
                _itemStackEgressRetired = true;
            if (!known)
                _duplicateContainerCloses++;
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
            IReadOnlyList<BedrockItemStackResponseOutcome> outcomes = _itemStackTracker.ApplyResponses(packet.Responses, _itemStackTracker.Generation);
            for (int index = 0; index < packet.Responses.Length; index++)
            {
                if (outcomes[index] is BedrockItemStackResponseOutcome.Accepted or BedrockItemStackResponseOutcome.Rejected)
                    _actionCoordinator.Release(packet.Responses[index].RequestId);
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
        if (member.Header.PacketId == 48)
            CaptureHotbarPayload(member.Payload);
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
        _actors.InvalidatePlayerLife();
        InvalidateInventoryTransient();
        RefreshSnapshot();
    }

    // A world replacement retires actors, terrain, transient inventory work, and item-stack egress together so late packets cannot land in the next world.
    internal void HandleWorldReplacement()
    {
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
            _inventory.InvalidateTransient();
            _containerWindows.Invalidate();
            _itemStackTracker.Invalidate();
            _actionCoordinator.Invalidate();
        }
    }

    private void TeardownInventory()
    {
        lock (_gate)
        {
            if (_inventoryTornDown)
                return;
            _inventoryTornDown = true;
            _inventoryWindows.Clear();
            _unresolvedInventorySlots = 0;
            _duplicateContainerCloses = 0;
            _lastHotbar = null;
            _hotbarObservations = 0;
            _lastCreativeContent = null;
            _creativeContentObservations = 0;
            _lastInventoryTransaction = null;
            _inventoryTransactionObservations = 0;
            _inventory.Invalidate();
            _containerWindows.Invalidate();
            _itemStackTracker.Invalidate();
            _actionCoordinator.Invalidate();
        }
    }

    private void ApplyPostJoinObservation(BedrockBatchMember member, BedrockSessionCodecContext context)
    {
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
                    _lifetime.TryComplete(new BedrockTermination(BedrockSessionEndReason.ProtocolFailure, BedrockSessionScope.Session, exception.Message, exception));
                    return;
                }
                catch (InvalidOperationException exception)
                {
                    _lifetime.TryComplete(new BedrockTermination(
                        BedrockSessionEndReason.ProtocolFailure,
                        BedrockSessionScope.Session,
                        $"The terrain observation cannot be retained: {exception.Message}.",
                        exception));
                    return;
                }
                catch (BedrockJoinTerminalException exception)
                {
                    _lifetime.TryComplete(exception.Termination);
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
        {
            _bootstrap = transition.State;
            if (transition.State.IsReady)
                _lifetime.MarkReady();
        }

        bool queueReadyFlush = false;
        lock (_gate)
        {
            if (transition.State.IsReady && !_readyFlushQueued)
            {
                _readyFlushQueued = true;
                queueReadyFlush = true;
            }
        }

        if (queueReadyFlush)
            EnqueueTerrain(new ReadyFlushWorkItem());

        RefreshSnapshot();
        return transition.Effects;
    }

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
                IsReady = _bootstrap.IsReady,
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

    private abstract record TerrainWorkItem(int WorldGeneration);

    private sealed record LevelChunkWorkItem(LevelChunkPacket Packet, int WorldGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record SubChunkResponseWorkItem(byte[] Payload, int WorldGeneration, long RequestGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record UpdateBlockWorkItem(BedrockUpdateBlockPacket Packet, int WorldGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record UpdateSubChunkBlocksWorkItem(BedrockUpdateSubChunkBlocksPacket Packet, int WorldGeneration) : TerrainWorkItem(WorldGeneration);

    private sealed record ReadyFlushWorkItem() : TerrainWorkItem(-1);
}
