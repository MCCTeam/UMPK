using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Game.Tests.Registries;

public class RegistrySnapshotTests
{
    [Fact]
    public void Snapshot_ContainsAllRegistries()
    {
        var snapshot = FakeRegistries.FullSnapshot();
        Assert.Equal(9, snapshot.Count);
        Assert.Contains(RegistryIds.Block, snapshot.RegistryIds);
        Assert.Contains(RegistryIds.Menu, snapshot.RegistryIds);
    }

    [Fact]
    public void TypedTryGet_ChecksElementType()
    {
        var snapshot = FakeRegistries.FullSnapshot();
        Assert.True(snapshot.TryGetRegistry<BlockDefinition>(RegistryIds.Block, out _));
        // Wrong element type resolves to false, not a cast exception.
        Assert.False(snapshot.TryGetRegistry<ItemDefinition>(RegistryIds.Block, out _));
    }

    [Fact]
    public void With_ProducesReplacementWithoutMutatingOriginal()
    {
        var original = FakeRegistries.FullSnapshot();
        var replacementBiomes = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 2)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Add(1, Identifier.Minecraft("nether_wastes"), new BiomeDefinition())
            .Build();

        var updated = original.With(replacementBiomes);

        // Original snapshot is unchanged (immutability): still one biome.
        Assert.True(original.TryGetRegistry<BiomeDefinition>(RegistryIds.Biome, out var originalBiomes));
        Assert.Single(originalBiomes);

        // The replacement has the server-sent extra biome.
        Assert.True(updated.TryGetRegistry<BiomeDefinition>(RegistryIds.Biome, out var updatedBiomes));
        Assert.Equal(2, updatedBiomes.Count);

        // Untouched registries are shared into the new snapshot.
        Assert.True(updated.TryGetRegistry(RegistryIds.Block, out _));
    }

    [Fact]
    public void SnapshotBuilder_IsSingleUse()
    {
        var builder = new RegistrySnapshotBuilder()
            .Add(new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 1)
                .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition()).Build());
        builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void RegistryAccess_OverDefaultThenOverride_ReflectsSwap()
    {
        // Models the phase swap: defaults, then a server-override snapshot rebuilt wholesale.
        var access1 = RegistryAccess.FromSnapshot(FakeRegistries.FullSnapshot());
        Assert.Single(access1.Biomes);

        var overrideBiomes = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 2)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Add(1, Identifier.Minecraft("the_end"), new BiomeDefinition())
            .Build();
        var access2 = RegistryAccess.FromSnapshot(access1.Snapshot.With(overrideBiomes));

        // The first access is untouched; the second is a distinct immutable view.
        Assert.Single(access1.Biomes);
        Assert.Equal(2, access2.Biomes.Count);
    }
}
