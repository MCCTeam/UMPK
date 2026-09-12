namespace Umpk.Game.World;

/// <summary>A block-entity-type registry entry (<c>minecraft:block_entity_type</c>). The version-independent facts are thin today; the payload lives in each block entity's <see cref="BlockEntityData.Nbt"/>. Defined here (rather than in the shared registry definitions) because block entities are owned by the world storage slice; a <see cref="Umpk.Game.Registries.Registry{T}"/> of these is built by the Java codec layer's registry loader.</summary>
public sealed record BlockEntityTypeDefinition;
