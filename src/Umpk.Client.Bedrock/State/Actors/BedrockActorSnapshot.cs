using System.Collections.Immutable;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.State.Actors;

/// <summary>Names which kind of actor a snapshot describes.</summary>
internal enum BedrockActorKind
{
    /// <summary>Marks the snapshot of the client local player.</summary>
    LocalPlayer,
    /// <summary>Marks the snapshot of another player seen on the server.</summary>
    Player,
    /// <summary>Marks the snapshot of a non-player entity.</summary>
    Entity,
    /// <summary>Marks the snapshot of a dropped item entity.</summary>
    Item,
}

/// <summary>Names what changed for one actor in the client view.</summary>
internal enum BedrockActorEventKind
{
    /// <summary>Marks an actor that started being tracked.</summary>
    Added,
    /// <summary>Marks an actor whose observed state changed.</summary>
    Updated,
    /// <summary>Marks an actor that stopped being tracked.</summary>
    Removed,
    /// <summary>Marks the reset that follows a dropped world view.</summary>
    WorldInvalidated,
    /// <summary>Marks the local entry after a respawn boundary cleared its observed motion.</summary>
    PlayerLifeInvalidated,
}

/// <summary>Captures one frozen view of everything the client has observed about a single actor.</summary>
/// <remarks>Position and velocity repeat the last server observation with no coordinate-domain label and never describe predicted physics, so they must not be read as feet values or as movement the server accepted.</remarks>
internal sealed record BedrockActorSnapshot(
    BedrockActorHandle Handle,
    BedrockActorKind Kind,
    string? DisplayName,
    Guid? Uuid,
    string? EntityType,
    BedrockVector3? Position,
    BedrockVector3? Velocity,
    ulong? LastServerTick,
    ulong? RidingRuntimeEntityId,
    bool MetadataObserved,
    bool LocationHidden,
    ImmutableArray<BedrockActorEffect> Effects,
    ImmutableArray<BedrockPlayerAttribute> Attributes,
    BedrockHeldItemObservation? HeldItem,
    BedrockArmorObservation? Armor,
    ImmutableArray<BedrockMetadataEntry> Metadata,
    ImmutableArray<BedrockEntityAttributeValue> BaseAttributes,
    BedrockEntityProperties Properties,
    ImmutableArray<BedrockAbilityLayer> Abilities,
    BedrockItemInstance? DroppedItem,
    bool IsFromFishing);

/// <summary>Pairs one actor change with the handle it affected and the actor store clock reading taken when it was queued.</summary>
/// <remarks>The timestamp comes from the store's time provider when the event was queued and never carries server time.</remarks>
internal sealed record BedrockActorEvent(BedrockActorEventKind Kind, BedrockActorHandle Handle, DateTimeOffset Timestamp);

/// <summary>Keeps the packet id, leading bytes, and total length of the first actor-observation packet retained for that packet id.</summary>
internal sealed record BedrockActorWireWitness(int PacketId, string FirstBytesHex, int TotalLength);
