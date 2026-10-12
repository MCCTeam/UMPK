using System.Collections.Immutable;
using System.Security.Cryptography;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockObservedOakDropPlanTests
{
    private static readonly BedrockContainerAddress s_player = new(0, 0, null);
    private const string WitnessHex = "0701011d01000100010200010100010000050040000000b6b5ac94070a0000000000000000000005003f000000b6b5ac94070a00000000000000000000020100010100000000000000000000050001000000b6b5ac94070a00000000000000000000";

    [Fact]
    public void OriginalWitnessProjectionMatchesEveryOriginalByteAndRoundTrips()
    {
        Input input = Valid(5, 1921718966, 1);
        BedrockInventoryTransactionPacket packet = Create(input).Packet;
        byte[] expected = Convert.FromHexString(WitnessHex);
        byte[] encoded = BedrockInventoryTransactionCodecs.Encode(packet);
        Assert.Equal(98, expected.Length);
        Assert.Equal("3B88140BC7BBF8E6FDBC55E043611073A47D4FAA9F435575A1884339720C6F91", Convert.ToHexString(SHA256.HashData(expected)));
        Assert.Equal(expected, encoded);
        Assert.Equal(packet, BedrockInventoryTransactionCodecs.Decode(expected));
        Assert.Equal(expected, BedrockInventoryTransactionCodecs.Encode(BedrockInventoryTransactionCodecs.Decode(expected)));
        AssertProjection(packet, input.Before.Items[0]);
    }

    [Fact]
    public void SecondActualRegistryProjectsItsOwnMaterialAndBlockRatherThanWitnessNumbers()
    {
        Input input = Valid(11, 77, 88);
        BedrockObservedOakDropPlan plan = Create(input);
        AssertProjection(plan.Packet, input.Before.Items[0]);
        Assert.Equal(plan.Packet, BedrockInventoryTransactionCodecs.Decode(BedrockInventoryTransactionCodecs.Encode(plan.Packet)));
        Assert.Equal(11, plan.Packet.Actions[0].OldItem.NetworkId);
        Assert.Equal((uint)77, plan.Packet.Actions[1].NewItem.BlockRuntimeId);
        Assert.Equal(88, plan.Before.Items[0].StackNetworkId);
        Assert.Null(plan.TentativeRemainder.StackNetworkId);
    }

    [Fact]
    public void PureConstructionPreservesAuthorityAvailabilityReceiptAndStore()
    {
        Input input = Valid();
        BedrockObservedOakDropPlan plan = Create(input);
        BedrockInventorySnapshot after = input.Store.GetSnapshot(s_player)!.Value;
        Assert.True(after.IsSlotKnown(0));
        Assert.Equal(input.Before.Items[0], after.Items[0]);
        Assert.Same(input.Before.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.Same(input.Before.AuthorityReceipts[0], plan.AuthorityReceipt);
        Assert.Null(input.Store.GetTentativeSnapshot(s_player, 0));
        Assert.Equal(-4, plan.LegacyRequestId);
        Assert.Equal(input.Ownership.InventoryGeneration, plan.Generation);
        Assert.Equal(s_player, plan.Address);
        Assert.Equal(0, plan.Slot);
        Assert.Equal(new BedrockItemInstance(5, 63, 0, null, 77, new byte[10]), plan.TentativeRemainder);
    }

    [Fact]
    public void RetainedAndReturnedOpaqueArraysCannotAlterTheProjection()
    {
        Input input = Valid();
        BedrockObservedOakDropPlan plan = Create(input);
        input.Before.Items[0].ExtraData[0] = 1;
        input.Before.StorageItem.ExtraData[0] = 2;
        input.Entry.Item.ExtraData[0] = 3;
        plan.Before.Items[0].ExtraData[0] = 4;
        plan.Before.StorageItem.ExtraData[0] = 5;
        BedrockInventoryTransactionPacket first = plan.Packet;
        first.Actions[0].OldItem.ExtraData[0] = 6;
        first.Actions[0].NewItem.ExtraData[0] = 7;
        first.Actions[1].NewItem.ExtraData[0] = 8;
        plan.TentativeRemainder.ExtraData[0] = 9;
        AssertProjection(plan.Packet, new(5, 64, 0, 88, 77, new byte[10]));
        Assert.Equal(new byte[10], plan.Before.Items[0].ExtraData);
        Assert.Equal(new byte[] { 9 }, plan.Before.StorageItem.ExtraData);
        Assert.Equal(new byte[10], plan.TentativeRemainder.ExtraData);
        Assert.Same(input.Before.AuthorityReceipts[0], plan.Before.AuthorityReceipts[0]);
        Assert.NotSame(plan.Packet.Actions[0].OldItem.ExtraData, plan.Packet.Actions[0].OldItem.ExtraData);
    }

    [Fact]
    public void NeutralCorrectionIsRetainedExactlyAndIsPartOfCurrentReceiptQualification()
    {
        Input input = Valid();
        var row = new BedrockItemStackResponseSlot(0, 0, 64, 88, "", "", 0);
        input = input with { Before = input.Before with { ResponseCorrections = input.Before.ResponseCorrections.SetItem(0, row) } };
        BedrockObservedOakDropPlan plan = Create(input);
        Assert.Equal(row, plan.Before.ResponseCorrections[0]);
        Assert.NotSame(row, plan.Before.ResponseCorrections[0]);
        Assert.True(plan.IsSourceCurrent(input.Before, plan.Generation));
        Assert.False(plan.IsSourceCurrent(input.Before with { ResponseCorrections = input.Before.ResponseCorrections.SetItem(0, null) }, plan.Generation));
    }

    [Fact]
    public void SameValueActualSlotAndContentReplaceTheReceiptWhileUnrelatedSlotDoesNot()
    {
        Input input = Valid();
        BedrockObservedOakDropPlan plan = Create(input);
        Assert.True(plan.IsSourceCurrent(input.Before, plan.Generation));
        input.Store.ApplySlot(new(0, 1, s_player, null, Air()), s_player, input.Store.Generation);
        Assert.True(plan.IsSourceCurrent(input.Store.GetSnapshot(s_player)!.Value, input.Store.Generation));
        input.Store.ApplySlot(new(0, 0, s_player, null, input.Before.Items[0]), s_player, input.Store.Generation);
        BedrockInventorySnapshot sameValue = input.Store.GetSnapshot(s_player)!.Value;
        Assert.Equal(input.Before.Items[0], sameValue.Items[0]);
        Assert.NotSame(plan.AuthorityReceipt, sameValue.AuthorityReceipts[0]);
        Assert.True(sameValue.IsSlotKnown(0));
        Assert.False(plan.IsSourceCurrent(sameValue, input.Store.Generation));
        input.Store.ApplyContent(new(0, input.Before.Items, s_player, input.Before.StorageItem), input.Store.Generation);
        Assert.False(plan.IsSourceCurrent(input.Store.GetSnapshot(s_player)!.Value, input.Store.Generation));
    }

    [Fact]
    public void CarriedForwardReceiptDoesNotRequalifyOrdinaryPreflightAfterGenerationChanges()
    {
        Input input = Valid();
        BedrockObservedOakDropPlan plan = Create(input);
        Assert.True(plan.IsSourceCurrent(input.Before, input.Store.Generation));
        Assert.True(plan.IsCurrent(input.Ownership));
        input.Store.InvalidateTransient();
        BedrockInventorySnapshot carried = input.Store.GetSnapshot(s_player)!.Value;
        Assert.Same(plan.AuthorityReceipt, carried.AuthorityReceipts[0]);
        Assert.True(carried.IsSlotKnown(0));
        Assert.False(plan.IsSourceCurrent(carried, input.Store.Generation));
        Assert.False(plan.IsCurrent(input.Ownership with { InventoryGeneration = input.Store.Generation }));
    }

    [Fact]
    public void DefaultKnownAndCorrectionMasksStillRequireTheActualReceipt()
    {
        Input input = Valid();
        input = input with { Before = input.Before with { KnownSlots = default, ResponseCorrections = default } };
        BedrockObservedOakDropPlan plan = Create(input);
        Assert.True(plan.IsSourceCurrent(input.Before, plan.Generation));
        Assert.Same(input.Before.AuthorityReceipts[0], plan.AuthorityReceipt);
        AssertProjection(plan.Packet, input.Before.Items[0]);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("registry")]
    [InlineData("catalog")]
    [InlineData("screen-event")]
    [InlineData("screen-value")]
    public void CurrentOwnerRequiresActualRetainedObjectIdentities(string change)
    {
        Input input = Valid();
        BedrockObservedOakDropPlan plan = Create(input);
        Assert.True(plan.IsCurrent(input.Ownership));
        BedrockCreativeCursorOwnership current = change switch
        {
            "generation" => input.Ownership with { InventoryGeneration = 2 },
            "registry" => input.Ownership with { Registry = new(1, input.Ownership.Registry.Items, input.Ownership.Registry.Entities) },
            "catalog" => input.Ownership with { Catalog = new([], [input.Entry]) },
            "screen-event" => input.Ownership with { ScreenLifetime = new object() },
            _ => input.Ownership with { Screen = input.Ownership.Screen with { X = 1 } },
        };
        Assert.False(plan.IsCurrent(current));
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("unknown")]
    [InlineData("value")]
    [InlineData("correction")]
    [InlineData("shape")]
    [InlineData("address")]
    public void CurrentSourceRequiresExactReceiptAndStillKnownOriginalData(string change)
    {
        Input input = Valid();
        BedrockObservedOakDropPlan plan = Create(input);
        Assert.True(plan.IsSourceCurrent(input.Before, plan.Generation));
        BedrockInventorySnapshot current = change switch
        {
            "receipt" => input.Before with { AuthorityReceipts = input.Before.AuthorityReceipts.SetItem(0, new object()) },
            "unknown" => input.Before with { KnownSlots = input.Before.KnownSlots.SetItem(0, false) },
            "value" => input.Before with { Items = input.Before.Items.SetItem(0, new(5, 63, 0, 88, 77, new byte[10])) },
            "correction" => input.Before with { ResponseCorrections = input.Before.ResponseCorrections.SetItem(0, new(0, 0, 64, 88, "", "", 0)) },
            "shape" => input.Before with { AuthorityReceipts = [] },
            _ => input.Before with { Address = new(124, 0, null) },
        };
        Assert.False(plan.IsSourceCurrent(current, plan.Generation));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("dynamic")]
    [InlineData("empty")]
    [InlineData("unknown")]
    [InlineData("receipt-shape")]
    [InlineData("receipt-null")]
    [InlineData("known-shape")]
    [InlineData("correction-shape")]
    [InlineData("count")]
    [InlineData("stack-id")]
    [InlineData("material")]
    [InlineData("metadata")]
    [InlineData("block")]
    [InlineData("opaque")]
    [InlineData("detached-entry")]
    [InlineData("duplicate-entry")]
    [InlineData("definition")]
    [InlineData("components")]
    [InlineData("component-tag")]
    [InlineData("component-data")]
    [InlineData("version")]
    [InlineData("registry")]
    [InlineData("owner-generation")]
    [InlineData("screen")]
    [InlineData("name-correction")]
    [InlineData("durability-correction")]
    [InlineData("correction-id")]
    public void UnqualifiedInputRefusesBeforeAnyPacketIsProduced(string change)
    {
        Input input = Valid();
        BedrockInventorySnapshot before = input.Before;
        var nonemptyComponents = new NbtCompound();
        nonemptyComponents.Put("unsupported", new NbtInt(1));
        input = change switch
        {
            "address" => input with { Before = before with { Address = new(124, 0, null) } },
            "dynamic" => input with { Before = before with { Address = new(0, 0, 7) } },
            "empty" => input with { Before = before with { Items = [] } },
            "unknown" => input with { Before = before with { KnownSlots = before.KnownSlots.SetItem(0, false) } },
            "receipt-shape" => input with { Before = before with { AuthorityReceipts = [] } },
            "receipt-null" => input with { Before = before with { AuthorityReceipts = before.AuthorityReceipts.SetItem(0, null!) } },
            "known-shape" => input with { Before = before with { KnownSlots = [] } },
            "correction-shape" => input with { Before = before with { ResponseCorrections = [] } },
            "count" => input with { Before = before with { Items = before.Items.SetItem(0, new(5, 63, 0, 88, 77, new byte[10])) } },
            "stack-id" => input with { Before = before with { Items = before.Items.SetItem(0, new(5, 64, 0, null, 77, new byte[10])) } },
            "material" => input with { Before = before with { Items = before.Items.SetItem(0, new(6, 64, 0, 88, 77, new byte[10])) } },
            "metadata" => input with { Before = before with { Items = before.Items.SetItem(0, new(5, 64, 1, 88, 77, new byte[10])) } },
            "block" => input with { Before = before with { Items = before.Items.SetItem(0, new(5, 64, 0, 88, 78, new byte[10])) } },
            "opaque" => input with { Before = before with { Items = before.Items.SetItem(0, new(5, 64, 0, 88, 77, [])) } },
            "detached-entry" => input with { Entry = new(input.Entry.EntryId, input.Entry.Item, 0) },
            "duplicate-entry" => input with { Ownership = input.Ownership with { Catalog = new([], [input.Entry, input.Entry]) } },
            "definition" => input with { Item = input.Item with { Name = "minecraft:stone" } },
            "components" => input with { Item = input.Item with { ComponentBased = true } },
            "component-tag" => input with { Item = input.Item with { Components = new NbtInt(1) } },
            "component-data" => input with { Item = input.Item with { Components = nonemptyComponents } },
            "version" => input with { Item = input.Item with { Version = 3 } },
            "registry" => input with { Ownership = input.Ownership with { Registry = new(1, ImmutableDictionary<short, string>.Empty, input.Ownership.Registry.Entities) } },
            "owner-generation" => input with { Ownership = input.Ownership with { InventoryGeneration = 0 } },
            "screen" => input with { Ownership = input.Ownership with { Screen = input.Ownership.Screen with { WindowType = 0 } } },
            "name-correction" => input with { Before = before with { ResponseCorrections = before.ResponseCorrections.SetItem(0, new(0, 0, 64, 88, "named", "", 0)) } },
            "durability-correction" => input with { Before = before with { ResponseCorrections = before.ResponseCorrections.SetItem(0, new(0, 0, 64, 88, "", "", 1)) } },
            _ => input with { Before = before with { ResponseCorrections = before.ResponseCorrections.SetItem(0, new(0, 0, 64, 89, "", "", 0)) } },
        };
        Assert.Throws<InvalidOperationException>(() => Create(input));
    }

    private static void AssertProjection(BedrockInventoryTransactionPacket packet, BedrockItemInstance source)
    {
        Assert.Equal(-4, packet.Legacy.RequestId);
        BedrockTransactionLegacyEntry touch = Assert.Single(packet.Legacy.Entries);
        Assert.Equal((byte)29, touch.ContainerId);
        Assert.Equal(new byte[] { 0 }, touch.ChangedSlots.ToArray());
        Assert.Equal(BedrockInventoryTransactionType.Normal, packet.TransactionType);
        Assert.Null(packet.Data);
        Assert.Equal(2, packet.Actions.Length);
        BedrockTransactionAction player = packet.Actions[0];
        BedrockTransactionAction world = packet.Actions[1];
        Assert.Equal((uint)0, player.SourceType);
        Assert.True(player.ContainerPresence);
        Assert.Equal((sbyte)0, player.WindowId);
        Assert.True(player.FlagPresence);
        Assert.Null(player.Flags);
        Assert.Equal((uint)0, player.Slot);
        Assert.Equal((uint)2, world.SourceType);
        Assert.True(world.ContainerPresence);
        Assert.Null(world.WindowId);
        Assert.True(world.FlagPresence);
        Assert.Equal((uint)0, world.Flags);
        Assert.Equal((uint)0, world.Slot);
        Assert.Equal(new BedrockItemInstance(source.NetworkId, 64, source.MetadataValue, null, source.BlockRuntimeId, source.ExtraData), player.OldItem);
        Assert.Equal(new BedrockItemInstance(source.NetworkId, 63, source.MetadataValue, null, source.BlockRuntimeId, source.ExtraData), player.NewItem);
        Assert.Equal(Air(), world.OldItem);
        Assert.Empty(world.OldItem.ExtraData);
        Assert.Equal(new BedrockItemInstance(source.NetworkId, 1, source.MetadataValue, null, source.BlockRuntimeId, source.ExtraData), world.NewItem);
        Assert.Null(player.OldItem.StackNetworkId);
        Assert.Null(player.NewItem.StackNetworkId);
        Assert.Null(world.OldItem.StackNetworkId);
        Assert.Null(world.NewItem.StackNetworkId);
    }

    private sealed record Input(BedrockInventoryStore Store, BedrockInventorySnapshot Before, BedrockCreativeEntry Entry, BedrockItemState Item, BedrockCreativeCursorOwnership Ownership);

    private static Input Valid(short material = 5, int block = 77, int stackId = 88)
    {
        var store = new BedrockInventoryStore();
        BedrockItemInstance[] items = Enumerable.Range(0, 36).Select(_ => Air()).ToArray();
        items[0] = new(material, 64, 0, stackId, checked((uint)block), new byte[10]);
        store.ApplyContent(new(0, items, s_player, new BedrockItemInstance(0, 0, 0, null, 0, [9])), store.Generation);
        var item = new BedrockItemState("minecraft:oak_planks", material, false, 2, new NbtCompound());
        var entry = new BedrockCreativeEntry(17, new(material, 1, 0, block, new byte[10]), 0);
        var catalog = new BedrockCreativeContentPacket([], [entry]);
        var registry = new BedrockRegistrySnapshot(1, ImmutableDictionary<short, string>.Empty.Add(material, item.Name), ImmutableDictionary<string, int>.Empty);
        var owner = new BedrockCreativeCursorOwnership(store.Generation, registry, catalog, new(2, -1, 0, 0, 0, -1), new object());
        return new(store, store.GetSnapshot(s_player)!.Value, entry, item, owner);
    }

    private static BedrockObservedOakDropPlan Create(Input input) => BedrockObservedOakDropPlan.Create(input.Before, input.Entry, input.Item, input.Ownership);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
}
