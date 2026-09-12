using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Game.Tests.Registries;

public class RegistryAccessTests
{
    [Fact]
    public void FromSnapshot_ExposesTypedRegistries()
    {
        var access = RegistryAccess.FromSnapshot(FakeRegistries.FullSnapshot());

        Assert.True(access.Blocks.ContainsKey(Identifier.Minecraft("air")));
        Assert.True(access.Items.ContainsKey(Identifier.Minecraft("air")));
        Assert.True(access.EntityTypes.ContainsKey(Identifier.Minecraft("zombie")));
        Assert.True(access.DimensionTypes.ContainsKey(Identifier.Minecraft("overworld")));
        Assert.True(access.Biomes.ContainsKey(Identifier.Minecraft("plains")));
        Assert.True(access.Enchantments.ContainsKey(Identifier.Minecraft("sharpness")));
        Assert.True(access.Attributes.ContainsKey(Identifier.Minecraft("generic.max_health")));
    }

    [Fact]
    public void TryGetRegistry_ResolvesByRegistryId()
    {
        var access = RegistryAccess.FromSnapshot(FakeRegistries.FullSnapshot());
        Assert.True(access.TryGetRegistry(RegistryIds.EntityType, out var registry));
        Assert.Equal(RegistryIds.EntityType, registry.RegistryId);
        Assert.False(access.TryGetRegistry(Identifier.Minecraft("does_not_exist"), out _));
    }

    [Fact]
    public void FromSnapshot_MissingRequiredRegistry_Throws()
    {
        var partial = new RegistrySnapshotBuilder()
            .Add(new RegistryBuilder<BlockDefinition>(RegistryIds.Block, 1)
                .Add(0, Identifier.Minecraft("air"), new BlockDefinition(0, 0, 0)).Build())
            .Build();

        Assert.Throws<ArgumentException>(() => RegistryAccess.FromSnapshot(partial));
    }

    [Fact]
    public void DimensionTypeDefinition_MaxYIsMinPlusHeight()
    {
        var access = RegistryAccess.FromSnapshot(FakeRegistries.FullSnapshot());
        var overworld = access.DimensionTypes[Identifier.Minecraft("overworld")].Value;
        Assert.Equal(-64, overworld.MinY);
        Assert.Equal(320, overworld.MaxY);
    }
}
