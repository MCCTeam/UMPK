using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Actors;

/// <summary>Keeps the client-side view of server-observed actors with generation-gated applies that count unknown targets instead of rejecting them.</summary>
/// <remarks>Stored positions and velocities are overwritten by each accepted server observation and never claim server acceptance of client movement, while stale generations return false without mutating state.</remarks>
internal sealed class BedrockActorStore
{
    internal const int MaxActors = 1024;
    internal const int MaxEvents = 4096;
    internal const int MaxWitnessBytes = 256;
    internal const int MaxEffectsPerActor = 64;
    internal const int MaxAttributesPerActor = 64;
    internal const int MaxMetadataPerActor = 256;
    internal const int MaxBaseAttributesPerActor = 64;
    internal const int MaxPropertiesPerActor = 128;
    internal const int MaxAbilitiesPerActor = 16;

    private static readonly int[] s_witnessedPacketIds = [12, 13, 14, 15, 19, 28, 29, 31, 32, 39, 40, 41, 326];

    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<ulong, Entry> _byRuntime = new();
    private readonly Dictionary<long, ulong> _runtimeByUnique = new();
    private readonly Queue<BedrockActorEvent> _events = new();
    private readonly Dictionary<int, BedrockActorWireWitness> _witnesses = new();
    private Entry? _local;
    private int _worldGeneration = 1;
    private int _unknownTargetUpdates;
    private int _staleRemovals;

    internal BedrockActorStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the current actor world generation used to refuse stale observations.</summary>
    internal int WorldGeneration
    {
        get
        {
            lock (_gate)
                return _worldGeneration;
        }
    }

    internal int ActorCount
    {
        get
        {
            lock (_gate)
                return _byRuntime.Count + (_local is null ? 0 : 1);
        }
    }

    /// <summary>Gets the number of accepted observations that named an unknown actor and were counted instead of applied.</summary>
    internal int UnknownTargetUpdates
    {
        get
        {
            lock (_gate)
                return _unknownTargetUpdates;
        }
    }

    /// <summary>Gets the number of remove requests that named no tracked actor and were counted instead of applied.</summary>
    internal int StaleRemovals
    {
        get
        {
            lock (_gate)
                return _staleRemovals;
        }
    }

    internal IReadOnlyList<BedrockActorSnapshot> Snapshots
    {
        get
        {
            lock (_gate)
            {
                var snapshots = new List<BedrockActorSnapshot>(_byRuntime.Count + 1);
                if (_local is not null)
                    snapshots.Add(_local.Snapshot);
                foreach (Entry entry in _byRuntime.Values)
                    snapshots.Add(entry.Snapshot);
                return snapshots;
            }
        }
    }

    internal IReadOnlyList<BedrockActorEvent> Events
    {
        get
        {
            lock (_gate)
                return [.. _events];
        }
    }

    internal IReadOnlyList<BedrockActorWireWitness> WireWitnesses
    {
        get
        {
            lock (_gate)
                return [.. _witnesses.Values.OrderBy(static witness => witness.PacketId)];
        }
    }

    /// <summary>Binds the local player identity from StartGame and keeps it local across later observations.</summary>
    /// <remarks>Calling it again with the same identity changes nothing, and the stale unique-to-runtime mapping for that runtime id is dropped.</remarks>
    internal void SetLocalPlayer(long uniqueEntityId, ulong runtimeEntityId)
    {
        lock (_gate)
        {
            if (_local is { UniqueEntityId: var unique, RuntimeEntityId: var runtime }
                && unique == uniqueEntityId && runtime == runtimeEntityId)
                return;

            _local = new Entry(
                new BedrockActorHandle(_worldGeneration, runtimeEntityId, uniqueEntityId, true),
                BedrockActorKind.LocalPlayer,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false);
            foreach (long staleUnique in _runtimeByUnique.Where(pair => pair.Value == runtimeEntityId).Select(static pair => pair.Key).ToArray())
                _runtimeByUnique.Remove(staleUnique);
            _runtimeByUnique[uniqueEntityId] = runtimeEntityId;
            AddEvent(BedrockActorEventKind.Added, _local.Handle);
        }
    }

    /// <summary>Adds or refreshes an entity from its server spawn observation under the current world generation.</summary>
    /// <remarks>A spawn carrying the local runtime id updates the local entry, a reused runtime id with a new unique id replaces the old entry, and stale generations return false without mutating state.</remarks>
    internal bool ApplyAddEntity(BedrockAddEntityPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            Entry? local = _local;
            if (local is not null && packet.RuntimeEntityId == local.RuntimeEntityId)
            {
                local.EntityType = packet.EntityType;
                local.Position = packet.Position;
                local.Velocity = packet.Velocity;
                RefreshSpawnStateLocked(local, packet.Attributes, packet.Metadata, packet.Properties, [], packet.Links);
                AddEvent(BedrockActorEventKind.Updated, local.Handle);
                return true;
            }

            if (_byRuntime.TryGetValue(packet.RuntimeEntityId, out Entry? existing))
            {
                if (existing.UniqueEntityId == packet.UniqueEntityId)
                {
                    existing.EntityType = packet.EntityType;
                    existing.Position = packet.Position;
                    existing.Velocity = packet.Velocity;
                    RefreshSpawnStateLocked(existing, packet.Attributes, packet.Metadata, packet.Properties, [], packet.Links);
                    AddEvent(BedrockActorEventKind.Updated, existing.Handle);
                    return true;
                }

                RemoveLocked(existing, BedrockActorEventKind.Removed);
            }

            Entry entry = AddLocked(
                BedrockActorKind.Entity,
                packet.RuntimeEntityId,
                packet.UniqueEntityId,
                null,
                null,
                packet.EntityType,
                packet.Position,
                packet.Velocity);
            RefreshSpawnStateLocked(entry, packet.Attributes, packet.Metadata, packet.Properties, [], packet.Links);
            AddEvent(BedrockActorEventKind.Added, entry.Handle);
            return true;
        }
    }

    /// <summary>Adds or refreshes a player from its server spawn observation under the current world generation.</summary>
    /// <remarks>A spawn carrying the local runtime id updates the local entry, a reused runtime id with a new unique id replaces the old entry, and stale generations return false without mutating state.</remarks>
    internal bool ApplyAddPlayer(BedrockAddPlayerPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            Entry? localPlayer = _local;
            if (localPlayer is not null && packet.RuntimeEntityId == localPlayer.RuntimeEntityId)
            {
                localPlayer.DisplayName = packet.Username;
                localPlayer.Uuid = packet.Uuid;
                localPlayer.Position = packet.Position;
                localPlayer.Velocity = packet.Velocity;
                RefreshSpawnStateLocked(localPlayer, [], packet.Metadata, packet.Properties, packet.AbilityLayers, packet.Links);
                AddEvent(BedrockActorEventKind.Updated, localPlayer.Handle);
                return true;
            }

            if (_byRuntime.TryGetValue(packet.RuntimeEntityId, out Entry? existing))
            {
                if (existing.UniqueEntityId == packet.UniqueEntityId)
                {
                    existing.DisplayName = packet.Username;
                    existing.Uuid = packet.Uuid;
                    existing.Position = packet.Position;
                    existing.Velocity = packet.Velocity;
                    RefreshSpawnStateLocked(existing, [], packet.Metadata, packet.Properties, packet.AbilityLayers, packet.Links);
                    AddEvent(BedrockActorEventKind.Updated, existing.Handle);
                    return true;
                }

                RemoveLocked(existing, BedrockActorEventKind.Removed);
            }

            Entry entry = AddLocked(
                BedrockActorKind.Player,
                packet.RuntimeEntityId,
                packet.UniqueEntityId,
                packet.Username,
                packet.Uuid,
                null,
                packet.Position,
                packet.Velocity);
            RefreshSpawnStateLocked(entry, [], packet.Metadata, packet.Properties, packet.AbilityLayers, packet.Links);
            AddEvent(BedrockActorEventKind.Added, entry.Handle);
            return true;
        }
    }

    /// <summary>Applies a dropped item spawn to the current world generation while retaining its item stack and actor identity for later observations.</summary>
    internal bool ApplyAddItemEntity(BedrockAddItemEntityPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            Entry? local = _local;
            if (local is not null && packet.RuntimeEntityId == local.RuntimeEntityId)
                return true;
            if (_byRuntime.TryGetValue(packet.RuntimeEntityId, out Entry? existing))
            {
                if (existing.UniqueEntityId == packet.UniqueEntityId)
                {
                    if (existing.Kind != BedrockActorKind.Item)
                        RemoveLocked(existing, BedrockActorEventKind.Removed);
                    else
                    {
                        existing.Position = packet.Position;
                        existing.Velocity = packet.Velocity;
                        existing.DroppedItem = packet.Item;
                        existing.IsFromFishing = packet.IsFromFishing;
                        existing.HeldItem = null;
                        existing.Armor = null;
                        RefreshSpawnStateLocked(existing, [], packet.Metadata, BedrockEntityProperties.Empty, [], []);
                        AddEvent(BedrockActorEventKind.Updated, existing.Handle);
                        return true;
                    }
                }
                else
                {
                    RemoveLocked(existing, BedrockActorEventKind.Removed);
                }
            }

            Entry entry = AddLocked(
                BedrockActorKind.Item,
                packet.RuntimeEntityId,
                packet.UniqueEntityId,
                null,
                null,
                null,
                packet.Position,
                packet.Velocity);
            entry.DroppedItem = packet.Item;
            entry.IsFromFishing = packet.IsFromFishing;
            entry.HeldItem = null;
            entry.Armor = null;
            RefreshSpawnStateLocked(entry, [], packet.Metadata, BedrockEntityProperties.Empty, [], []);
            AddEvent(BedrockActorEventKind.Added, entry.Handle);
            return true;
        }
    }

    /// <summary>Removes one actor by unique id while keeping the local player entry and counting unknown targets as stale.</summary>
    /// <remarks>Removing the local player succeeds without changing it, unknown ids increment the stale counter, and stale generations return false without mutating state.</remarks>
    internal bool ApplyRemoveEntity(long uniqueEntityId, int worldGeneration)
    {
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (_local is not null && _local.UniqueEntityId == uniqueEntityId)
                return true;
            if (!_runtimeByUnique.TryGetValue(uniqueEntityId, out ulong runtimeEntityId)
                || !_byRuntime.TryGetValue(runtimeEntityId, out Entry? existing))
            {
                _staleRemovals++;
                return true;
            }

            RemoveLocked(existing, BedrockActorEventKind.Removed);
            return true;
        }
    }

    /// <summary>Overwrites one actor velocity from a protocol-2169 set-entity-motion observation and records its server tick.</summary>
    /// <remarks>Unknown runtime ids increment the unknown-target counter instead of throwing, and stale generations return false without mutating state.</remarks>
    internal bool ApplySetEntityMotion(ulong runtimeEntityId, BedrockVector3 velocity, ulong tick, int worldGeneration)
    {
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFind(runtimeEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            entry.Velocity = velocity;
            entry.LastServerTick = tick;
            AddEvent(BedrockActorEventKind.Updated, entry.Handle);
            return true;
        }
    }

    /// <summary>Overwrites one actor position from a protocol-2169 move-player observation and records its server tick.</summary>
    /// <remarks>A zero ridden runtime id clears the riding link, unknown runtime ids increment the unknown-target counter instead of throwing, and stale generations return false without mutating state.</remarks>
    internal bool ApplyMovePlayer(
        ulong runtimeEntityId,
        BedrockVector3 position,
        ulong riddenRuntimeEntityId,
        ulong tick,
        int worldGeneration)
    {
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFind(runtimeEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            entry.Position = position;
            entry.RidingRuntimeEntityId = riddenRuntimeEntityId == 0UL ? null : riddenRuntimeEntityId;
            entry.LastServerTick = tick;
            AddEvent(BedrockActorEventKind.Updated, entry.Handle);
            return true;
        }
    }

    internal bool ApplySetEntityData(BedrockSetEntityDataPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFind(packet.RuntimeEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            CheckObservationBounds(packet.Metadata.Length, packet.Properties.Ints.Length, packet.Properties.Floats.Length, 0, 0);
            entry.Metadata = packet.Metadata;
            entry.Properties = packet.Properties;
            entry.MetadataObserved = true;
            entry.LastServerTick = packet.Tick;
            AddEvent(BedrockActorEventKind.Updated, entry.Handle);
            return true;
        }
    }

    internal bool ApplySetEntityLink(BedrockSetEntityLinkPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            ApplyLinksLocked([packet.Link]);
            return true;
        }
    }

    internal bool ApplyPlayerLocation(long uniqueEntityId, BedrockVector3? position, bool hide, int worldGeneration)
    {
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFindByUnique(uniqueEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            if (position.HasValue)
                entry.Position = position.Value;
            entry.LocationHidden = hide;
            AddEvent(BedrockActorEventKind.Updated, entry.Handle);
            return true;
        }
    }

    internal bool ApplyMobEffect(BedrockMobEffectPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFind(packet.RuntimeEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            switch (packet.Event)
            {
                case BedrockMobEffectEvent.Add or BedrockMobEffectEvent.Modify:
                    if (!entry.Effects.ContainsKey(packet.EffectId) && entry.Effects.Count >= MaxEffectsPerActor)
                        throw new InvalidOperationException($"The Bedrock actor keeps more than {MaxEffectsPerActor} effects.");
                    entry.Effects[packet.EffectId] = new BedrockActorEffect(packet.EffectId, packet.Amplifier, packet.Duration, packet.Particles, packet.Ambient);
                    entry.LastServerTick = packet.Tick;
                    AddEvent(BedrockActorEventKind.Updated, entry.Handle);
                    break;
                case BedrockMobEffectEvent.Remove:
                    entry.LastServerTick = packet.Tick;
                    if (entry.Effects.Remove(packet.EffectId))
                        AddEvent(BedrockActorEventKind.Updated, entry.Handle);
                    break;
                default:
                    throw new BedrockCodecException($"Unknown MobEffect event {(byte)packet.Event}.");
            }

            return true;
        }
    }

    internal bool ApplyUpdateAttributes(BedrockUpdateAttributesPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFind(packet.RuntimeEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            foreach (BedrockPlayerAttribute attribute in packet.Attributes)
            {
                if (!entry.Attributes.ContainsKey(attribute.Name) && entry.Attributes.Count >= MaxAttributesPerActor)
                    throw new InvalidOperationException($"The Bedrock actor keeps more than {MaxAttributesPerActor} attributes.");
                entry.Attributes[attribute.Name] = attribute;
            }

            entry.LastServerTick = packet.Tick;
            AddEvent(BedrockActorEventKind.Updated, entry.Handle);
            return true;
        }
    }

    internal bool ApplyMobEquipment(BedrockMobEquipmentPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFind(packet.RuntimeEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            entry.HeldItem = new BedrockHeldItemObservation(packet.Item, packet.InventorySlot, packet.SelectedSlot, packet.WindowId);
            AddEvent(BedrockActorEventKind.Updated, entry.Handle);
            return true;
        }
    }

    internal bool ApplyMobArmorEquipment(BedrockMobArmorEquipmentPacket packet, int worldGeneration)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (!TryFind(packet.RuntimeEntityId, out Entry? entry))
            {
                _unknownTargetUpdates++;
                return true;
            }

            entry.Armor = new BedrockArmorObservation(packet.Helmet, packet.Chestplate, packet.Leggings, packet.Boots, packet.Body);
            AddEvent(BedrockActorEventKind.Updated, entry.Handle);
            return true;
        }
    }

    internal void RecordWireWitness(int packetId, ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            if (Array.IndexOf(s_witnessedPacketIds, packetId) < 0 || _witnesses.ContainsKey(packetId))
                return;
            int kept = Math.Min(payload.Length, MaxWitnessBytes);
            _witnesses[packetId] = new BedrockActorWireWitness(packetId, Convert.ToHexString(payload[..kept]), payload.Length);
        }
    }

    /// <summary>Advances the world generation, drops remote actors, and resets local observations so late packets cannot land in the next world.</summary>
    /// <remarks>Dropped entries keep their original handles in the removal events while the local handle is reissued under the new generation.</remarks>
    internal void InvalidateWorld()
    {
        lock (_gate)
        {
            _worldGeneration++;
            var removed = new List<Entry>(_byRuntime.Values);
            _byRuntime.Clear();
            foreach (Entry entry in removed)
                _runtimeByUnique.Remove(entry.UniqueEntityId);
            foreach (Entry entry in removed)
                AddEvent(BedrockActorEventKind.Removed, entry.Handle);
            if (_local is not null)
            {
                _local.Handle = _local.Handle with { WorldGeneration = _worldGeneration };
                ResetObservationsLocked(_local);
                AddEvent(BedrockActorEventKind.WorldInvalidated, _local.Handle);
            }
            else
            {
                AddEvent(BedrockActorEventKind.WorldInvalidated, new BedrockActorHandle(_worldGeneration, 0UL, 0L, false));
            }
        }
    }

    /// <summary>Clears the local player position and velocity at a respawn boundary while keeping the local identity bound.</summary>
    internal void InvalidatePlayerLife()
    {
        lock (_gate)
        {
            if (_local is null)
                return;
            _local.Position = null;
            _local.Velocity = null;
            AddEvent(BedrockActorEventKind.PlayerLifeInvalidated, _local.Handle);
        }
    }

    /// <summary>Stores one dequeued packet-45 wire coordinate for the local player without converting it to feet or using it to seed movement simulation.</summary>
    internal bool ApplyRespawnPosition(BedrockVector3 position, int worldGeneration)
    {
        lock (_gate)
        {
            if (!CheckGeneration(worldGeneration))
                return false;
            if (_local is null)
                return true;
            _local.Position = position;
            AddEvent(BedrockActorEventKind.Updated, _local.Handle);
            return true;
        }
    }

    private bool CheckGeneration(int worldGeneration) => worldGeneration == _worldGeneration;

    private bool TryFind(ulong runtimeEntityId, [NotNullWhen(true)] out Entry? entry)
    {
        if (_local is not null && _local.RuntimeEntityId == runtimeEntityId)
        {
            entry = _local;
            return true;
        }

        return _byRuntime.TryGetValue(runtimeEntityId, out entry);
    }

    private bool TryFindByUnique(long uniqueEntityId, [NotNullWhen(true)] out Entry? entry)
    {
        if (_local is not null && _local.UniqueEntityId == uniqueEntityId)
        {
            entry = _local;
            return true;
        }

        if (_runtimeByUnique.TryGetValue(uniqueEntityId, out ulong runtimeEntityId))
            return _byRuntime.TryGetValue(runtimeEntityId, out entry);

        entry = null;
        return false;
    }

    private Entry AddLocked(
        BedrockActorKind kind,
        ulong runtimeEntityId,
        long uniqueEntityId,
        string? displayName,
        Guid? uuid,
        string? entityType,
        BedrockVector3? position,
        BedrockVector3? velocity)
    {
        if (_byRuntime.Count >= MaxActors)
            throw new InvalidOperationException($"The Bedrock actor store is full at {MaxActors} actors.");
        if (_runtimeByUnique.TryGetValue(uniqueEntityId, out ulong conflictingRuntime)
            && conflictingRuntime != runtimeEntityId
            && _byRuntime.TryGetValue(conflictingRuntime, out Entry? conflicting))
            RemoveLocked(conflicting, BedrockActorEventKind.Removed);
        var entry = new Entry(
            new BedrockActorHandle(_worldGeneration, runtimeEntityId, uniqueEntityId, false),
            kind,
            displayName,
            uuid,
            entityType,
            position,
            velocity,
            null,
            null,
            false,
            false);
        _byRuntime[runtimeEntityId] = entry;
        _runtimeByUnique[uniqueEntityId] = runtimeEntityId;
        return entry;
    }

    private void RefreshSpawnStateLocked(
        Entry entry,
        ImmutableArray<BedrockEntityAttributeValue> baseAttributes,
        ImmutableArray<BedrockMetadataEntry> metadata,
        BedrockEntityProperties properties,
        ImmutableArray<BedrockAbilityLayer> abilities,
        ImmutableArray<BedrockEntityLink> links)
    {
        CheckObservationBounds(metadata.Length, properties.Ints.Length, properties.Floats.Length, baseAttributes.Length, abilities.Length);
        entry.BaseAttributes = baseAttributes;
        entry.Metadata = metadata;
        entry.Properties = properties;
        entry.Abilities = abilities;
        if (metadata.Length > 0)
            entry.MetadataObserved = true;
        ApplyLinksLocked(links);
    }

    private void ApplyLinksLocked(ImmutableArray<BedrockEntityLink> links)
    {
        foreach (BedrockEntityLink link in links)
        {
            if (!TryFindByUnique(link.RiderUniqueId, out Entry? rider) || !TryFindByUnique(link.RiddenUniqueId, out Entry? ridden))
            {
                _unknownTargetUpdates++;
                continue;
            }

            if (link.Type == BedrockEntityLinkType.Remove)
            {
                if (rider.RidingRuntimeEntityId == ridden.RuntimeEntityId)
                {
                    rider.RidingRuntimeEntityId = null;
                    AddEvent(BedrockActorEventKind.Updated, rider.Handle);
                }

                continue;
            }

            if (rider.RidingRuntimeEntityId != ridden.RuntimeEntityId)
            {
                rider.RidingRuntimeEntityId = ridden.RuntimeEntityId;
                AddEvent(BedrockActorEventKind.Updated, rider.Handle);
            }
        }
    }

    private static void CheckObservationBounds(int metadata, int propertyInts, int propertyFloats, int baseAttributes, int abilities)
    {
        if (metadata > MaxMetadataPerActor)
            throw new InvalidOperationException($"The Bedrock actor keeps more than {MaxMetadataPerActor} metadata entries.");
        if (propertyInts > MaxPropertiesPerActor || propertyFloats > MaxPropertiesPerActor)
            throw new InvalidOperationException($"The Bedrock actor keeps more than {MaxPropertiesPerActor} property values.");
        if (baseAttributes > MaxBaseAttributesPerActor)
            throw new InvalidOperationException($"The Bedrock actor keeps more than {MaxBaseAttributesPerActor} base attributes.");
        if (abilities > MaxAbilitiesPerActor)
            throw new InvalidOperationException($"The Bedrock actor keeps more than {MaxAbilitiesPerActor} ability layers.");
    }

    private static void ResetObservationsLocked(Entry entry)
    {
        entry.Metadata = [];
        entry.BaseAttributes = [];
        entry.Properties = BedrockEntityProperties.Empty;
        entry.Abilities = [];
        entry.Effects.Clear();
        entry.Attributes.Clear();
        entry.HeldItem = null;
        entry.Armor = null;
        entry.DroppedItem = null;
        entry.IsFromFishing = false;
        entry.RidingRuntimeEntityId = null;
        entry.MetadataObserved = false;
        entry.LocationHidden = false;
        entry.Position = null;
        entry.Velocity = null;
    }

    private void RemoveLocked(Entry entry, BedrockActorEventKind kind)
    {
        _byRuntime.Remove(entry.RuntimeEntityId);
        _runtimeByUnique.Remove(entry.UniqueEntityId);
        foreach (Entry rider in _byRuntime.Values)
        {
            if (rider.RidingRuntimeEntityId == entry.RuntimeEntityId)
            {
                rider.RidingRuntimeEntityId = null;
                AddEvent(BedrockActorEventKind.Updated, rider.Handle);
            }
        }

        AddEvent(kind, entry.Handle);
    }

    private void AddEvent(BedrockActorEventKind kind, BedrockActorHandle handle)
    {
        while (_events.Count >= MaxEvents)
            _events.Dequeue();
        _events.Enqueue(new BedrockActorEvent(kind, handle, _timeProvider.GetUtcNow()));
    }

    private sealed class Entry
    {
        internal Entry(
            BedrockActorHandle handle,
            BedrockActorKind kind,
            string? displayName,
            Guid? uuid,
            string? entityType,
            BedrockVector3? position,
            BedrockVector3? velocity,
            ulong? lastServerTick,
            ulong? ridingRuntimeEntityId,
            bool metadataObserved,
            bool locationHidden)
        {
            Handle = handle;
            Kind = kind;
            DisplayName = displayName;
            Uuid = uuid;
            EntityType = entityType;
            Position = position;
            Velocity = velocity;
            LastServerTick = lastServerTick;
            RidingRuntimeEntityId = ridingRuntimeEntityId;
            MetadataObserved = metadataObserved;
            LocationHidden = locationHidden;
        }

        internal BedrockActorHandle Handle { get; set; }

        internal BedrockActorKind Kind { get; }

        internal ulong RuntimeEntityId => Handle.RuntimeEntityId;

        internal long UniqueEntityId => Handle.UniqueEntityId;

        internal string? DisplayName { get; set; }

        internal Guid? Uuid { get; set; }

        internal string? EntityType { get; set; }

        internal BedrockVector3? Position { get; set; }

        internal BedrockVector3? Velocity { get; set; }

        internal ulong? LastServerTick { get; set; }

        internal ulong? RidingRuntimeEntityId { get; set; }

        internal bool MetadataObserved { get; set; }

        internal bool LocationHidden { get; set; }

        internal Dictionary<int, BedrockActorEffect> Effects { get; } = new();

        internal Dictionary<string, BedrockPlayerAttribute> Attributes { get; } = new(StringComparer.Ordinal);

        internal BedrockHeldItemObservation? HeldItem { get; set; }

        internal BedrockArmorObservation? Armor { get; set; }

        internal BedrockItemInstance? DroppedItem { get; set; }

        internal bool IsFromFishing { get; set; }

        internal ImmutableArray<BedrockMetadataEntry> Metadata { get; set; } = [];

        internal ImmutableArray<BedrockEntityAttributeValue> BaseAttributes { get; set; } = [];

        internal BedrockEntityProperties Properties { get; set; } = BedrockEntityProperties.Empty;

        internal ImmutableArray<BedrockAbilityLayer> Abilities { get; set; } = [];

        internal BedrockActorSnapshot Snapshot => new(
            Handle,
            Kind,
            DisplayName,
            Uuid,
            EntityType,
            Position,
            Velocity,
            LastServerTick,
            RidingRuntimeEntityId,
            MetadataObserved,
            LocationHidden,
            [.. Effects.Values.OrderBy(static effect => effect.EffectId)],
            [.. Attributes.Values.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)],
            HeldItem,
            Armor,
            Metadata,
            BaseAttributes,
            Properties,
            Abilities,
            DroppedItem,
            IsFromFishing);
    }
}
