using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies world/terrain state: chunk load/unload, block and multi-block updates, block entities, time, world border variants, spawn position, view/simulation radii, plus the world effect events (level/block events, explosions, sounds). Requires the Terrain feature.</summary>
internal sealed class WorldApplier : IApplier
{
    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        // The world spawn is a SERVER fact rather than terrain, and it routinely arrives inside the join sequence, before the world exists. Handle it ahead of the world gate so an early announcement is recorded and publishes SpawnPositionChanged.
        if (packet is ClientboundSetDefaultSpawnPositionPacket spawn)
        {
            context.State.Server.WorldSpawn = new State.WorldSpawnPoint(spawn.Position, spawn.Angle);
            await context.PublishAsync(new SpawnPositionChanged(spawn.Position, spawn.Angle)).ConfigureAwait(false);
            return true;
        }

        if (context.State.WorldOrNull is null)
        {
            // World not yet built (join not processed). Terrain packets before join are dropped.
            return packet is ClientboundLevelChunkPacket or ClientboundMapChunkBulkPacket or ClientboundForgetLevelChunkPacket;
        }

        Game.World.World world = context.State.World;
        switch (packet)
        {
            case ClientboundLevelChunkPacket chunk:
                // A frame that is not ground-up carries ONLY the sections its bitmask names, so installing it as a whole column deletes every other section of that column. Vanilla's server sends exactly that frame whenever 64 distinct block changes land in one chunk in one tick, in place of the block-update and section-blocks-update forms, and it is only ever sent for a column the client already holds in full. See MergeColumnSections.
                if (chunk.FullChunk)
                {
                    world.LoadColumn(chunk.Column);
                    world.RemoveBlockEntityNbt(new ChunkPos(chunk.ChunkX, chunk.ChunkZ));
                }
                else
                    world.MergeColumnSections(chunk.Column);

                foreach (ChunkBlockEntity blockEntity in chunk.BlockEntities)
                    world.SetBlockEntityNbt(blockEntity.Position, blockEntity.Nbt);

                await context.PublishAsync(new ChunkLoaded(new ChunkPos(chunk.ChunkX, chunk.ChunkZ))).ConfigureAwait(false);
                return true;

            // The 1.8-only bulk form. Decoding it without this arm would have moved the marker one layer up rather than fixing anything: the columns would be parsed and then dropped, which reads from outside exactly like the unbound codec did. Each column is installed and announced individually, so a ChunkLoaded subscriber cannot tell how the column was framed on the wire, and terrain arriving in bulk is no longer second-class.
            case ClientboundMapChunkBulkPacket bulk:
                foreach (Game.World.ChunkColumn column in bulk.Columns)
                {
                    world.LoadColumn(column);
                    await context.PublishAsync(new ChunkLoaded(column.Position)).ConfigureAwait(false);
                }

                return true;

            case ClientboundForgetLevelChunkPacket forget:
                world.UnloadColumn(forget.Chunk);
                world.RemoveBlockEntityNbt(forget.Chunk);
                await context.PublishAsync(new ChunkUnloaded(forget.Chunk)).ConfigureAwait(false);
                return true;

            case ClientboundBlockUpdatePacket block:
                world.SetBlockStateId(block.Position, block.BlockStateId);
                await context.PublishAsync(new BlockChanged(block.Position, block.BlockStateId)).ConfigureAwait(false);
                return true;

            case ClientboundSectionBlocksUpdatePacket multi:
                await ApplyMultiBlockAsync(multi, world, context).ConfigureAwait(false);
                return true;

            case ClientboundBlockEntityDataPacket blockEntity:
                if (blockEntity.Nbt is NbtCompound nbt)
                    world.SetBlockEntityNbt(blockEntity.Position, nbt);

                return true;

            case ClientboundSetTimePacket time:
                world.SetTime(time.GameTime, time.DayTime);
                await context.PublishAsync(new TimeChanged(time.GameTime, time.DayTime)).ConfigureAwait(false);
                return true;

            case ClientboundInitializeBorderPacket border:
                world.SetBorder(new Game.World.WorldBorderState(
                    border.CenterX, border.CenterZ, border.OldSize, border.NewSize,
                    border.LerpTime, border.WarningBlocks, border.WarningTime));
                await context.PublishAsync(new WorldBorderChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetBorderCenterPacket center:
                UpdateBorder(world, b => new Game.World.WorldBorderState(
                    center.CenterX, center.CenterZ, b.Size, b.TargetSize, b.LerpTimeMillis, b.WarningBlocks, b.WarningTimeSeconds));
                await context.PublishAsync(new WorldBorderChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetBorderSizePacket size:
                UpdateBorder(world, b => new Game.World.WorldBorderState(
                    b.CenterX, b.CenterZ, size.Size, size.Size, 0, b.WarningBlocks, b.WarningTimeSeconds));
                await context.PublishAsync(new WorldBorderChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetBorderLerpSizePacket lerp:
                UpdateBorder(world, b => new Game.World.WorldBorderState(
                    b.CenterX, b.CenterZ, lerp.OldSize, lerp.NewSize, lerp.LerpTime, b.WarningBlocks, b.WarningTimeSeconds));
                await context.PublishAsync(new WorldBorderChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetBorderWarningDelayPacket warnDelay:
                UpdateBorder(world, b => new Game.World.WorldBorderState(
                    b.CenterX, b.CenterZ, b.Size, b.TargetSize, b.LerpTimeMillis, b.WarningBlocks, warnDelay.WarningDelay));
                await context.PublishAsync(new WorldBorderChanged()).ConfigureAwait(false);
                return true;

            case ClientboundSetBorderWarningDistancePacket warnDist:
                UpdateBorder(world, b => new Game.World.WorldBorderState(
                    b.CenterX, b.CenterZ, b.Size, b.TargetSize, b.LerpTimeMillis, warnDist.WarningBlocks, b.WarningTimeSeconds));
                await context.PublishAsync(new WorldBorderChanged()).ConfigureAwait(false);
                return true;

            case ClientboundLightUpdatePacket light:
                // own the standalone light packet and surface it. Per-section light storage is deferred (chunk-packet light is likewise decoded but not stored today); the client's physics and pathfinding do not consume light.
                await context.PublishAsync(new LightUpdated(light.ChunkX, light.ChunkZ)).ConfigureAwait(false);
                return true;

            case ClientboundChunksBiomesPacket biomes:
                // own the chunks-biomes packet and surface it. Per-section biome-buffer application is deferred (no column-level apply helper exists; biomes arrive with the chunk on join).
                await context.PublishAsync(new ChunkBiomesUpdated(biomes.Chunks.Count)).ConfigureAwait(false);
                return true;

            case ClientboundLevelParticlesPacket particles:
                await context.PublishAsync(new ParticleSpawned(
                    particles.Particle.TypeId, new Vec3d(particles.X, particles.Y, particles.Z), particles.Count)).ConfigureAwait(false);
                return true;

            case ClientboundStopSoundPacket stopSound:
                await context.PublishAsync(new SoundStopped(stopSound.Source, stopSound.Name)).ConfigureAwait(false);
                return true;

            case ClientboundSetChunkCacheCenterPacket:
                return true;

            case ClientboundSetChunkCacheRadiusPacket radius:
                context.State.Self.ViewDistance = radius.Radius;
                return true;

            case ClientboundSetSimulationDistancePacket sim:
                context.State.Self.SimulationDistance = sim.SimulationDistance;
                return true;

            case ClientboundLevelEventPacket levelEvent:
                await context.PublishAsync(new LevelEventOccurred(levelEvent.EffectId, levelEvent.Position, levelEvent.Data, levelEvent.Global)).ConfigureAwait(false);
                return true;

            case ClientboundBlockEventPacket blockEvent:
                await context.PublishAsync(new BlockEventOccurred(blockEvent.Position, blockEvent.B0, blockEvent.B1, blockEvent.BlockId)).ConfigureAwait(false);
                return true;

            case ClientboundExplodePacket explode:
                await context.PublishAsync(new ExplosionOccurred(explode.Center)).ConfigureAwait(false);
                return true;

            case ClientboundSoundPacket sound:
                // SoundEventHolder carries either a registry id or, for a sound outside the built-in registry, the name inline. Both are forwarded as SoundCodec.ReadHolder produced them (registry -> id with a null name, inline -> -1 with a name); a consumer that wants to match one specific sound has nothing to match on otherwise.
                await context.PublishAsync(new SoundPlayed(
                    sound.Source,
                    new Vec3d(sound.X / 8.0, sound.Y / 8.0, sound.Z / 8.0),
                    sound.Volume,
                    sound.Pitch,
                    sound.Sound.SoundId,
                    sound.Sound.InlineName
                        ?? JavaGameData.SoundName(context.Version.Version.Protocol, sound.Sound.SoundId))).ConfigureAwait(false);
                return true;

            // The name-addressed sound packets: minecraft:sound_effect on 1.8 and minecraft:custom_sound on 1.9 through 1.19.2. Both decoded to nothing a consumer could observe before, because neither had an applier at all; they now reach the same SoundPlayed a registry-id sound does. The 1.8 packet carries no source field, so it reports the ambient/master source id 0.
            case ClientboundNamedSoundPacket named:
                await context.PublishAsync(new SoundPlayed(
                    0, new Vec3d(named.X / 8.0, named.Y / 8.0, named.Z / 8.0), named.Volume, named.Pitch,
                    SoundName: named.SoundName)).ConfigureAwait(false);
                return true;

            case ClientboundCustomSoundPacket custom:
                await context.PublishAsync(new SoundPlayed(
                    custom.Source, new Vec3d(custom.X / 8.0, custom.Y / 8.0, custom.Z / 8.0), custom.Volume,
                    custom.Pitch, SoundName: custom.SoundName)).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Applies a section-level block update and announces every change.</summary>
    /// <remarks>
    /// <para>Published per change rather than batched, so one event type describes one block change however the server framed it, matching the single-block path. That is the same reason the bulk chunk arm above publishes per column. Flooding is not a real concern at the wire's own bound: vanilla's <c>ClientboundSectionBlocksUpdatePacket</c> covers ONE 16x16x16 section, so a single frame can carry at most 4096 changes, and the two live 1.8 corpora contain section updates of 1 change each. A consumer that wants coarser granularity can debounce; a consumer that never hears the change has no recourse at all.</para>
    /// </remarks>
    private static async ValueTask ApplyMultiBlockAsync(
        ClientboundSectionBlocksUpdatePacket packet, Game.World.World world, ApplierContext context)
    {
        if (packet.IsLegacy)
        {
            // Legacy (through 1.16.1) records pack (x:4 hi, z:4, y:8 lo) with an ABSOLUTE Y; the chunk origin comes from the two chunk ints, not from a SectionPos.
            int baseX = packet.LegacyChunkX << 4;
            int baseZ = packet.LegacyChunkZ << 4;
            foreach (SectionBlockChange change in packet.Changes)
            {
                int packed = change.PackedPosition & 0xFFFF;
                int localX = (packed >> 12) & 0xF;
                int localZ = (packed >> 8) & 0xF;
                int worldY = packed & 0xFF;
                var legacyPos = new BlockPos(baseX + localX, worldY, baseZ + localZ);
                world.SetBlockStateId(legacyPos, change.BlockStateId);
                await context.PublishAsync(new BlockChanged(legacyPos, change.BlockStateId)).ConfigureAwait(false);
            }

            return;
        }

        // SectionPos packs the section coordinates in the high bits (x:22, z:22, y:20).
        long sectionPos = packet.SectionPos;
        int sectionX = (int)(sectionPos >> 42);
        int sectionY = (int)(sectionPos << 44 >> 44);
        int sectionZ = (int)(sectionPos << 22 >> 42);

        foreach (SectionBlockChange change in packet.Changes)
        {
            int packed = change.PackedPosition;
            int localX = (packed >> 8) & 0xF;
            int localZ = (packed >> 4) & 0xF;
            int localY = packed & 0xF;
            var pos = new BlockPos((sectionX << 4) + localX, (sectionY << 4) + localY, (sectionZ << 4) + localZ);
            world.SetBlockStateId(pos, change.BlockStateId);
            await context.PublishAsync(new BlockChanged(pos, change.BlockStateId)).ConfigureAwait(false);
        }
    }

    private static void UpdateBorder(Game.World.World world, Func<Game.World.WorldBorderState, Game.World.WorldBorderState> update)
        => world.SetBorder(update(world.Border));
}
