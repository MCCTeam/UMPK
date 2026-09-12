using Umpk.Game.Entities;
using Umpk.Geometry;
using Xunit;

namespace Umpk.Game.Tests.Entities;

public sealed class EntityStoreTests
{
    [Fact]
    public void Add_And_Lookup_By_Id_And_Uuid()
    {
        var store = new EntityStore();
        var e = EntityTestFixtures.NewEntity(7, EntityTestFixtures.Zombie);

        store.Add(e);

        Assert.Same(e, store.Get(7));
        Assert.True(store.TryGetByUuid(e.Uuid, out Entity? byUuid));
        Assert.Same(e, byUuid);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Add_Duplicate_Id_Returns_Existing_And_Does_Not_Replace()
    {
        var store = new EntityStore();
        var first = EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie);
        var second = EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat);

        store.Add(first);
        Entity result = store.Add(second);

        Assert.Same(first, result);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void SetPassengers_Links_Both_Directions()
    {
        var store = new EntityStore();
        var boat = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));
        var rider = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Player));

        store.SetPassengers(boat, [rider]);

        Assert.Single(boat.Passengers);
        Assert.Same(rider, boat.Passengers[0]);
        Assert.Same(boat, rider.Vehicle);
    }

    [Fact]
    public void SetPassengers_Detaches_Former_Passengers()
    {
        var store = new EntityStore();
        var boat = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));
        var a = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Player));
        var b = store.Add(EntityTestFixtures.NewEntity(3, EntityTestFixtures.Zombie));

        store.SetPassengers(boat, [a]);
        store.SetPassengers(boat, [b]);

        Assert.Null(a.Vehicle);
        Assert.Same(boat, b.Vehicle);
        Assert.Single(boat.Passengers);
        Assert.Same(b, boat.Passengers[0]);
    }

    [Fact]
    public void SetVehicle_Reparents_Between_Vehicles()
    {
        var store = new EntityStore();
        var boatA = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));
        var boatB = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Boat));
        var rider = store.Add(EntityTestFixtures.NewEntity(3, EntityTestFixtures.Player));

        store.SetVehicle(rider, boatA);
        store.SetVehicle(rider, boatB);

        Assert.Empty(boatA.Passengers);
        Assert.Same(boatB, rider.Vehicle);
        Assert.Single(boatB.Passengers);
        Assert.Same(rider, boatB.Passengers[0]);
    }

    [Fact]
    public void SetVehicle_Null_Dismounts()
    {
        var store = new EntityStore();
        var boat = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));
        var rider = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Player));

        store.SetVehicle(rider, boat);
        store.SetVehicle(rider, null);

        Assert.Null(rider.Vehicle);
        Assert.Empty(boat.Passengers);
    }

    [Fact]
    public void SetVehicle_Self_Mount_Rejected()
    {
        var store = new EntityStore();
        var e = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));

        Assert.Throws<InvalidOperationException>(() => store.SetVehicle(e, e));
    }

    [Fact]
    public void SetVehicle_Direct_Cycle_Rejected()
    {
        var store = new EntityStore();
        var a = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));
        var b = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Boat));

        store.SetVehicle(b, a); // b rides a

        // a riding b would close the cycle a->b->a
        Assert.Throws<InvalidOperationException>(() => store.SetVehicle(a, b));
    }

    [Fact]
    public void SetPassengers_Transitive_Cycle_Rejected()
    {
        var store = new EntityStore();
        var a = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));
        var b = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Boat));
        var c = store.Add(EntityTestFixtures.NewEntity(3, EntityTestFixtures.Boat));

        store.SetPassengers(a, [b]);
        store.SetPassengers(b, [c]);

        // Making a a passenger of c would form c<-... cycle: a is transitive passenger of a via c
        Assert.Throws<InvalidOperationException>(() => store.SetPassengers(c, [a]));
    }

    [Fact]
    public void Remove_Detaches_From_Vehicle_And_Dismounts_Passengers()
    {
        var store = new EntityStore();
        var boat = store.Add(EntityTestFixtures.NewEntity(1, EntityTestFixtures.Boat));
        var rider = store.Add(EntityTestFixtures.NewEntity(2, EntityTestFixtures.Player));
        var sub = store.Add(EntityTestFixtures.NewEntity(3, EntityTestFixtures.Zombie));

        store.SetVehicle(rider, boat);
        store.SetVehicle(sub, rider);

        // Remove the middle rider: it should detach from boat and dismount sub.
        Entity? removed = store.Remove(rider.Id);

        Assert.Same(rider, removed);
        Assert.Empty(boat.Passengers);
        Assert.Null(sub.Vehicle);
        Assert.Null(store.Get(rider.Id));
    }

    [Fact]
    public void Nearby_Filters_By_Range()
    {
        var store = new EntityStore();
        var near = EntityTestFixtures.NewEntity(1, EntityTestFixtures.Zombie);
        near.Position = new Vec3d(3, 0, 4); // distance 5
        var far = EntityTestFixtures.NewEntity(2, EntityTestFixtures.Zombie);
        far.Position = new Vec3d(30, 0, 0);
        store.Add(near);
        store.Add(far);

        Entity[] result = [.. store.Nearby(Vec3d.Zero, 6)];

        Assert.Single(result);
        Assert.Same(near, result[0]);
    }
}
