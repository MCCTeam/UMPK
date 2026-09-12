using Umpk.Game.Entities;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.Entities;

/// <summary><see cref="EntityStore.Nearest"/>, <see cref="EntityStore.OfType(string)"/>, <see cref="EntityStore.OfType(Identifier)"/>, and <see cref="EntityStore.NearestFirst"/> provide nearest and by-type queries. The store intentionally exposes no bulk act-on-type method; see the class remarks on <see cref="EntityStore"/>.</summary>
public sealed class EntityStoreQueryTests
{
    [Fact]
    public void Nearest_ReturnsTheClosestEntityInsideRange()
    {
        var store = new EntityStore();
        var near = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        near.Position = new Vec3d(3, 0, 4); // distance 5
        var far = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Zombie));
        far.Position = new Vec3d(9, 0, 12); // distance 15

        Entity? result = store.Nearest(Vec3d.Zero, 20);

        Assert.Same(near, result);
    }

    [Fact]
    public void Nearest_ReturnsNull_WhenNothingIsInRange()
    {
        var store = new EntityStore();
        var far = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        far.Position = new Vec3d(30, 0, 0);

        Assert.Null(store.Nearest(Vec3d.Zero, 6));
    }

    /// <summary>Same squared-distance boundary as <see cref="EntityStore.Nearby"/>: a point exactly ON the range boundary is included (&lt;=), not excluded.</summary>
    [Fact]
    public void Nearest_UsesTheSameInclusiveSquaredBoundaryAsNearby()
    {
        var store = new EntityStore();
        var atBoundary = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        atBoundary.Position = new Vec3d(3, 0, 4); // distance exactly 5

        Entity? nearest = store.Nearest(Vec3d.Zero, 5);
        Entity[] nearby = [.. store.Nearby(Vec3d.Zero, 5)];

        Assert.Same(atBoundary, nearest);
        Assert.Single(nearby);
        Assert.Same(atBoundary, nearby[0]);
    }

    [Fact]
    public void Nearest_AppliesThePredicateBeforeDistance()
    {
        var store = new EntityStore();
        var closeButWrongType = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        closeButWrongType.Position = new Vec3d(1, 0, 0);
        var fartherButRightType = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Boat));
        fartherButRightType.Position = new Vec3d(5, 0, 0);

        Entity? result = store.Nearest(Vec3d.Zero, 10, e => e.Type.Id.Equals(EntityTestFixtures.Boat.Id));

        Assert.Same(fartherButRightType, result);
    }

    /// <summary>A tie is broken by lowest entity id, pinned regardless of add order.</summary>
    [Fact]
    public void Nearest_BreaksATieByTheLowestEntityId()
    {
        var store = new EntityStore();
        var higherId = store.Add(EntityTestFixtures.NewEntity(9, EntityTestFixtures.Zombie));
        higherId.Position = new Vec3d(5, 0, 0);
        var lowerId = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Zombie));
        lowerId.Position = new Vec3d(0, 0, 5); // identical distance, added second

        Entity? result = store.Nearest(Vec3d.Zero, 10);

        Assert.Same(lowerId, result);
    }

    [Theory]
    [InlineData("zombie")]
    [InlineData("minecraft:zombie")]
    public void OfType_String_MatchesTheMinecraftNamespace_BareOrFull(string needle)
    {
        var store = new EntityStore();
        var zombie = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Boat));

        Entity[] matches = [.. store.OfType(needle)];

        Assert.Single(matches);
        Assert.Same(zombie, matches[0]);
    }

    /// <summary>A bare "zombie" must not reach the "mod:zombie" entity, only the vanilla one.</summary>
    [Fact]
    public void OfType_String_BarePath_RejectsAForeignNamespace()
    {
        var store = new EntityStore();
        var vanillaZombie = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.ModZombie));

        Entity[] matches = [.. store.OfType("zombie")];

        Assert.Single(matches);
        Assert.Same(vanillaZombie, matches[0]);
    }

    /// <summary>The typed-<see cref="Identifier"/> overload has no bare/namespaced ambiguity: it is a direct equality check, so the modded and vanilla zombies (same path, different namespace) never collide.</summary>
    [Fact]
    public void OfType_Identifier_MatchesOnlyTheExactNamespacedId()
    {
        var store = new EntityStore();
        var vanillaZombie = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        var modZombie = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.ModZombie));

        Entity[] vanillaMatches = [.. store.OfType(EntityTestFixtures.Zombie.Id)];
        Entity[] modMatches = [.. store.OfType(EntityTestFixtures.ModZombie.Id)];

        Assert.Single(vanillaMatches);
        Assert.Same(vanillaZombie, vanillaMatches[0]);
        Assert.Single(modMatches);
        Assert.Same(modZombie, modMatches[0]);
    }

    [Fact]
    public void NearestFirst_OrdersAscendingByDistance()
    {
        var store = new EntityStore();
        var far = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie));
        far.Position = new Vec3d(9, 0, 0);
        var near = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Zombie));
        near.Position = new Vec3d(1, 0, 0);
        var middle = store.Add(EntityTestFixtures.NewEntity(3, EntityTestFixtures.Zombie));
        middle.Position = new Vec3d(5, 0, 0);

        IReadOnlyList<Entity> ordered = store.NearestFirst(Vec3d.Zero, 100);

        Assert.Equal([near, middle, far], ordered);
    }

    [Fact]
    public void NearestFirst_HonoursMaxResults()
    {
        var store = new EntityStore();
        for (int i = 0; i < 5; i++)
        {
            var e = store.Add(EntityTestFixtures.NewEntity(i + 1, EntityTestFixtures.Zombie));
            e.Position = new Vec3d(i, 0, 0);
        }

        IReadOnlyList<Entity> limited = store.NearestFirst(Vec3d.Zero, 100, maxResults: 2);

        Assert.Equal(2, limited.Count);
        Assert.Equal(0, limited[0].Position.X);
        Assert.Equal(1, limited[1].Position.X);
    }
}
