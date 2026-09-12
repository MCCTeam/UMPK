using Umpk.Game.Registries;
using Xunit;

namespace Umpk.Game.Tests.Registries;

public class RegistryTests
{
    private static Registry<BiomeDefinition> BuildDense() =>
        new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 3)
            .Add(0, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Add(1, Identifier.Minecraft("desert"), new BiomeDefinition())
            .Add(2, Identifier.Minecraft("ocean"), new BiomeDefinition())
            .Build();

    private static Registry<BiomeDefinition> BuildSparse() =>
        new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome, 3)
            .Add(10, Identifier.Minecraft("plains"), new BiomeDefinition())
            .Add(5, Identifier.Minecraft("desert"), new BiomeDefinition())
            .Add(99, Identifier.Minecraft("ocean"), new BiomeDefinition())
            .Build();

    [Fact]
    public void Build_SetsRegistryIdAndCount()
    {
        var registry = BuildDense();
        Assert.Equal(RegistryIds.Biome, registry.RegistryId);
        Assert.Equal(3, registry.Count);
    }

    [Fact]
    public void LookupByNetworkId_Dense_ReturnsEntry()
    {
        var registry = BuildDense();
        var entry = registry[1];
        Assert.Equal(1, entry.NetworkId);
        Assert.Equal(Identifier.Minecraft("desert"), entry.Id);
        Assert.False(entry.IsDefault);
    }

    [Fact]
    public void LookupByNetworkId_Sparse_ReturnsEntry()
    {
        var registry = BuildSparse();
        Assert.True(registry.TryGet(99, out var entry));
        Assert.Equal(Identifier.Minecraft("ocean"), entry.Id);
        Assert.Equal(99, entry.NetworkId);
    }

    [Fact]
    public void LookupByKey_ReturnsEntry()
    {
        var registry = BuildSparse();
        Assert.True(registry.TryGet(Identifier.Minecraft("plains"), out var entry));
        Assert.Equal(10, entry.NetworkId);
    }

    [Fact]
    public void MissingNetworkId_TryGetReturnsFalse()
    {
        var registry = BuildDense();
        Assert.False(registry.TryGet(42, out var entry));
        Assert.True(entry.IsDefault);
    }

    [Fact]
    public void MissingNetworkId_IndexerThrows()
    {
        var registry = BuildSparse();
        Assert.Throws<KeyNotFoundException>(() => registry[3]);
    }

    [Fact]
    public void MissingKey_TryGetReturnsFalse()
    {
        var registry = BuildDense();
        Assert.False(registry.TryGet(Identifier.Minecraft("nether"), out _));
        Assert.False(registry.ContainsKey(Identifier.Minecraft("nether")));
    }

    [Fact]
    public void MissingKey_IndexerThrows()
    {
        var registry = BuildDense();
        Assert.Throws<KeyNotFoundException>(() => registry[Identifier.Minecraft("nether")]);
    }

    [Fact]
    public void ContainsAndCrossLookups_Agree()
    {
        var registry = BuildSparse();
        Assert.True(registry.ContainsNetworkId(5));
        Assert.False(registry.ContainsNetworkId(6));

        Assert.True(registry.TryGetKey(5, out var key));
        Assert.Equal(Identifier.Minecraft("desert"), key);

        Assert.True(registry.TryGetNetworkId(Identifier.Minecraft("desert"), out int id));
        Assert.Equal(5, id);

        Assert.False(registry.TryGetKey(6, out _));
        Assert.False(registry.TryGetNetworkId(Identifier.Minecraft("void"), out _));
    }

    [Fact]
    public void TryGetValue_ResolvesDefinition()
    {
        var registry = BuildDense();
        Assert.True(registry.TryGetValue(Identifier.Minecraft("ocean"), out var value));
        Assert.NotNull(value);
        Assert.False(registry.TryGetValue(Identifier.Minecraft("nope"), out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void Enumeration_YieldsInsertionOrder()
    {
        var registry = BuildSparse();
        var ids = registry.Select(e => e.Id.Path).ToArray();
        Assert.Equal(new[] { "plains", "desert", "ocean" }, ids);
    }

    [Fact]
    public void Builder_DuplicateNetworkId_Throws()
    {
        var builder = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome)
            .Add(0, Identifier.Minecraft("a"), new BiomeDefinition());
        Assert.Throws<ArgumentException>(() => builder.Add(0, Identifier.Minecraft("b"), new BiomeDefinition()));
    }

    [Fact]
    public void Builder_DuplicateKey_Throws()
    {
        var builder = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome)
            .Add(0, Identifier.Minecraft("a"), new BiomeDefinition());
        Assert.Throws<ArgumentException>(() => builder.Add(1, Identifier.Minecraft("a"), new BiomeDefinition()));
    }

    [Fact]
    public void Builder_NullValue_Throws()
    {
        var builder = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome);
        Assert.Throws<ArgumentNullException>(() => builder.Add(0, Identifier.Minecraft("a"), null!));
    }

    [Fact]
    public void Builder_IsSingleUse()
    {
        var builder = new RegistryBuilder<BiomeDefinition>(RegistryIds.Biome)
            .Add(0, Identifier.Minecraft("a"), new BiomeDefinition());
        builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.Add(1, Identifier.Minecraft("b"), new BiomeDefinition()));
    }

    [Fact]
    public void Factory_FromEntries_BuildsEquivalentRegistry()
    {
        ReadOnlySpan<int> ids = [0, 1];
        Identifier[] keys = [Identifier.Minecraft("a"), Identifier.Minecraft("b")];
        BiomeDefinition[] values = [new BiomeDefinition(), new BiomeDefinition()];
        var registry = Registry.FromEntries<BiomeDefinition>(RegistryIds.Biome, ids, keys, values);
        Assert.Equal(2, registry.Count);
        Assert.True(registry.ContainsKey(Identifier.Minecraft("b")));
    }

    [Fact]
    public void Factory_FromEntries_MismatchedLengths_Throws()
    {
        int[] ids = [0, 1];
        Identifier[] keys = [Identifier.Minecraft("a")];
        BiomeDefinition[] values = [new BiomeDefinition()];
        Assert.Throws<ArgumentException>(() =>
            Registry.FromEntries<BiomeDefinition>(RegistryIds.Biome, ids, keys, values));
    }
}
