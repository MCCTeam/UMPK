using System.Globalization;
using System.Text;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Geometry;

namespace Umpk.IntegrationTests;

/// <summary>Cumulative event signals collected over a live leg. Subscriptions run on the session loop; counters use <see cref="Interlocked"/> so the test thread can read them safely. Cumulative counting (not a point-in-time snapshot) makes the entity/chunk signals robust against a transient spawn/despawn (a summoned mob that falls out of a bare-shell world, for example).</summary>
public sealed class LegSignals
{
    private int _chunkLoads;
    private int _entitySpawns;
    private int _entityRemovals;
    private int _containerContentChanges;
    private int _containerOpens;
    private int _timeChanges;
    private int _blockChanges;
    private int _chatReceived;
    private int _effectsApplied;
    private int _disconnectDuringWindow;
    private readonly List<(int Id, string Type, Vec3d Position)> _spawnedEntities = [];
    private readonly Dictionary<int, int> _metadataByEntity = [];
    private readonly object _entityGate = new();

    /// <summary>The most recent disconnect reason + fault, captured for failure diagnosis.</summary>
    public string? LastDisconnect { get; private set; }

    /// <summary>The most recent chat message text received (for echo diagnosis).</summary>
    public string? LastChat { get; private set; }

    /// <summary>The window id of the most recently opened container (from <see cref="ContainerOpened"/>).</summary>
    public int? LastOpenedWindowId { get; private set; }

    /// <summary>Number of <see cref="ChunkLoaded"/> events observed.</summary>
    public int ChunkLoads => Volatile.Read(ref _chunkLoads);

    /// <summary>Number of <see cref="EntitySpawned"/> events observed.</summary>
    public int EntitySpawns => Volatile.Read(ref _entitySpawns);

    /// <summary>Number of <see cref="EntityRemoved"/> events observed.</summary>
    public int EntityRemovals => Volatile.Read(ref _entityRemovals);

    /// <summary>Number of <see cref="ContainerContentChanged"/> events observed (full-window and single-slot).</summary>
    public int ContainerContentChanges => Volatile.Read(ref _containerContentChanges);

    /// <summary>Number of <see cref="ContainerOpened"/> events observed (server-driven window opens).</summary>
    public int ContainerOpens => Volatile.Read(ref _containerOpens);

    /// <summary>Number of <see cref="TimeChanged"/> events observed.</summary>
    public int TimeChanges => Volatile.Read(ref _timeChanges);

    /// <summary>Number of <see cref="BlockChanged"/> events observed.</summary>
    public int BlockChanges => Volatile.Read(ref _blockChanges);

    /// <summary>Number of <see cref="ChatMessageReceived"/> events observed.</summary>
    public int ChatReceived => Volatile.Read(ref _chatReceived);

    /// <summary>Number of <see cref="EntityEffectApplied"/> events observed (potion effect apply+track).</summary>
    public int EffectsApplied => Volatile.Read(ref _effectsApplied);

    /// <summary>Number of <see cref="Disconnected"/> events observed (any unexpected mid-leg drop).</summary>
    public int DisconnectDuringWindow => Volatile.Read(ref _disconnectDuringWindow);

    /// <summary>Attributes a console summon: among the spawns observed at or after the Nth spawn (0-based), returns the id of the first whose type id contains the fragment, or failing that the first that spawned within <paramref name="radius"/> blocks of the summon point. The position fallback is the workhorse: the static per-protocol registries carry no entity-type table, so decoded spawns resolve to the synthetic <c>minecraft:unknown</c> type on every version, while the summon coordinates are exact and ambient spawns land far away.</summary>
    public int? AttributeSummon(int index, string typeFragment, Vec3d near, double radius)
    {
        lock (_entityGate)
        {
            for (int i = Math.Max(index, 0); i < _spawnedEntities.Count; i++)
                if (_spawnedEntities[i].Type.Contains(typeFragment, StringComparison.OrdinalIgnoreCase))
                    return _spawnedEntities[i].Id;

            for (int i = Math.Max(index, 0); i < _spawnedEntities.Count; i++)
            {
                Vec3d p = _spawnedEntities[i].Position;
                double dx = p.X - near.X;
                double dy = p.Y - near.Y;
                double dz = p.Z - near.Z;
                if (dx * dx + dy * dy + dz * dz <= radius * radius)
                    return _spawnedEntities[i].Id;

            }

            return null;
        }
    }

    /// <summary>Number of <see cref="EntityMetadataChanged"/> events observed for one entity id.</summary>
    public int MetadataChangesFor(int entityId)
    {
        lock (_entityGate)
            return _metadataByEntity.GetValueOrDefault(entityId);

    }

    /// <summary>The (id, type, position) tuples of spawns observed at or after the Nth spawn, for diagnostics.</summary>
    public string SpawnListSince(int index)
    {
        lock (_entityGate)
            return string.Join(",", _spawnedEntities
                .Skip(Math.Max(index, 0))
                .Select(s => $"{s.Id}:{(s.Type.Length == 0 ? "?" : s.Type)}@({s.Position.X:0.#},{s.Position.Y:0.#},{s.Position.Z:0.#})"));

    }

    /// <summary>Subscribes the counters to a client's event bus.</summary>
    public void Attach(UmpkClient client)
    {
        client.Events.Subscribe<ChunkLoaded>(_ => Interlocked.Increment(ref _chunkLoads));
        client.Events.Subscribe<EntitySpawned>(e =>
        {
            Interlocked.Increment(ref _entitySpawns);
            lock (_entityGate)
                _spawnedEntities.Add((
                    e.EntityId,
                    e.Entity.Type.IsDefault ? string.Empty : e.Entity.Type.Id.ToString(),
                    e.Entity.Position));

        });
        client.Events.Subscribe<EntityRemoved>(_ => Interlocked.Increment(ref _entityRemovals));
        client.Events.Subscribe<EntityMetadataChanged>(m =>
        {
            lock (_entityGate)
                _metadataByEntity[m.EntityId] = _metadataByEntity.GetValueOrDefault(m.EntityId) + 1;

        });
        client.Events.Subscribe<ContainerContentChanged>(_ => Interlocked.Increment(ref _containerContentChanges));
        client.Events.Subscribe<ContainerOpened>(o =>
        {
            Interlocked.Increment(ref _containerOpens);
            LastOpenedWindowId = o.WindowId;
        });
        client.Events.Subscribe<TimeChanged>(_ => Interlocked.Increment(ref _timeChanges));
        client.Events.Subscribe<BlockChanged>(_ => Interlocked.Increment(ref _blockChanges));
        client.Events.Subscribe<ChatMessageReceived>(c =>
        {
            Interlocked.Increment(ref _chatReceived);
            LastChat = c.Message?.ToString();
        });
        client.Events.Subscribe<EntityEffectApplied>(_ => Interlocked.Increment(ref _effectsApplied));
        client.Events.Subscribe<Disconnected>(d =>
        {
            Interlocked.Increment(ref _disconnectDuringWindow);
            LastDisconnect = $"reason={d.Info.Reason}, fault={d.Info.Fault?.GetType().Name}: {d.Info.Fault?.Message}";
        });
    }
}

/// <summary>The per-leg result record: one row of the live matrix, plus the raw signal counts.</summary>
public sealed class LegReport(string version, int protocol, string jre)
{
    public string Version { get; } = version;

    public int Protocol { get; } = protocol;

    public string Jre { get; } = jre;

    public bool Login { get; set; }

    public bool KeepAliveSurvived { get; set; }

    public bool World { get; set; }

    public bool Entities { get; set; }

    public string? EntitiesNote { get; set; }

    public bool Inventory { get; set; }

    public string? InventoryNote { get; set; }

    public bool Movement { get; set; }

    /// <summary>"n/a" marker when the client movement send path is a verbatim marker for this version.</summary>
    public string? MovementNote { get; set; }

    public bool Provisioned { get; set; }

    public bool Commands { get; set; }

    /// <summary>"n/a" marker when the declare-commands codec is a verbatim marker for this version.</summary>
    public string? CommandsNote { get; set; }

    public bool Chat { get; set; }

    public string? ChatNote { get; set; }

    /// <summary>Whether the client chat SEND round-trip was proven (server log token + echo where observable).</summary>
    public bool ChatSend { get; set; }

    /// <summary>Echo-observability marker for the chat send (for eras whose player-chat receive is a marker).</summary>
    public string? ChatSendNote { get; set; }

    /// <summary>Whether the client-driven chest open was observed (open_screen -> ContainerOpened).</summary>
    public bool ChestOpen { get; set; }

    /// <summary>"n/a" marker when the chest open is not drivable/observable on this version.</summary>
    public string? ChestOpenNote { get; set; }

    /// <summary>Whether the console-filled chest content decoded into the open container view.</summary>
    public bool ChestContent { get; set; }

    /// <summary>"n/a" marker when the container content receive is a marker on this version.</summary>
    public string? ChestContentNote { get; set; }

    /// <summary>Whether the client container click (quick-move) round-trip was confirmed by the server.</summary>
    public bool ChestClick { get; set; }

    /// <summary>"n/a" marker when the container click is not drivable/observable on this version.</summary>
    public string? ChestClickNote { get; set; }

    /// <summary>Whether the client-driven entity attack was confirmed (removal by attack or health decrease).</summary>
    public bool EntityAttack { get; set; }

    /// <summary>"n/a" / detail marker for the entity attack (which confirmation was observed).</summary>
    public string? EntityAttackNote { get; set; }

    /// <summary>Whether entity metadata (set_entity_data) was observed for the summoned cow.</summary>
    public bool EntityMetadata { get; set; }

    /// <summary>"n/a" marker when set_entity_data receive is a marker on this version.</summary>
    public string? EntityMetadataNote { get; set; }

    public bool CommandEffect { get; set; }

    public string? CommandEffectNote { get; set; }

    public bool BlockBreak { get; set; }

    /// <summary>Whether the client block-place (use_item_on) action was sent (send-path exercised).</summary>
    public bool BlockPlaceSent { get; set; }

    /// <summary>Whether the client-placed block was observed appearing in World (place round-trip).</summary>
    public bool BlockPlace { get; set; }

    /// <summary>"n/a" / capability marker for the client block-place (use_item_on) send path.</summary>
    public string? BlockPlaceNote { get; set; }

    /// <summary>Whether a potion effect apply was observed on a tracked entity (effects column).</summary>
    public bool Effects { get; set; }

    /// <summary>"n/a" marker when update_mob_effect is a verbatim marker for this version.</summary>
    public string? EffectsNote { get; set; }

    /// <summary>Whether the client dig action was sent (send-path exercised).</summary>
    public bool BlockDigSent { get; set; }

    /// <summary>Whether the server-side break was observed after the client dig (recorded, not gated).</summary>
    public bool BlockDigBroke { get; set; }

    /// <summary>"n/a" markers for features not applicable to this version (recorded, not asserted).</summary>
    public string? BlockBreakNote { get; set; }

    public bool Disconnect { get; set; }

    public bool HasWorld { get; set; }

    public int WorldNonAir { get; set; }

    public int ChunkLoads { get; set; }

    public int EntitySpawns { get; set; }

    public bool CommandTree { get; set; }

    public int SurvivedSeconds { get; set; }

    public Vec3d SpawnPosition { get; set; }

    public string? MovementError { get; set; }

    public string? MovementFinalStatus { get; set; }

    public string Result { get; set; } = "INCOMPLETE";

    private static string Mark(bool value) => value ? "ok" : "NO";

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"[LIVE] {Version} (proto {Protocol}, {Jre}) => {Result} | ");
        sb.Append(CultureInfo.InvariantCulture, $"login={Mark(Login)} keepalive={Mark(KeepAliveSurvived)} ");
        sb.Append(CultureInfo.InvariantCulture, $"world={Mark(World)}(nonAir={WorldNonAir},chunks={ChunkLoads}) ");
        sb.Append(CultureInfo.InvariantCulture, $"entities={(EntitiesNote ?? Mark(Entities))}(spawns={EntitySpawns}) ");
        sb.Append(CultureInfo.InvariantCulture, $"inventory={(InventoryNote ?? Mark(Inventory))} movement={(MovementNote ?? Mark(Movement))} ");
        sb.Append(CultureInfo.InvariantCulture, $"chat={(ChatNote ?? Mark(Chat))} chatSend={(ChatSendNote ?? Mark(ChatSend))} ");
        sb.Append(CultureInfo.InvariantCulture, $"cmdEffect={(CommandEffectNote ?? Mark(CommandEffect))} ");
        sb.Append(CultureInfo.InvariantCulture, $"blockPlace={(BlockPlaceNote ?? Mark(BlockPlace))} blockBreak={(BlockBreakNote ?? Mark(BlockBreak))} ");
        sb.Append(CultureInfo.InvariantCulture, $"chestOpen={(ChestOpenNote ?? Mark(ChestOpen))} chestContent={(ChestContentNote ?? Mark(ChestContent))} ");
        sb.Append(CultureInfo.InvariantCulture, $"chestClick={(ChestClickNote ?? Mark(ChestClick))} ");
        sb.Append(CultureInfo.InvariantCulture, $"attack={(EntityAttackNote ?? Mark(EntityAttack))} meta={(EntityMetadataNote ?? Mark(EntityMetadata))} ");
        sb.Append(CultureInfo.InvariantCulture, $"effects={(EffectsNote ?? Mark(Effects))} ");
        sb.Append(CultureInfo.InvariantCulture, $"commands={(CommandsNote ?? Mark(Commands))}(tree={CommandTree}) disconnect={Mark(Disconnect)}");
        if (MovementError is not null)
            sb.Append(CultureInfo.InvariantCulture, $" movementError={MovementError}");

        return sb.ToString();
    }

    /// <summary>A compact pipe-delimited matrix row for the results log.</summary>
    public string ToMatrixRow() => string.Join(" | ",
        Version, Protocol.ToString(CultureInfo.InvariantCulture), Jre,
        Mark(Login), Mark(KeepAliveSurvived), Mark(World), EntitiesNote ?? Mark(Entities),
        InventoryNote ?? Mark(Inventory), MovementNote ?? Mark(Movement), ChatNote ?? Mark(Chat),
        ChatSendNote ?? Mark(ChatSend), CommandEffectNote ?? Mark(CommandEffect),
        BlockPlaceNote ?? Mark(BlockPlace), BlockBreakNote ?? Mark(BlockBreak),
        ChestOpenNote ?? Mark(ChestOpen), ChestContentNote ?? Mark(ChestContent), ChestClickNote ?? Mark(ChestClick),
        EntityAttackNote ?? Mark(EntityAttack), EntityMetadataNote ?? Mark(EntityMetadata),
        EffectsNote ?? Mark(Effects),
        CommandsNote ?? Mark(Commands), Mark(Disconnect), Result);
}

/// <summary>Appends per-leg results to the file named by <c>UMPK_LIVE_RESULTS</c> (if set), so a long matrix run stays resumable and partial results survive an interruption. No-op when the variable is unset.</summary>
public static class LegResultLog
{
    private static readonly object Gate = new();

    public static void Append(LegReport report)
    {
        string? path = Environment.GetEnvironmentVariable("UMPK_LIVE_RESULTS");
        if (string.IsNullOrEmpty(path))
            return;

        string line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:o} | {1}\n{2}\n",
            DateTime.UtcNow, report.ToMatrixRow(), report.ToString());
        lock (Gate)
        {
            try
            {
                File.AppendAllText(path, line);
            }
            catch (IOException)
            {
                // best effort; the console output is the primary record
            }
        }
    }
}
