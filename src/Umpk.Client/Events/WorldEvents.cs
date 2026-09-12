using Umpk.Geometry;

namespace Umpk.Client.Events;

/// <summary>Raised when a chunk column is loaded into the world.</summary>
public sealed record ChunkLoaded(ChunkPos Position) : IClientEvent;

/// <summary>Raised when a chunk column is unloaded.</summary>
public sealed record ChunkUnloaded(ChunkPos Position) : IClientEvent;

/// <summary>Raised when a single block state changes.</summary>
public sealed record BlockChanged(BlockPos Position, int BlockStateId) : IClientEvent;

/// <summary>Raised when the world time changes.</summary>
public sealed record TimeChanged(long WorldAge, long TimeOfDay) : IClientEvent;

/// <summary>Raised when the world border state changes.</summary>
public sealed record WorldBorderChanged : IClientEvent;

/// <summary>Raised for an explosion.</summary>
public sealed record ExplosionOccurred(Vec3d Center) : IClientEvent;

/// <summary>Raised for a level event (world effect: piston, note block, and so on).</summary>
public sealed record LevelEventOccurred(int EffectId, BlockPos Position, int Data, bool Global) : IClientEvent;

/// <summary>Raised for a block event (piston, chest lid, note block).</summary>
public sealed record BlockEventOccurred(BlockPos Position, byte ActionId, byte ActionParam, int BlockId) : IClientEvent;

/// <summary>Raised for a sound effect.</summary>
/// <param name="Source">The sound category (master, music, weather, block, hostile, player, ...).</param>
/// <param name="Position">The sound position in world coordinates.</param>
/// <param name="Volume">The volume the server asked for.</param>
/// <param name="Pitch">The pitch the server asked for.</param>
/// <param name="SoundId">The sound registry id for a registry-addressed sound, or -1 when the sound was addressed by name (the 1.8 <c>sound_effect</c> and 1.9-1.19.2 <c>custom_sound</c> packets) or carried inline.</param>
/// <param name="SoundName">The sound identity when the wire carried one: the literal name from a name-addressed packet, or the inline name a modern server sends for a sound outside the built-in registry. Null when the sound was addressed only by registry id, which is what a vanilla server sends for its own sounds; resolving that id to a name needs a per-protocol sound table the data package does not carry yet. Consumers matching a specific sound must handle null rather than assume identity is always available.</param>
public sealed record SoundPlayed(
    int Source,
    Vec3d Position,
    float Volume,
    float Pitch,
    int SoundId = -1,
    string? SoundName = null) : IClientEvent;

/// <summary>Raised when the server default spawn position is set.</summary>
public sealed record SpawnPositionChanged(BlockPos Position, float Angle) : IClientEvent;

/// <summary>Raised when a standalone light-update packet arrives for a chunk column.</summary>
public sealed record LightUpdated(int ChunkX, int ChunkZ) : IClientEvent;

/// <summary>Raised when a chunk-biomes update arrives for one or more columns.</summary>
public sealed record ChunkBiomesUpdated(int ChunkCount) : IClientEvent;

/// <summary>Raised when the world spawns a particle effect.</summary>
public sealed record ParticleSpawned(int ParticleTypeId, Vec3d Position, int Count) : IClientEvent;

/// <summary>Raised when the server stops a sound; a null field means "all" for that dimension.</summary>
public sealed record SoundStopped(int? Source, string? SoundName) : IClientEvent;
