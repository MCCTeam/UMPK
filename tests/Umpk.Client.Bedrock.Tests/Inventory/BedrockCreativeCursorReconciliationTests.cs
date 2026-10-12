using System.Collections.Immutable;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockCreativeCursorReconciliationTests
{
    private sealed record Input(BedrockInventorySnapshot Before, BedrockCreativeEntry Entry, BedrockItemState RegistryItem, BedrockItemStackRequestPacket Packet, BedrockCreativeCursorOwnership Ownership);

    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);

    private static Input Valid(int requestId = -3)
    {
        var item = new BedrockCreativeItemInstance(5, 1, 0, 1921718966, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var entry = new BedrockCreativeEntry(17, item, 0);
        var registryItem = new BedrockItemState("minecraft:oak_planks", 5, false, 2, new NbtCompound());
        var registry = new BedrockRegistrySnapshot(3, ImmutableDictionary<short, string>.Empty.Add(5, registryItem.Name), ImmutableDictionary<string, int>.Empty);
        var catalog = new BedrockCreativeContentPacket([], [entry]);
        var ownership = new BedrockCreativeCursorOwnership(7, registry, catalog, new BedrockOpenContainer(2, -1, 0, 0, 0, -1), new object());
        var before = new BedrockInventorySnapshot(new BedrockContainerAddress(124, 0, null), Enumerable.Range(0, 54).Select(_ => Air()).ToImmutableArray(), Air());
        return new(before, entry, registryItem, Request(entry, registryItem.Name, requestId), ownership);
    }

    private static BedrockItemStackRequestPacket Request(BedrockCreativeEntry entry, string name, int requestId = -3)
    {
        BedrockCreativeItemInstance item = entry.Item;
        var descriptor = new BedrockItemStackResultDescriptor(1, 1, name, checked((int)item.MetadataValue), checked((short)item.Count), checked((uint)item.BlockRuntimeId), item.ExtraData);
        return new([new BedrockItemStackRequest(requestId,
        [
            new(BedrockItemStackActionType.CraftCreative, 14, new BedrockCraftCreativePayload(entry.EntryId, 1)),
            new(BedrockItemStackActionType.ResultsDeprecated, 19, new BedrockResultsDeprecatedPayload([descriptor], 1)),
            new(BedrockItemStackActionType.Take, 0, new BedrockTakePayload(64, new(new(60, null), 50, requestId), new(new(59, null), 0, 0))),
        ], [], -1)]);
    }

    private static BedrockCreativeCursorReconciliation Create(Input input) =>
        BedrockCreativeCursorReconciliation.Create(input.Before, input.Entry, input.RegistryItem, input.Packet, input.Ownership);

    private static BedrockItemStackResponseSlot Row(byte count = 64, int? id = 1) => new(0, 0, count, id, "named", "filtered", 9);

    private static BedrockItemStackResponse Response(params BedrockItemStackResponseSlot[] rows) =>
        new(0, -3, true, [new(new BedrockFullContainerName(59, null), rows)]);

    [Fact]
    public void AcceptedCursorUsesRetainedCatalogFullItemAndSeparateCorrections()
    {
        Input input = Valid();
        BedrockCreativeCursorReconciliation operation = Create(input);
        BedrockItemStackResponseSlot row = Row();

        BedrockCreativeCursorReconciliationResult result = operation.Reconcile(Response(row), input.Ownership);

        Assert.True(result.IsEligible);
        BedrockInventoryReconciledSlot update = Assert.Single(result.Updates);
        Assert.Equal(0, update.Slot);
        Assert.Equal(new BedrockItemInstance(5, 64, 0, 1, 1921718966, input.Entry.Item.ExtraData), update.Item);
        Assert.NotSame(input.Entry.Item.ExtraData, update.Item.ExtraData);
        Assert.Equal(row, update.Response);
        Assert.NotSame(row, update.Response);
        Assert.Equal(new BedrockContainerAddress(124, 0, null), operation.Before.Address);
        Assert.Equal(new[] { 0 }, operation.AffectedSlots.ToArray());
        Assert.Equal(-3, operation.RequestId);
        Assert.Equal(7, operation.Generation);
        Assert.Equal(Air(), input.Before.Items[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(65)]
    public void AuthoritativeCountDoesNotAssertAMaxStackPolicy(byte count)
    {
        Input input = Valid();
        Assert.Equal(count, Assert.Single(Create(input).Reconcile(Response(Row(count)), input.Ownership).Updates).Item.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1)]
    public void ZeroCountReturnsCanonicalAirAndRetainsAllCorrections(int? id)
    {
        Input input = Valid();
        BedrockItemStackResponseSlot row = Row(0, id);
        BedrockInventoryReconciledSlot update = Assert.Single(Create(input).Reconcile(Response(row), input.Ownership).Updates);
        Assert.Equal(Air(), update.Item);
        Assert.Equal(row, update.Response);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void PositiveCountWithoutPositiveServerIdIsEligibleButUnavailable(int? id)
    {
        Input input = Valid();
        BedrockCreativeCursorReconciliationResult result = Create(input).Reconcile(Response(Row(64, id)), input.Ownership);
        Assert.True(result.IsEligible);
        Assert.Empty(result.Updates);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("registry")]
    [InlineData("catalog")]
    [InlineData("screen")]
    [InlineData("reopened")]
    public void ReplacedOwnershipIsIneligibleEvenWhenContentAndWindowValuesMatch(string replacement)
    {
        Input input = Valid();
        BedrockCreativeCursorOwnership current = replacement switch
        {
            "inventory" => input.Ownership with { InventoryGeneration = 8 },
            "registry" => input.Ownership with { Registry = input.Ownership.Registry with { } },
            "catalog" => input.Ownership with { Catalog = new BedrockCreativeContentPacket([], [input.Entry]) },
            "screen" => input.Ownership with { Screen = input.Ownership.Screen with { WindowId = 3 } },
            _ => input.Ownership with { ScreenLifetime = new object() },
        };
        BedrockCreativeCursorReconciliation operation = Create(input);
        Assert.False(operation.IsCurrent(current));
        BedrockCreativeCursorReconciliationResult result = operation.Reconcile(Response(Row()), current);
        Assert.False(result.IsEligible);
        Assert.Empty(result.Updates);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(-3, 7)]
    public void ForeignOrRejectedResponseIsIneligible(int id, byte status)
    {
        Input input = Valid();
        var response = new BedrockItemStackResponse(status, id, true, Response(Row()).Containers);
        BedrockCreativeCursorReconciliationResult result = Create(input).Reconcile(response, input.Ownership);
        Assert.False(result.IsEligible);
        Assert.Empty(result.Updates);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("absent")]
    [InlineData("foreign")]
    [InlineData("dynamic")]
    [InlineData("slot")]
    [InlineData("hotbar")]
    [InlineData("conflict")]
    public void UnknownOrConflictingCursorRowsStayUnavailable(string invalid)
    {
        Input input = Valid();
        BedrockItemStackResponse response = invalid switch
        {
            "missing" => Response(),
            "absent" => new(0, -3, false, []),
            "foreign" => new(0, -3, true, [new(new(60, null), [Row()])]),
            "dynamic" => new(0, -3, true, [new(new(59, 8), [Row()])]),
            "slot" => Response(new BedrockItemStackResponseSlot(1, 1, 64, 1, "", "", 0)),
            "hotbar" => Response(new BedrockItemStackResponseSlot(0, 1, 64, 1, "", "", 0)),
            _ => Response(Row(), Row(63)),
        };
        BedrockCreativeCursorReconciliationResult result = Create(input).Reconcile(response, input.Ownership);
        Assert.True(result.IsEligible);
        Assert.Empty(result.Updates);
    }

    [Fact]
    public void IdenticalDuplicateCursorRowsProduceOneUpdateAndIgnoreUnrelatedSlots()
    {
        Input input = Valid();
        var response = Response(Row(), new(4, 4, 1, 2, "", "", 0), Row());
        Assert.Single(Create(input).Reconcile(response, input.Ownership).Updates);
    }

    [Theory]
    [InlineData("order")]
    [InlineData("creativeLegacy")]
    [InlineData("resultsLegacy")]
    [InlineData("takeLegacy")]
    [InlineData("itemId")]
    [InlineData("creativeTimes")]
    [InlineData("resultsTimes")]
    [InlineData("descriptor")]
    [InlineData("takeCount")]
    [InlineData("createdSlot")]
    [InlineData("createdId")]
    [InlineData("createdDynamic")]
    [InlineData("cursorId")]
    [InlineData("cursorSlot")]
    [InlineData("names")]
    [InlineData("cause")]
    [InlineData("evenId")]
    [InlineData("positiveId")]
    public void UnsupportedRequestShapesAreRejectedBeforeRetention(string invalid)
    {
        Input input = Valid();
        BedrockItemStackRequest request = input.Packet.Requests[0];
        BedrockItemStackAction[] actions = request.Actions.ToArray();
        var creative = (BedrockCraftCreativePayload)actions[0].Payload;
        var results = (BedrockResultsDeprecatedPayload)actions[1].Payload;
        var take = (BedrockTakePayload)actions[2].Payload;
        switch (invalid)
        {
            case "order": (actions[0], actions[1]) = (actions[1], actions[0]); break;
            case "creativeLegacy": actions[0] = new(actions[0].Type, 12, creative); break;
            case "resultsLegacy": actions[1] = new(actions[1].Type, 17, results); break;
            case "takeLegacy": actions[2] = new(actions[2].Type, 1, take); break;
            case "itemId": actions[0] = new(actions[0].Type, 14, creative with { ItemId = 18 }); break;
            case "creativeTimes": actions[0] = new(actions[0].Type, 14, creative with { TimesCrafted = 2 }); break;
            case "resultsTimes": actions[1] = new(actions[1].Type, 19, results with { TimesCrafted = 2 }); break;
            case "descriptor": actions[1] = new(actions[1].Type, 19, new BedrockResultsDeprecatedPayload([new(1, 1, input.RegistryItem.Name, 1, 1, 1921718966, input.Entry.Item.ExtraData)], 1)); break;
            case "takeCount": actions[2] = new(actions[2].Type, 0, take with { Count = 1 }); break;
            case "createdSlot": actions[2] = new(actions[2].Type, 0, take with { Source = take.Source with { Slot = 0 } }); break;
            case "createdId": actions[2] = new(actions[2].Type, 0, take with { Source = take.Source with { StackId = -4 } }); break;
            case "createdDynamic": actions[2] = new(actions[2].Type, 0, take with { Source = take.Source with { Container = new(60, 8) } }); break;
            case "cursorId": actions[2] = new(actions[2].Type, 0, take with { Destination = take.Destination with { StackId = 1 } }); break;
            case "cursorSlot": actions[2] = new(actions[2].Type, 0, take with { Destination = take.Destination with { Slot = 1 } }); break;
        }
        int id = invalid == "evenId" ? -4 : invalid == "positiveId" ? 1 : -3;
        var changed = new BedrockItemStackRequestPacket([new(id, actions, invalid == "names" ? ["extra"] : [], invalid == "cause" ? 0 : -1)]);
        Assert.Throws<InvalidOperationException>(() => Create(input with { Packet = changed }));
    }

    [Theory]
    [InlineData("address")]
    [InlineData("size")]
    [InlineData("unknown")]
    [InlineData("nonempty")]
    [InlineData("registryName")]
    [InlineData("runtimeId")]
    [InlineData("catalogReplacement")]
    [InlineData("negativeBlock")]
    [InlineData("networkOverflow")]
    [InlineData("screenType")]
    [InlineData("screenWindow")]
    public void UnknownBaselineIdentityOrUnqualifiedRepresentationIsRejected(string invalid)
    {
        Input input = Valid();
        switch (invalid)
        {
            case "address": input = input with { Before = input.Before with { Address = new(0, 0, null) } }; break;
            case "size": input = input with { Before = input.Before with { Items = [Air()] } }; break;
            case "unknown": input = input with { Before = input.Before with { KnownSlots = Enumerable.Repeat(false, 54).ToImmutableArray() } }; break;
            case "nonempty": input = input with { Before = input.Before with { Items = input.Before.Items.SetItem(0, new(5, 1, 0, 2, 0, [])) } }; break;
            case "registryName": input = input with { RegistryItem = input.RegistryItem with { Name = "minecraft:stone" } }; break;
            case "runtimeId": input = input with { RegistryItem = input.RegistryItem with { RuntimeId = 1 } }; break;
            case "catalogReplacement": input = input with { Entry = new(input.Entry.EntryId, input.Entry.Item, 0) }; break;
            case "negativeBlock": input = input with { Entry = new(17, new(5, 1, 0, -1, []), 0) }; break;
            case "networkOverflow": input = input with { Entry = new(17, new(40000, 1, 0, 0, []), 0) }; break;
            case "screenType": input = input with { Ownership = input.Ownership with { Screen = input.Ownership.Screen with { WindowType = 0 } } }; break;
            case "screenWindow": input = input with { Ownership = input.Ownership with { Screen = input.Ownership.Screen with { WindowId = 124 } } }; break;
        }
        Assert.Throws<InvalidOperationException>(() => Create(input));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("legacy")]
    [InlineData("name")]
    [InlineData("metadata")]
    [InlineData("count")]
    [InlineData("block")]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("multiple")]
    public void EveryResultDescriptorFieldMustMatchTheRetainedCatalog(string mismatch)
    {
        Input input = Valid();
        var request = input.Packet.Requests[0];
        var results = (BedrockResultsDeprecatedPayload)request.Actions[1].Payload;
        BedrockItemStackResultDescriptor row = results.ResultItems[0];
        var changed = new BedrockItemStackResultDescriptor(mismatch == "kind" ? 0 : row.Type, mismatch == "legacy" ? (byte)0 : row.LegacyType,
            mismatch == "name" ? "minecraft:stone" : row.Name, mismatch == "metadata" ? 1 : row.Metadata, mismatch == "count" ? (short)2 : row.Count,
            mismatch == "block" ? 1u : row.BlockRuntimeId, mismatch == "extra" ? [1] : row.ExtraData);
        ImmutableArray<BedrockItemStackResultDescriptor> rows = mismatch == "missing" ? [] : mismatch == "multiple" ? [row, row] : [changed];
        BedrockItemStackAction[] actions = request.Actions.ToArray();
        actions[1] = new(BedrockItemStackActionType.ResultsDeprecated, 19, new BedrockResultsDeprecatedPayload(rows, 1));
        Assert.Throws<InvalidOperationException>(() => Create(input with { Packet = new([new(-3, actions, [], -1)]) }));
    }

    [Theory]
    [InlineData("knownMask")]
    [InlineData("correctionMask")]
    [InlineData("airIdentity")]
    [InlineData("airOpaque")]
    public void MalformedOrNoncanonicalEmptyBaselineCannotBeRetained(string invalid)
    {
        Input input = Valid();
        BedrockInventorySnapshot before = invalid switch
        {
            "knownMask" => input.Before with { KnownSlots = [true] },
            "correctionMask" => input.Before with { ResponseCorrections = [null] },
            "airIdentity" => input.Before with { Items = input.Before.Items.SetItem(0, new(0, 0, 0, 1, 0, [])) },
            _ => input.Before with { Items = input.Before.Items.SetItem(0, new(0, 0, 0, null, 0, [1])) },
        };
        Assert.Throws<InvalidOperationException>(() => Create(input with { Before = before }));
    }

    [Fact]
    public void DeepSnapshotCopiesPreserveUnrelatedItemsAndBaselineCorrections()
    {
        Input input = Valid();
        var unrelated = new BedrockItemInstance(306, 1, 4, 8, 0, [1, 2]);
        BedrockItemStackResponseSlot correction = Row(0);
        input = input with
        {
            Before = input.Before with
            {
                Items = input.Before.Items.SetItem(1, unrelated),
                ResponseCorrections = Enumerable.Range(0, 54).Select(index => index == 0 ? correction : null).ToImmutableArray(),
            }
        };
        BedrockCreativeCursorReconciliation operation = Create(input);
        unrelated.ExtraData[0] = 9;
        operation.Before.Items[1].ExtraData[0] = 10;
        Assert.Equal(1, operation.Before.Items[1].ExtraData[0]);
        Assert.Equal(correction, operation.Before.ResponseCorrections[0]);
        Assert.NotSame(correction, operation.Before.ResponseCorrections[0]);
    }

    [Fact]
    public void RetainedRequestCatalogRegistryAndSnapshotCopiesDoNotAliasCallerData()
    {
        Input input = Valid();
        var compound = (NbtCompound)input.RegistryItem.Components;
        compound["extra"] = new NbtByteArray([1, 2]);
        BedrockCreativeCursorReconciliation operation = Create(input);
        input.Entry.Item.ExtraData[0] = 42;
        ((BedrockResultsDeprecatedPayload)input.Packet.Requests[0].Actions[1].Payload).ResultItems[0].ExtraData[0] = 43;
        ((NbtByteArray)compound["extra"]).Value[0] = 44;
        operation.CatalogEntry.Item.ExtraData[0] = 45;
        ((BedrockResultsDeprecatedPayload)operation.Packet.Requests[0].Actions[1].Payload).ResultItems[0].ExtraData[0] = 46;
        ((NbtByteArray)((NbtCompound)operation.RegistryItem.Components)["extra"]).Value[0] = 47;

        Assert.Equal(0, operation.CatalogEntry.Item.ExtraData[0]);
        Assert.Equal(0, ((BedrockResultsDeprecatedPayload)operation.Packet.Requests[0].Actions[1].Payload).ResultItems[0].ExtraData[0]);
        Assert.Equal(1, ((NbtByteArray)((NbtCompound)operation.RegistryItem.Components)["extra"]).Value[0]);
        Assert.Equal(0, Assert.Single(operation.Reconcile(Response(Row()), input.Ownership).Updates).Item.ExtraData[0]);
    }

    [Fact]
    public void OddNegativeIdIsRetainedWithoutRequiringTheFirstAllocation()
    {
        Input input = Valid(-5);
        BedrockCreativeCursorReconciliation operation = Create(input);
        var response = new BedrockItemStackResponse(0, -5, true, Response(Row()).Containers);
        Assert.True(operation.Reconcile(response, input.Ownership).IsEligible);
        Assert.Equal(-5, operation.RequestId);
    }
}
