using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Actors;

public sealed class BedrockActorStoreTests
{
    [Fact]
    public void LocalPlayerTrackedSeparatelyFromOrdinaryActors()
    {
        var store = new BedrockActorStore();

        store.SetLocalPlayer(456L, 789UL);
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(1f, 2f, 3f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        Assert.Equal(2, store.ActorCount);
        BedrockActorSnapshot local = Assert.Single(store.Snapshots, static snapshot => snapshot.Kind == BedrockActorKind.LocalPlayer);
        Assert.Equal(456L, local.Handle.UniqueEntityId);
        Assert.Equal(789UL, local.Handle.RuntimeEntityId);
        Assert.True(local.Handle.IsLocalPlayer);
        BedrockActorSnapshot entity = Assert.Single(store.Snapshots, static snapshot => snapshot.Kind == BedrockActorKind.Entity);
        Assert.Equal("minecraft:pig", entity.EntityType);
    }

    [Fact]
    public void RemotePlayerAddUpdateRemoveRoundTrip()
    {
        var store = new BedrockActorStore();
        var uuid = Guid.Parse("12345678-1234-1234-1234-1234567890ab");

        store.ApplyAddPlayer(
            new BedrockAddPlayerPacket(
                uuid,
                "SecondBot",
                2UL,
                string.Empty,
                new BedrockVector3(8f, 65f, 8f),
                new BedrockVector3(0f, 0f, 0f),
                0f,
                0f,
                0f,
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                0,
                [],
                BedrockEntityProperties.Empty,
                100L,
                0,
                0,
                [],
                [],
                string.Empty,
                null),
            store.WorldGeneration);
        store.ApplyMovePlayer(2UL, new BedrockVector3(9f, 65f, 8f), 0UL, 12UL, store.WorldGeneration);

        BedrockActorSnapshot player = Assert.Single(store.Snapshots);
        Assert.Equal(BedrockActorKind.Player, player.Kind);
        Assert.Equal("SecondBot", player.DisplayName);
        Assert.Equal(uuid, player.Uuid);
        Assert.Equal(100L, player.Handle.UniqueEntityId);
        Assert.Equal(new BedrockVector3(9f, 65f, 8f), player.Position);
        Assert.Equal(12UL, player.LastServerTick);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Added);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Updated);

        store.ApplyRemoveEntity(100L, store.WorldGeneration);

        Assert.Empty(store.Snapshots);
        Assert.Equal(0, store.ActorCount);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Removed);
    }

    [Fact]
    public void IdentityNamespacesPreserveValuesBeyondInt32()
    {
        var store = new BedrockActorStore();
        const ulong bigRuntime = 0x1_0000_0005UL;
        const long bigUnique = 5_000_000_000L;

        store.ApplyAddEntity(
            new BedrockAddEntityPacket(bigUnique, bigRuntime, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        BedrockActorSnapshot entity = Assert.Single(store.Snapshots);
        Assert.Equal(bigRuntime, entity.Handle.RuntimeEntityId);
        Assert.Equal(bigUnique, entity.Handle.UniqueEntityId);

        store.ApplyRemoveEntity(bigUnique, store.WorldGeneration);

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void RemoveBeforeAddCountsStaleWithoutFailing()
    {
        var store = new BedrockActorStore();

        Assert.True(store.ApplyRemoveEntity(999L, store.WorldGeneration));

        Assert.Equal(1, store.StaleRemovals);
        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void UpdatesForUnknownTargetsAreCountedWithoutInventingActors()
    {
        var store = new BedrockActorStore();

        store.ApplySetEntityMotion(77UL, new BedrockVector3(0f, 0f, 0f), 3UL, store.WorldGeneration);
        store.ApplyMovePlayer(77UL, new BedrockVector3(0f, 0f, 0f), 0UL, 3UL, store.WorldGeneration);
        store.ApplySetEntityData(new BedrockSetEntityDataPacket(77UL, [], BedrockEntityProperties.Empty, 3UL), store.WorldGeneration);
        store.ApplySetEntityLink(new BedrockSetEntityLinkPacket(new BedrockEntityLink(78L, 77L, BedrockEntityLinkType.Rider, false, false, 0f)), store.WorldGeneration);
        store.ApplyPlayerLocation(78L, new BedrockVector3(0f, 0f, 0f), false, store.WorldGeneration);

        Assert.Equal(5, store.UnknownTargetUpdates);
        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void RuntimeReplacementRetiresOldUniqueIdentity()
    {
        var store = new BedrockActorStore();

        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(11L, 3UL, "minecraft:cow", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        BedrockActorSnapshot entity = Assert.Single(store.Snapshots);
        Assert.Equal(11L, entity.Handle.UniqueEntityId);
        Assert.Equal("minecraft:cow", entity.EntityType);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Removed);
    }

    [Fact]
    public void DuplicateUniqueIdOnAnotherRuntimeRetiresTheOldEntry()
    {
        var store = new BedrockActorStore();

        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 4UL, "minecraft:cow", new BedrockVector3(1f, 2f, 3f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        BedrockActorSnapshot entity = Assert.Single(store.Snapshots);
        Assert.Equal(4UL, entity.Handle.RuntimeEntityId);
        Assert.Equal(10L, entity.Handle.UniqueEntityId);
        Assert.Equal("minecraft:cow", entity.EntityType);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.Removed);

        store.ApplyRemoveEntity(10L, store.WorldGeneration);

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void StaleGenerationWorkIsRejected()
    {
        var store = new BedrockActorStore();

        Assert.False(store.ApplyRemoveEntity(10L, worldGeneration: 999));
        Assert.False(store.ApplyMovePlayer(3UL, new BedrockVector3(0f, 0f, 0f), 0UL, 1UL, worldGeneration: 999));
        Assert.Equal(0, store.StaleRemovals);
        Assert.Equal(0, store.UnknownTargetUpdates);
    }

    [Fact]
    public void WorldInvalidationClearsRemotesAndKeepsLocalPlayer()
    {
        var store = new BedrockActorStore();
        store.SetLocalPlayer(456L, 789UL);
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);
        int oldGeneration = store.WorldGeneration;

        store.InvalidateWorld();

        Assert.NotEqual(oldGeneration, store.WorldGeneration);
        BedrockActorSnapshot local = Assert.Single(store.Snapshots);
        Assert.Equal(BedrockActorKind.LocalPlayer, local.Kind);
        Assert.Equal(store.WorldGeneration, local.Handle.WorldGeneration);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.WorldInvalidated);
        Assert.False(store.ApplyMovePlayer(3UL, new BedrockVector3(0f, 0f, 0f), 0UL, 1UL, oldGeneration));
    }

    [Fact]
    public void PlayerLifeInvalidationKeepsActorsAndRecordsScope()
    {
        var store = new BedrockActorStore();
        store.SetLocalPlayer(456L, 789UL);

        store.InvalidatePlayerLife();

        Assert.Single(store.Snapshots);
        Assert.Contains(store.Events, static evt => evt.Kind == BedrockActorEventKind.PlayerLifeInvalidated);
    }

    [Fact]
    public void RiderLinksClearWhenRiddenEntityRemoved()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(20L, 5UL, "minecraft:horse", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(21L, 6UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);
        store.ApplyMovePlayer(6UL, new BedrockVector3(1f, 2f, 3f), 5UL, 9UL, store.WorldGeneration);

        Assert.Equal(5UL, store.Snapshots.Single(static snapshot => snapshot.Handle.RuntimeEntityId == 6UL).RidingRuntimeEntityId);

        store.ApplyRemoveEntity(20L, store.WorldGeneration);

        BedrockActorSnapshot rider = Assert.Single(store.Snapshots);
        Assert.Null(rider.RidingRuntimeEntityId);
    }

    [Fact]
    public void BoundedStoreRejectsActorsBeyondCapacity()
    {
        var store = new BedrockActorStore();
        for (int index = 0; index < BedrockActorStore.MaxActors; index++)
        {
            store.ApplyAddEntity(
                new BedrockAddEntityPacket(1000L + index, (ulong)(2000 + index), "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
                store.WorldGeneration);
        }

        Assert.Equal(BedrockActorStore.MaxActors, store.ActorCount);
        Assert.Throws<InvalidOperationException>(() => store.ApplyAddEntity(
            new BedrockAddEntityPacket(9_999_999L, 9_999_999UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration));
    }

    [Fact]
    public void PlayerLocationHideAndCoordinatesUpdateKnownActors()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(30L, 8UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        store.ApplyPlayerLocation(30L, new BedrockVector3(4f, 5f, 6f), false, store.WorldGeneration);

        BedrockActorSnapshot shown = Assert.Single(store.Snapshots);
        Assert.Equal(new BedrockVector3(4f, 5f, 6f), shown.Position);
        Assert.False(shown.LocationHidden);

        store.ApplyPlayerLocation(30L, null, true, store.WorldGeneration);

        BedrockActorSnapshot hidden = Assert.Single(store.Snapshots);
        Assert.True(hidden.LocationHidden);
        Assert.Equal(new BedrockVector3(4f, 5f, 6f), hidden.Position);
    }

    [Fact]
    public void MobEffectObservationsUpsertAndRemoveByEffectId()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        store.ApplyMobEffect(
            new BedrockMobEffectPacket(3UL, BedrockMobEffectEvent.Add, 10, 1, true, 600, 42UL, false),
            store.WorldGeneration);

        BedrockActorSnapshot observed = Assert.Single(store.Snapshots);
        BedrockActorEffect effect = Assert.Single(observed.Effects);
        Assert.Equal(10, effect.EffectId);
        Assert.Equal(1, effect.Amplifier);
        Assert.Equal(600, effect.Duration);
        Assert.True(effect.Particles);
        Assert.False(effect.Ambient);
        Assert.Equal(42UL, observed.LastServerTick);

        store.ApplyMobEffect(
            new BedrockMobEffectPacket(3UL, BedrockMobEffectEvent.Modify, 10, 2, false, 300, 43UL, true),
            store.WorldGeneration);

        BedrockActorEffect modified = Assert.Single(Assert.Single(store.Snapshots).Effects);
        Assert.Equal(2, modified.Amplifier);
        Assert.Equal(300, modified.Duration);

        store.ApplyMobEffect(
            new BedrockMobEffectPacket(3UL, BedrockMobEffectEvent.Remove, 10, 0, false, 0, 44UL, false),
            store.WorldGeneration);

        Assert.Empty(Assert.Single(store.Snapshots).Effects);

        store.ApplyMobEffect(
            new BedrockMobEffectPacket(999UL, BedrockMobEffectEvent.Add, 10, 0, false, 100, 45UL, false),
            store.WorldGeneration);

        Assert.Equal(1, store.UnknownTargetUpdates);
    }

    [Fact]
    public void MobEffectObservationsClearWithOwningActor()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);
        store.ApplyMobEffect(
            new BedrockMobEffectPacket(3UL, BedrockMobEffectEvent.Add, 10, 1, true, 600, 42UL, false),
            store.WorldGeneration);

        store.ApplyRemoveEntity(10L, store.WorldGeneration);

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void UpdateAttributesUpsertByNameAndClearWithOwningActor()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        store.ApplyUpdateAttributes(
            new BedrockUpdateAttributesPacket(3UL, [new BedrockPlayerAttribute(0f, 20f, 20f, 0f, 20f, 20f, "minecraft:health", [])], 7UL),
            store.WorldGeneration);

        BedrockActorSnapshot observed = Assert.Single(store.Snapshots);
        BedrockPlayerAttribute health = Assert.Single(observed.Attributes);
        Assert.Equal("minecraft:health", health.Name);
        Assert.Equal(20f, health.Current);
        Assert.Equal(7UL, observed.LastServerTick);

        store.ApplyUpdateAttributes(
            new BedrockUpdateAttributesPacket(3UL, [new BedrockPlayerAttribute(0f, 20f, 14f, 0f, 20f, 20f, "minecraft:health", [])], 8UL),
            store.WorldGeneration);

        Assert.Equal(14f, Assert.Single(Assert.Single(store.Snapshots).Attributes).Current);

        store.ApplyUpdateAttributes(
            new BedrockUpdateAttributesPacket(999UL, [new BedrockPlayerAttribute(0f, 20f, 20f, 0f, 20f, 20f, "minecraft:health", [])], 9UL),
            store.WorldGeneration);

        Assert.Equal(1, store.UnknownTargetUpdates);

        store.ApplyRemoveEntity(10L, store.WorldGeneration);

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void EquipmentObservationsReplaceAndClearWithOwningActor()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(10L, 3UL, "minecraft:pig", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);

        store.ApplyMobEquipment(
            new BedrockMobEquipmentPacket(3UL, new BedrockItemInstance(7, 1, 0U, null, 0U, []), 1, 2, 0),
            store.WorldGeneration);
        store.ApplyMobArmorEquipment(
            new BedrockMobArmorEquipmentPacket(
                3UL,
                new BedrockItemInstance(8, 1, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, [])),
            store.WorldGeneration);

        BedrockActorSnapshot observed = Assert.Single(store.Snapshots);
        Assert.NotNull(observed.HeldItem);
        Assert.Equal(7, observed.HeldItem.Item.NetworkId);
        Assert.Equal(1, observed.HeldItem.InventorySlot);
        Assert.Equal(2, observed.HeldItem.SelectedSlot);
        Assert.Equal(0, observed.HeldItem.WindowId);
        Assert.NotNull(observed.Armor);
        Assert.Equal(8, observed.Armor.Helmet.NetworkId);

        store.ApplyMobEquipment(
            new BedrockMobEquipmentPacket(999UL, new BedrockItemInstance(7, 1, 0U, null, 0U, []), 0, 0, 0),
            store.WorldGeneration);
        store.ApplyMobArmorEquipment(
            new BedrockMobArmorEquipmentPacket(
                999UL,
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, []),
                new BedrockItemInstance(0, 0, 0U, null, 0U, [])),
            store.WorldGeneration);

        Assert.Equal(2, store.UnknownTargetUpdates);

        store.ApplyRemoveEntity(10L, store.WorldGeneration);

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public void AddSectionsApplyToSnapshotsAndLinksResolve()
    {
        var store = new BedrockActorStore();
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(20L, 5UL, "minecraft:horse", new BedrockVector3(0f, 0f, 0f), new BedrockVector3(0f, 0f, 0f), 0f, 0f, 0f, 0f, [], [], BedrockEntityProperties.Empty, []),
            store.WorldGeneration);
        store.ApplyAddEntity(
            new BedrockAddEntityPacket(
                21L,
                6UL,
                "minecraft:pig",
                new BedrockVector3(0f, 0f, 0f),
                new BedrockVector3(0f, 0f, 0f),
                0f,
                0f,
                0f,
                0f,
                [new BedrockEntityAttributeValue("minecraft:health", 0f, 20f, 20f)],
                [new BedrockMetadataEntry(4U, 4U, 4, BedrockMetadataValue.FromString("Hi"))],
                new BedrockEntityProperties([new BedrockIndexedInt(1U, 5)], []),
                [new BedrockEntityLink(20L, 21L, BedrockEntityLinkType.Rider, false, true, 0f)]),
            store.WorldGeneration);

        BedrockActorSnapshot rider = store.Snapshots.Single(static snapshot => snapshot.Handle.RuntimeEntityId == 6UL);
        Assert.Equal(5UL, rider.RidingRuntimeEntityId);
        Assert.True(rider.MetadataObserved);
        Assert.Equal("Hi", Assert.Single(rider.Metadata).Value.Text);
        Assert.Equal(20f, Assert.Single(rider.BaseAttributes).Value);
        Assert.Equal(5, Assert.Single(rider.Properties.Ints).Value);

        store.ApplySetEntityData(
            new BedrockSetEntityDataPacket(
                6UL,
                [new BedrockMetadataEntry(4U, 4U, 4, BedrockMetadataValue.FromString("Bye"))],
                new BedrockEntityProperties([], [new BedrockIndexedFloat(2U, 1.5f)]),
                11UL),
            store.WorldGeneration);

        BedrockActorSnapshot replaced = store.Snapshots.Single(static snapshot => snapshot.Handle.RuntimeEntityId == 6UL);
        Assert.Equal("Bye", Assert.Single(replaced.Metadata).Value.Text);
        Assert.Equal(1.5f, Assert.Single(replaced.Properties.Floats).Value);
        Assert.Equal(11UL, replaced.LastServerTick);

        store.ApplySetEntityLink(
            new BedrockSetEntityLinkPacket(new BedrockEntityLink(20L, 21L, BedrockEntityLinkType.Remove, true, false, 0f)),
            store.WorldGeneration);

        Assert.Null(store.Snapshots.Single(static snapshot => snapshot.Handle.RuntimeEntityId == 6UL).RidingRuntimeEntityId);

        store.ApplySetEntityLink(
            new BedrockSetEntityLinkPacket(new BedrockEntityLink(998L, 999L, BedrockEntityLinkType.Rider, false, false, 0f)),
            store.WorldGeneration);

        Assert.Equal(1, store.UnknownTargetUpdates);
    }

    [Fact]
    public void WorldInvalidationClearsLocalObservations()
    {
        var store = new BedrockActorStore();
        store.SetLocalPlayer(456L, 789UL);
        store.ApplyMobEffect(
            new BedrockMobEffectPacket(789UL, BedrockMobEffectEvent.Add, 10, 1, true, 600, 42UL, false),
            store.WorldGeneration);
        store.ApplySetEntityData(
            new BedrockSetEntityDataPacket(789UL, [new BedrockMetadataEntry(4U, 4U, 4, BedrockMetadataValue.FromString("Hi"))], BedrockEntityProperties.Empty, 43UL),
            store.WorldGeneration);

        store.InvalidateWorld();

        BedrockActorSnapshot local = Assert.Single(store.Snapshots);
        Assert.Equal(BedrockActorKind.LocalPlayer, local.Kind);
        Assert.Empty(local.Effects);
        Assert.Empty(local.Metadata);
        Assert.False(local.MetadataObserved);
        Assert.Null(local.HeldItem);
    }
}
