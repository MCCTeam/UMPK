using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java;

/// <summary>World-family timelines: block changes, chunk metadata, world metadata, audio-visual effects, and map data. Several 1.8 packets use different registry identifiers than their modern counterparts (for example <c>update_time</c> vs <c>set_time</c>); those legacy names are registered as aliases onto the same timeline, whose identity is the modern name.</summary>
internal static class WorldBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        WorldBlockCodecs.DeclareBlockChangedAck(bindings);
        WorldBlockCodecs.DeclareBlockDestruction(bindings);
        WorldBlockCodecs.DeclareBlockEntityData(bindings);
        WorldBlockCodecs.DeclareBlockEvent(bindings);
        WorldBlockCodecs.DeclareBlockUpdate(bindings);
        WorldBlockCodecs.DeclareSectionBlocksUpdate(bindings);
        WorldBlockCodecs.DeclareSetCommandBlock(bindings);
        WorldBorderCodecs.DeclareInitializeBorder(bindings);
        WorldBorderCodecs.DeclareSetBorderCenter(bindings);
        WorldBorderCodecs.DeclareSetBorderLerpSize(bindings);
        WorldBorderCodecs.DeclareSetBorderSize(bindings);
        WorldBorderCodecs.DeclareSetBorderWarningDelay(bindings);
        WorldBorderCodecs.DeclareSetBorderWarningDistance(bindings);
        WorldBorderCodecs.DeclareWorldBorder(bindings);
        WorldEffectCodecs.DeclareCustomSound(bindings);
        WorldEffectCodecs.DeclareExplode(bindings);
        WorldEffectCodecs.DeclareLevelEvent(bindings);
        WorldEffectCodecs.DeclareLevelParticles(bindings);
        WorldEffectCodecs.DeclareSound(bindings);
        WorldEffectCodecs.DeclareSoundEffect(bindings);
        WorldEffectCodecs.DeclareSoundEntity(bindings);
        WorldEffectCodecs.DeclareStopSound(bindings);
        WorldMapCodecs.DeclareMapItemData(bindings);
        WorldStateCodecs.DeclareChangeDifficulty(bindings);
        WorldStateCodecs.DeclareChunkBatchFinished(bindings);
        WorldStateCodecs.DeclareChunkBatchReceived(bindings);
        WorldStateCodecs.DeclareChunkBatchStart(bindings);
        WorldStateCodecs.DeclareChunksBiomes(bindings);
        WorldStateCodecs.DeclareForgetLevelChunk(bindings);
        WorldStateCodecs.DeclareGameEvent(bindings);
        WorldStateCodecs.DeclareLightUpdate(bindings);
        WorldStateCodecs.DeclareRespawn(bindings);
        WorldStateCodecs.DeclareSetChunkCacheCenter(bindings);
        WorldStateCodecs.DeclareSetChunkCacheRadius(bindings);
        WorldStateCodecs.DeclareSetDefaultSpawnPosition(bindings);
        WorldStateCodecs.DeclareSetSimulationDistance(bindings);
        WorldStateCodecs.DeclareSetTime(bindings);
    }
}
