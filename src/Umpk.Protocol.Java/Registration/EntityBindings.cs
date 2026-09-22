using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>Entity-family timelines: spawns, movement, metadata, effects, equipment, attributes, and the serverbound movement/interaction sends. The 1.8 dataset spells several clientbound entity packets with legacy identifiers (for example <c>entity_teleport</c> vs <c>teleport_entity</c>, <c>entity_effect</c> vs <c>update_mob_effect</c>); those names are registered as aliases onto the modern packet's timeline.</summary>
internal static class EntityBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        EntityDataCodecs.DeclareSetEntityData(bindings);
        EntityEffectCodecs.DeclareRemoveMobEffect(bindings);
        EntityEffectCodecs.DeclareUpdateMobEffect(bindings);
        EntityEquipmentCodecs.DeclareSetEquipment(bindings);
        EntityEquipmentCodecs.DeclareUpdateAttributes(bindings);
        EntityMoveCodecs.DeclareEntity(bindings);
        EntityMoveCodecs.DeclareEntityPositionSync(bindings);
        EntityMoveCodecs.DeclareMoveEntityPos(bindings);
        EntityMoveCodecs.DeclareMoveEntityPosRot(bindings);
        EntityMoveCodecs.DeclareMoveEntityRot(bindings);
        EntityMoveCodecs.DeclarePlayerPosition(bindings);
        EntityMoveCodecs.DeclareRotateHead(bindings);
        EntityMoveCodecs.DeclareSetEntityMotion(bindings);
        EntityMoveCodecs.DeclareTeleportEntity(bindings);
        EntityServerboundCodecs.DeclareAcceptTeleportation(bindings);
        EntityServerboundCodecs.DeclareAttack(bindings);
        EntityServerboundCodecs.DeclareInteract(bindings);
        EntityServerboundCodecs.DeclareMovePlayer(bindings);
        EntityServerboundCodecs.DeclareMovePlayerPos(bindings);
        EntityServerboundCodecs.DeclareMovePlayerPosRot(bindings);
        EntityServerboundCodecs.DeclareMovePlayerRot(bindings);
        EntityServerboundCodecs.DeclareMovePlayerStatusOnly(bindings);
        EntityServerboundCodecs.DeclareMoveVehicle(bindings);
        EntityServerboundCodecs.DeclarePaddleBoat(bindings);
        EntityServerboundCodecs.DeclarePlayerAction(bindings);
        EntityServerboundCodecs.DeclarePlayerCommand(bindings);
        EntityServerboundCodecs.DeclarePlayerInput(bindings);
        EntityServerboundCodecs.DeclareSetCarriedItem(bindings);
        EntityServerboundCodecs.DeclareSteerVehicle(bindings);
        EntityServerboundCodecs.DeclarePunch(bindings);
        EntityServerboundCodecs.DeclareSwing(bindings);
        EntityServerboundCodecs.DeclareTeleportToEntity(bindings);
        EntitySpawnCodecs.DeclareAddEntity(bindings);
        EntitySpawnCodecs.DeclareAddExperienceOrb(bindings);
        EntitySpawnCodecs.DeclareAddMob(bindings);
        EntitySpawnCodecs.DeclareAddPainting(bindings);
        EntitySpawnCodecs.DeclareAddPlayer(bindings);
        EntitySpawnCodecs.DeclareRemoveEntities(bindings);
        EntitySpawnCodecs.DeclareSpawnWeatherEntity(bindings);
        EntityStateCodecs.DeclareAnimate(bindings);
        EntityStateCodecs.DeclareDamageEvent(bindings);
        EntityStateCodecs.DeclareEntityEvent(bindings);
        EntityStateCodecs.DeclareHurtAnimation(bindings);
        EntityStateCodecs.DeclareSetCamera(bindings);
        EntityStateCodecs.DeclareSetEntityLink(bindings);
        EntityStateCodecs.DeclareSetExperience(bindings);
        EntityStateCodecs.DeclareSetHealth(bindings);
        EntityStateCodecs.DeclareSetHeldSlot(bindings);
        EntityStateCodecs.DeclareSetPassengers(bindings);
        EntityStateCodecs.DeclareSwingAnimation(bindings);
        EntityStateCodecs.DeclareTakeItemEntity(bindings);
        EntityStateCodecs.DeclareUseBed(bindings);
    }
}
