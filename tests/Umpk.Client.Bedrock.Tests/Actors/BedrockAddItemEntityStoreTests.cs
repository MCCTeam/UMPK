using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Actors;

public sealed class BedrockAddItemEntityStoreTests
{
    private static BedrockAddItemEntityPacket ItemPacket(
        long unique = 10L,
        ulong runtime = 3UL,
        bool fishing = false) =>
        new(
            unique,
            runtime,
            new BedrockItemInstance(306, 5, 0U, 2, 0U, []),
            new BedrockVector3(1f, 2f, 3f),
            new BedrockVector3(0f, 1f, 0f),
            [new BedrockMetadataEntry(0U, 7U, 0, BedrockMetadataValue.FromLong(0L))],
            fishing);

    [Fact]
    public void AddItemEntityRetainsTypedItemIdentityAsExplicitKind()
    {
        var store = new BedrockActorStore();

        Assert.True(store.ApplyAddItemEntity(ItemPacket(), store.WorldGeneration));

        BedrockActorSnapshot snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(BedrockActorKind.Item, snapshot.Kind);
        Assert.NotNull(snapshot.DroppedItem);
        Assert.Equal(306, snapshot.DroppedItem.NetworkId);
        Assert.Equal(5, snapshot.DroppedItem.Count);
        Assert.Equal(2, snapshot.DroppedItem.StackNetworkId);
        Assert.False(snapshot.IsFromFishing);
        Assert.Null(snapshot.HeldItem);
        Assert.Null(snapshot.Armor);
        Assert.Equal(new BedrockVector3(1f, 2f, 3f), snapshot.Position);
        Assert.Equal(new BedrockVector3(0f, 1f, 0f), snapshot.Velocity);
        Assert.True(snapshot.MetadataObserved);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Added);
    }

    [Fact]
    public void AddItemEntityReplacementByRuntimeInvalidatesPreviousIdentity()
    {
        var store = new BedrockActorStore();
        store.ApplyAddItemEntity(ItemPacket(unique: 10L, runtime: 3UL), store.WorldGeneration);

        Assert.True(store.ApplyAddItemEntity(ItemPacket(unique: 11L, runtime: 3UL, fishing: true), store.WorldGeneration));

        BedrockActorSnapshot snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(11L, snapshot.Handle.UniqueEntityId);
        Assert.Equal(3UL, snapshot.Handle.RuntimeEntityId);
        Assert.True(snapshot.IsFromFishing);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Removed);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Added);
    }

    [Fact]
    public void RemoveEntityClearsRetainedItemIdentity()
    {
        var store = new BedrockActorStore();
        store.ApplyAddItemEntity(ItemPacket(), store.WorldGeneration);

        Assert.True(store.ApplyRemoveEntity(10L, store.WorldGeneration));

        Assert.Empty(store.Snapshots);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Removed);
    }

    [Fact]
    public void WorldInvalidationClearsItemEntitiesAndKeepsLocalPlayerShell()
    {
        var store = new BedrockActorStore();
        store.SetLocalPlayer(456L, 789UL);
        store.ApplyAddItemEntity(ItemPacket(), store.WorldGeneration);

        store.InvalidateWorld();

        BedrockActorSnapshot local = Assert.Single(store.Snapshots);
        Assert.Equal(BedrockActorKind.LocalPlayer, local.Kind);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Removed);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.WorldInvalidated);
    }

    [Fact]
    public void AddItemEntityRecordsPacket15Witness()
    {
        var store = new BedrockActorStore();
        var packet = ItemPacket();
        byte[] payload = BedrockActorCodecs.Encode(packet);
        store.ApplyAddItemEntity(packet, store.WorldGeneration);
        store.RecordWireWitness(15, payload);

        Assert.Contains(store.WireWitnesses, static witness => witness.PacketId == 15 && witness.TotalLength > 0);
    }

    [Fact]
    public void AddItemEntityWithLocalRuntimePreservesLocalShell()
    {
        var store = new BedrockActorStore();
        store.SetLocalPlayer(456L, 789UL);

        Assert.True(store.ApplyAddItemEntity(ItemPacket(unique: 10L, runtime: 789UL), store.WorldGeneration));

        BedrockActorSnapshot local = Assert.Single(store.Snapshots);
        Assert.Equal(BedrockActorKind.LocalPlayer, local.Kind);
        Assert.Equal(456L, local.Handle.UniqueEntityId);
        Assert.Equal(789UL, local.Handle.RuntimeEntityId);
        Assert.Equal(1, store.ActorCount);

        Assert.True(store.ApplyRemoveEntity(10L, store.WorldGeneration));
        Assert.Equal(1, store.StaleRemovals);
        Assert.Single(store.Snapshots);
    }

    [Fact]
    public void AddItemEntityWithSameIdentityAsEntityReplacesKind()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(1f, 2f, 3f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []), store.WorldGeneration);

        Assert.True(store.ApplyAddItemEntity(ItemPacket(unique: 10L, runtime: 3UL), store.WorldGeneration));

        BedrockActorSnapshot snapshot = Assert.Single(store.Snapshots);
        Assert.Equal(BedrockActorKind.Item, snapshot.Kind);
        Assert.Equal(10L, snapshot.Handle.UniqueEntityId);
        Assert.Equal(3UL, snapshot.Handle.RuntimeEntityId);
        Assert.NotNull(snapshot.DroppedItem);
        Assert.Null(snapshot.EntityType);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Removed);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Added);
    }
}
