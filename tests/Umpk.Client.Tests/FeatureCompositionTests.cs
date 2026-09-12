using Umpk.Client;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class FeatureCompositionTests
{
    [Fact]
    public void Pathfinding_Implies_Physics_And_Terrain()
    {
        var features = new ClientFeatures { Terrain = false, Physics = false, Pathfinding = true };
        ClientFeatures normalized = features.Normalized();
        Assert.True(normalized.Physics);
        Assert.True(normalized.Terrain);
    }

    [Fact]
    public void Physics_Implies_Terrain()
    {
        var features = new ClientFeatures { Terrain = false, Physics = true, Pathfinding = false };
        ClientFeatures normalized = features.Normalized();
        Assert.True(normalized.Terrain);
    }

    [Fact]
    public void DisabledEntities_State_Throws()
    {
        var state = new ClientState(new ClientFeatures { Entities = false, Physics = false, Pathfinding = false, Terrain = true, Inventory = true });
        Assert.Throws<FeatureDisabledException>(() => state.Entities);
    }

    [Fact]
    public void DisabledInventory_State_Throws()
    {
        var state = new ClientState(new ClientFeatures { Inventory = false, Physics = false, Pathfinding = false, Terrain = true, Entities = true });
        Assert.Throws<FeatureDisabledException>(() => state.Inventory);
    }

    [Fact]
    public void EnabledEntities_State_Available()
    {
        var state = new ClientState(new ClientFeatures());
        Assert.NotNull(state.Entities);
    }
}
