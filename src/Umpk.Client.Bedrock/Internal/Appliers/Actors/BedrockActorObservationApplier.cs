using Umpk.Client.Bedrock.State.Actors;
using Umpk.Protocol.Bedrock;

namespace Umpk.Client.Bedrock.Internal.Appliers.Actors;

/// <summary>Routes decoded protocol-2169 actor packets to the actor store behind the entity-registry-installed gate.</summary>
/// <remarks>Packets arriving before the registry is installed are refused without touching the store, and the packet-id switch is the only map from wire id to applier.</remarks>
internal static class BedrockActorObservationApplier
{
    /// <summary>Reports whether a protocol-2169 packet id carries actor observation handled by this applier.</summary>
    /// <remarks>The thirteen listed ids are the wire surface this slice decodes, and anything else stays with its own handler.</remarks>
    internal static bool IsActorPacket(int packetId) => packetId is 12 or 13 or 14 or 15 or 19 or 28 or 29 or 31 or 32 or 39 or 40 or 41 or 326;

    /// <summary>Records the wire witness and applies one decoded actor packet to the store when the entity registry is installed.</summary>
    /// <remarks>Unknown targets never throw here because the store counts them, while an unmapped packet type throws because it reached the wrong applier.</remarks>
    internal static bool Apply(
        BedrockActorStore store,
        object packet,
        byte[]? payload,
        int worldGeneration,
        bool entityRegistryInstalled)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(packet);
        if (!entityRegistryInstalled)
            return false;
        if (payload is not null)
            store.RecordWireWitness(PacketId(packet), payload);

        switch (packet)
        {
            case BedrockAddEntityPacket add:
                return store.ApplyAddEntity(add, worldGeneration);
            case BedrockAddPlayerPacket add:
                return store.ApplyAddPlayer(add, worldGeneration);
            case BedrockAddItemEntityPacket addItem:
                return store.ApplyAddItemEntity(addItem, worldGeneration);
            case BedrockRemoveEntityPacket remove:
                return store.ApplyRemoveEntity(remove.UniqueEntityId, worldGeneration);
            case BedrockSetEntityMotionPacket motion:
                return store.ApplySetEntityMotion(motion.RuntimeEntityId, motion.Velocity, motion.Tick, worldGeneration);
            case BedrockMovePlayerPacket move:
                return store.ApplyMovePlayer(move.RuntimeEntityId, move.Position, move.RiddenRuntimeEntityId, move.Tick, worldGeneration);
            case BedrockMobEffectPacket effect:
                return store.ApplyMobEffect(effect, worldGeneration);
            case BedrockUpdateAttributesPacket attributes:
                return store.ApplyUpdateAttributes(attributes, worldGeneration);
            case BedrockMobEquipmentPacket equipment:
                return store.ApplyMobEquipment(equipment, worldGeneration);
            case BedrockMobArmorEquipmentPacket armor:
                return store.ApplyMobArmorEquipment(armor, worldGeneration);
            case BedrockSetEntityDataPacket data:
                return store.ApplySetEntityData(data, worldGeneration);
            case BedrockSetEntityLinkPacket link:
                return store.ApplySetEntityLink(link, worldGeneration);
            case BedrockPlayerLocationPacket location:
                return store.ApplyPlayerLocation(location.EntityUniqueId, location.Position, location.Hide, worldGeneration);
            default:
                throw new BedrockCodecException($"The actor observation {packet.GetType().Name} is not supported.");
        }
    }

    private static int PacketId(object packet) => packet switch
    {
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
        BedrockSetEntityLinkPacket => 41,
        BedrockSetEntityMotionPacket => 40,
        BedrockPlayerLocationPacket => 326,
        _ => -1,
    };
}
