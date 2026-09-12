using Umpk.Client.Internal;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Nbt;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// Pins the dimension bounds <see cref="WorldFactory"/> resolves: from the server's <c>minecraft:dimension_type</c> where an era puts one on the wire, and against built-in constants where it falls back, so the table cannot drift into a guess that merely looks plausible.
/// <para>The default bounds are which is the source the built-in dimension types in constructed from:</para>
/// <code>
/// OVERWORLD_MIN_Y = -64;  OVERWORLD_LEVEL_HEIGHT = 384; NETHER_MIN_Y    =   0;  NETHER_LEVEL_HEIGHT    = 256;  NETHER_GENERATION_HEIGHT = 128; END_MIN_Y       =   0;  END_LEVEL_HEIGHT       = 256;  END_GENERATION_HEIGHT    = 128;
/// </code>
/// <para>The distinction that matters, and that is easy to get wrong: the value which sizes a chunk column's section stack is <c>LEVEL_HEIGHT</c> (the dimension type's <c>height</c> field), NOT <c>GENERATION_HEIGHT</c> or <c>LOGICAL_HEIGHT</c>. Both of the latter are 128 in the nether, which is why "the nether is 128 tall" is a natural but wrong reading: the nether only GENERATES terrain over its bottom 8 sections, while its dimension type spans 16.</para>
/// <para>These bounds decide where every installed chunk column sits. <c>DecodedColumnPlacementTests</c> verifies that decoded columns use them.</para>
/// </summary>
public sealed class WorldFactoryBoundsTests
{
    private const int Modern = 770;

    [Theory]
    // dimensionName, expected MinY, expected Height (vanilla DimensionDefaults *_MIN_Y / *_LEVEL_HEIGHT)
    [InlineData("minecraft:overworld", -64, 384)]
    [InlineData("minecraft:the_nether", 0, 256)]
    [InlineData("minecraft:the_end", 0, 256)]
    public void CreateDimension_MatchesVanillaDimensionDefaults(string dimensionName, int minY, int height)
    {
        DimensionState dimension = Create(dimensionName, Modern);

        Assert.Equal(minY, dimension.MinY);
        Assert.Equal(height, dimension.Height);
        Assert.Equal(height / 16, dimension.SectionCount);
        Assert.Equal(minY + height, dimension.MaxY);
    }

    /// <summary>Skylight per dimension: the overworld has it, while the nether and the end do not. It was hardcoded true at both construction sites, which is wrong for two of the three built-ins.</summary>
    [Theory]
    [InlineData("minecraft:overworld", true)]
    [InlineData("minecraft:the_nether", false)]
    [InlineData("minecraft:the_end", false)]
    public void CreateDimension_MatchesVanillaSkylight(string dimensionName, bool hasSkylight)
        => Assert.Equal(hasSkylight, Create(dimensionName, Modern).HasSkylight);

    /// <summary>The overworld's floor is an ERA fact, not a constant. 1.18 (protocol 757) is where the world height extension landed; 1.17.1's built-in overworld is still <c>min_y = 0, height = 256</c>. Pinned because chunk columns now take the world's floor. Assuming -64 on protocols 47-756 would put every overworld column 64 blocks too low.</summary>
    [Theory]
    [InlineData(47, 0, 256)]
    [InlineData(340, 0, 256)]
    [InlineData(754, 0, 256)]
    [InlineData(756, 0, 256)]
    [InlineData(757, -64, 384)]
    [InlineData(770, -64, 384)]
    [InlineData(776, -64, 384)]
    public void CreateDimension_OverworldFloorFollowsTheWireLayout(int protocol, int minY, int height)
    {
        DimensionState dimension = Create("minecraft:overworld", protocol);

        Assert.Equal(minY, dimension.MinY);
        Assert.Equal(height, dimension.Height);
    }

    /// <summary>The dimension NAME must survive onto the state, because it is the only identity a consumer can key on. This is also the guard on the branch that produced the y=64 nether investigation: a name that failed to match would silently take the overworld fallback, and only the resolved bounds would say so.</summary>
    [Theory]
    [InlineData("minecraft:overworld")]
    [InlineData("minecraft:the_nether")]
    [InlineData("minecraft:the_end")]
    public void CreateDimension_KeepsTheDimensionName(string dimensionName)
        => Assert.Equal(dimensionName, Create(dimensionName, Modern).DimensionName.ToString());

    /// <summary>A modern datapack world cannot be reconstructed from its world name. If the configuration registry did not provide its dimension type, silently using overworld bounds would place every chunk at the wrong height and leave the client apparently connected with corrupt world state.</summary>
    [Fact]
    public void CreateDimension_UnknownModernDimensionWithoutRegistry_Fails()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => Create("mypack:skyblock", Modern));

        Assert.Contains("mypack:skyblock", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Before custom dimension registries existed, the historical name-keyed fallback remains valid. This control keeps strict modern registry handling from becoming a global no-registry policy.</summary>
    [Fact]
    public void CreateDimension_PreCustomRegistryUnknownName_KeepsTheLegacyFallback()
    {
        DimensionState dimension = Create("mypack:skyblock", 578);

        Assert.Equal(0, dimension.MinY);
        Assert.Equal(256, dimension.Height);
        Assert.Equal("mypack:skyblock", dimension.DimensionName.ToString());
    }

    /// <summary>1.20.5+ (766+) name the dimension type by NETWORK ID, resolved against the registry the server sent during configuration. The registry here deliberately orders the nether at id 0 and a datapack dimension at id 1 with bounds no built-in table could produce, so a pass proves the server's table was consulted rather than the world name.</summary>
    [Fact]
    public void CreateDimension_ResolvesTheServersRegistryByNetworkId()
    {
        RegistryAccess registries = WithDimensionTypes(
            (Identifier.Minecraft("the_nether"), new DimensionTypeDefinition(0, 256, HasSkylight: false)),
            (new Identifier("mypack", "skyblock"), new DimensionTypeDefinition(48, 128, HasSkylight: true)));

        DimensionState dimension = WorldFactory.CreateDimension(
            new CommonWorldSetup("mypack:skyblock", 1), registries, Modern);

        Assert.Equal(48, dimension.MinY);
        Assert.Equal(128, dimension.Height);
        Assert.Equal(8, dimension.SectionCount);
        Assert.True(dimension.HasSkylight);
    }

    /// <summary>An advertised modern registry makes the spawn-info network id authoritative.</summary>
    [Fact]
    public void CreateDimension_MissingModernRegistryId_FailsInsteadOfGuessingFromTheWorldName()
    {
        RegistryAccess registries = WithDimensionTypes(
            (Identifier.Minecraft("overworld"), new DimensionTypeDefinition(-64, 384, HasSkylight: true)));

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => WorldFactory.CreateDimension(
            new CommonWorldSetup("mypack:skyblock", 9), registries, Modern));

        Assert.Contains("network id 9", error.Message, StringComparison.Ordinal);
    }

    /// <summary>1.16/1.16.1 and 1.19-1.20.4 (735/736, 759-765) name the dimension type by RESOURCE KEY instead.</summary>
    [Fact]
    public void CreateDimension_ResolvesTheServersRegistryByResourceKey()
    {
        RegistryAccess registries = WithDimensionTypes(
            (Identifier.Minecraft("overworld"), new DimensionTypeDefinition(-64, 384, HasSkylight: true)),
            (new Identifier("mypack", "attic"), new DimensionTypeDefinition(96, 64, HasSkylight: false)));

        DimensionState dimension = WorldFactory.CreateDimension(
            new CommonWorldSetup("mypack:loft", 0) { DimensionTypeName = "mypack:attic" }, registries, 763);

        Assert.Equal(96, dimension.MinY);
        Assert.Equal(64, dimension.Height);
        Assert.Equal("mypack:loft", dimension.DimensionName.ToString());
    }

    /// <summary>Protocols 1.16.2-1.18.2 carry the current dimension type inline in JoinGame and Respawn as a bare datapack compound, so no registry is needed or available.</summary>
    [Fact]
    public void CreateDimension_ReadsTheInlineDimensionTypeCompound()
    {
        var element = new NbtCompound();
        element.PutInt("min_y", 0);
        element.PutInt("height", 256);
        element.PutBool("has_skylight", false);

        DimensionState dimension = WorldFactory.CreateDimension(
            new CommonWorldSetup("minecraft:the_nether", 0) { InlineType = element }, registries: null, 758);

        Assert.Equal(0, dimension.MinY);
        Assert.Equal(256, dimension.Height);
        Assert.False(dimension.HasSkylight);
    }

    /// <summary>A malformed inline type (a height vanilla's own codec would reject) must not size a column. The resolution falls through to the built-in table rather than producing a nonsense world.</summary>
    [Fact]
    public void CreateDimension_RejectsAnUnusableInlineType()
    {
        var element = new NbtCompound();
        element.PutInt("min_y", 3);
        element.PutInt("height", 7);

        DimensionState dimension = WorldFactory.CreateDimension(
            new CommonWorldSetup("minecraft:the_nether", 0) { InlineType = element }, registries: null, 758);

        Assert.Equal(0, dimension.MinY);
        Assert.Equal(256, dimension.Height);
    }

    /// <summary>A legacy respawn passes the signed dimension index in the type-id slot. It must never be read as a registry network id, and here it cannot be, because the registry is empty on those protocols: dimension -1 is the nether by NAME and resolves to the pre-1.18 nether bounds.</summary>
    [Fact]
    public void CreateDimension_LegacyDimensionIndex_IsNotARegistryId()
    {
        DimensionState dimension = WorldFactory.CreateDimension(
            new CommonWorldSetup("minecraft:the_nether", -1), Umpk.Data.Java.JavaGameData.Registries(47), 47);

        Assert.Equal(0, dimension.MinY);
        Assert.Equal(256, dimension.Height);
        Assert.Equal("minecraft:the_nether", dimension.DimensionName.ToString());
    }

    /// <summary>Below 1.20.5 the type-id slot is not a registry id even when a registry IS populated: 764/765 decode it as -1 and 735-763 fill it with a literal 0. Measured live on 1.16.5 before this guard existed: the nether resolved through id 0 to the registry's FIRST entry, the overworld, and the client ran the nether as a world with skylight. Pre-1.18 the two share bounds, so only the skylight flag showed it; on 759-763 it would have moved every block.</summary>
    [Theory]
    [InlineData(754, false)]
    [InlineData(763, false)]
    [InlineData(765, false)]
    [InlineData(766, true)]
    public void CreateDimension_TypeIdIsOnlyARegistryIdFrom766(int protocol, bool resolvesThroughTheRegistry)
    {
        RegistryAccess registries = WithDimensionTypes(
            (Identifier.Minecraft("overworld"), new DimensionTypeDefinition(-64, 384, HasSkylight: true)),
            (Identifier.Minecraft("the_nether"), new DimensionTypeDefinition(0, 256, HasSkylight: false)));

        // Type id 0 is the overworld entry. A nether world must not pick it up below 766.
        DimensionState dimension = WorldFactory.CreateDimension(
            new CommonWorldSetup("minecraft:the_nether", 0), registries, protocol);

        Assert.Equal(resolvesThroughTheRegistry ? -64 : 0, dimension.MinY);
        Assert.Equal(resolvesThroughTheRegistry, dimension.HasSkylight);
    }

    private static DimensionState Create(string dimensionName, int protocol)
        => WorldFactory.CreateDimension(new CommonWorldSetup(dimensionName, 0), registries: null, protocol);

    private static RegistryAccess WithDimensionTypes(params (Identifier Key, DimensionTypeDefinition Value)[] entries)
    {
        var builder = new RegistryBuilder<DimensionTypeDefinition>(RegistryIds.DimensionType);
        for (int id = 0; id < entries.Length; id++)
            builder.Add(id, entries[id].Key, entries[id].Value);

        return RegistryAccess.FromSnapshot(
            Umpk.Data.Java.JavaGameData.Registries(Modern).Snapshot.With(builder.Build()));
    }
}
