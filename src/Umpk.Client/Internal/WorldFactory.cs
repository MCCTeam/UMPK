using Umpk.Game.Registries;
using Umpk.Game.World;

namespace Umpk.Client.Internal;

/// <summary>Builds the world's dimension/biome scaffolding for a client session. The dimension's vertical bounds come from the SERVER's own <c>minecraft:dimension_type</c> whenever the era puts one on the wire; the name-keyed vanilla table is the fallback for the eras that send nothing and for a server whose registry did not carry the entry.</summary>
/// <remarks>
/// <para>Why the bounds have to be right rather than merely consistent: a world's floor is what every installed chunk column is bound to (<c>World.LoadColumn</c>), so a wrong floor moves every block in the world. Guessing from the world NAME is only ever an approximation, and it is wrong outright for any datapack dimension.</para>
/// </remarks>
internal static class WorldFactory
{
    /// <summary>The first protocol whose spawn-info block carries a dimension-type registry id is 1.20.5, where the resource-key string became a plain VarInt id. Below it the slot is not an id and must never be resolved as one: 764/765 decode it as -1, 735-763 fill it with a literal 0, and 47-404 put the legacy signed dimension index there. Reading zero as a registry id can incorrectly give a legacy Nether world the Overworld's skylight and bounds.</summary>
    private const int FirstDimensionTypeIdProtocol = 766;

    /// <summary>1.16 is the first supported era with datapack-defined dimensions. From this point an unknown world cannot be sized honestly without the server's dimension-type definition.</summary>
    private const int FirstCustomDimensionProtocol = 735;

    /// <summary>Resolves the dimension for a join/respawn. Preference order, most authoritative first: the current type's own inline compound, then the server's registry by resource key, then the server's registry by network id, then the vanilla built-in table keyed on the world name.</summary>
    public static DimensionState CreateDimension(CommonWorldSetup setup, RegistryAccess? registries, int protocol)
    {
        Identifier worldName = Identifier.TryParse(setup.DimensionName, out Identifier parsed)
            ? parsed
            : Identifier.Minecraft("overworld");

        Registry<DimensionTypeDefinition>? types = registries?.DimensionTypes;

        // 751-758 send the current dimension type inline, so no registry lookup is needed or possible.
        if (DimensionTypes.TryReadElement(setup.InlineType, out DimensionTypeDefinition inline))
            return Bind(inline, worldName);

        // 735/736 and 759-765 send a dimension-type RESOURCE KEY; 766+ send a network id. Both resolve against the same registry, which is only non-empty once the server's own registry data has been observed.
        if (types is not null && types.Count > 0)
        {
            if (setup.DimensionTypeName is { Length: > 0 } typeName
                && Identifier.TryParse(typeName, out Identifier typeKey)
                && types.TryGet(typeKey, out RegistryEntry<DimensionTypeDefinition> byKey))
                return new DimensionState(byKey, worldName);

            if (protocol >= FirstDimensionTypeIdProtocol
                && types.TryGet(setup.DimensionTypeId, out RegistryEntry<DimensionTypeDefinition> byId))
                return new DimensionState(byId, worldName);

            if (setup.DimensionTypeName is { Length: > 0 } missingType)
            {
                throw new InvalidDataException(
                    $"The server's dimension-type registry does not contain required key {missingType}.");
            }

            if (protocol >= FirstDimensionTypeIdProtocol)
            {
                throw new InvalidDataException(
                    $"The server's dimension-type registry does not contain required network id {setup.DimensionTypeId} " +
                    $"for world {worldName}.");
            }
        }

        if (protocol >= FirstCustomDimensionProtocol
            && !DimensionTypes.TryVanillaDefinition(worldName, protocol, out _))
        {
            string reference = setup.DimensionTypeName is { Length: > 0 } typeName
                ? $"key {typeName}"
                : protocol >= FirstDimensionTypeIdProtocol
                    ? $"network id {setup.DimensionTypeId}"
                    : "a server-supplied definition";
            throw new InvalidDataException(
                $"World {worldName} requires dimension type {reference}, but no usable server definition was installed.");
        }

        return Bind(DimensionTypes.FallbackFor(setup.DimensionName, protocol), worldName);
    }

    public static Registry<BiomeDefinition> EmptyBiomes()
        => new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Build();

    public static Registry<BlockDefinition> EmptyBlocks()
        => new RegistryBuilder<BlockDefinition>(RegistryIds.Block)
            .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0))
            .Build();

    /// <summary>Wraps a resolved definition in a single-entry registry so it has a registry entry to bind to.</summary>
    private static DimensionState Bind(DimensionTypeDefinition definition, Identifier worldName)
    {
        Registry<DimensionTypeDefinition> registry =
            new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType)
                .Add(0, worldName, definition)
                .Build();
        return new DimensionState(registry[0], worldName);
    }
}
