using System.Collections.Immutable;
using Umpk.Nbt;
using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockCursorPlaceReconciliationTests
{
    internal static readonly BedrockContainerAddress Pool = new(124, 0, null);
    internal static readonly BedrockContainerAddress Player = new(0, 0, null);
    internal static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    internal static BedrockItemInstance Oak(ushort count = 64, int id = 88) => new(5, count, 0, id, 77, new byte[10]);
    internal sealed record Input(BedrockInventoryStore Store, BedrockInventorySnapshot Cursor, BedrockInventorySnapshot Player, BedrockCreativeEntry Entry, BedrockItemState Item, BedrockItemStackRequestPacket Packet, BedrockCreativeCursorOwnership Ownership);
    internal static Input Valid()
    {
        var store = new BedrockInventoryStore();
        BedrockItemInstance[] cursor = Enumerable.Range(0, 54).Select(_ => Air()).ToArray();
        cursor[0] = Oak();
        store.ApplyContent(new(124, cursor, Pool, Air()), store.Generation);
        store.ApplyContent(new(0, Enumerable.Range(0, 36).Select(_ => Air()).ToArray(), Player, Air()), store.Generation);
        var item = new BedrockItemState("minecraft:oak_planks", 5, false, 2, new NbtCompound());
        var entry = new BedrockCreativeEntry(17, new(5, 1, 0, 77, new byte[10]), 0);
        var catalog = new BedrockCreativeContentPacket([], [entry]);
        var registry = new BedrockRegistrySnapshot(1, ImmutableDictionary<short, string>.Empty.Add(5, item.Name), ImmutableDictionary<string, int>.Empty);
        var owner = new BedrockCreativeCursorOwnership(1, registry, catalog, new(2, -1, 0, 0, 0, -1), new object());
        return new(store, store.GetSnapshot(Pool)!.Value, store.GetSnapshot(Player)!.Value, entry, item, Packet(), owner);
    }

    internal static BedrockItemStackRequestPacket Packet(int id = -5, byte count = 64, byte source = 59, byte destination = 28, byte sourceSlot = 0, byte destinationSlot = 0, int sourceId = 88, int destinationId = 0, int cause = -1, byte legacy = 1) => new([new(id,
        [new(BedrockItemStackActionType.Place, legacy, new BedrockPlacePayload(count, new(new(source, null), sourceSlot, sourceId), new(new(destination, null), destinationSlot, destinationId)))], [], cause)]);
    internal static BedrockCursorPlaceReconciliation Create(Input input) => BedrockCursorPlaceReconciliation.Create(input.Cursor, input.Player, input.Entry, input.Item, input.Packet, input.Ownership);
    internal static BedrockItemStackResponseSlot Row(byte count, int? id, string name = "named") => new(0, 0, count, id, name, "filtered", 9);
    internal static BedrockItemStackResponse Response(BedrockItemStackResponseSlot? cursor = null, BedrockItemStackResponseSlot? player = null, int id = -5, byte status = 0) => new(status, id, true,
        [new(new(59, null), [cursor ?? Row(0, 88)]), new(new(28, null), [player ?? Row(64, 88)])]);

    [Fact]
    public void AcceptedRowsMaterializeBothKnownTypesAndPreserveCorrections()
    {
        Input input = Valid();
        BedrockCursorPlaceReconciliation operation = Create(input);
        var result = operation.Reconcile(Response(), input.Ownership);
        Assert.True(result.IsEligible);
        Assert.Equal(Air(), Assert.Single(result.CursorUpdates).Item);
        Assert.Equal(Oak(), Assert.Single(result.PlayerUpdates).Item);
        Assert.Equal(Row(64, 88), Assert.Single(result.PlayerUpdates).Response);
        Assert.Equal(-5, operation.RequestId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(88)]
    public void ZeroCountIsCanonicalAirRegardlessOfReturnedId(int? id)
    {
        Input input = Valid();
        var update = Assert.Single(Create(input).Reconcile(Response(cursor: Row(0, id)), input.Ownership).CursorUpdates);
        Assert.Equal(Air(), update.Item);
        Assert.Equal(id, update.Response.StackId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void PositiveCountWithoutPositiveIdIsUnavailable(int? id)
    {
        Input input = Valid();
        var result = Create(input).Reconcile(Response(player: Row(64, id)), input.Ownership);
        Assert.True(result.IsEligible);
        Assert.Empty(result.PlayerUpdates);
        Assert.Single(result.CursorUpdates);
    }

    [Fact]
    public void RetentionClonesOpaqueDataAndReturnedRows()
    {
        Input input = Valid();
        BedrockCursorPlaceReconciliation operation = Create(input);
        input.Cursor.Items[0].ExtraData[0] = 1;
        input.Entry.Item.ExtraData[0] = 2;
        var row = Row(63, 99);
        var update = Assert.Single(operation.Reconcile(Response(player: row), input.Ownership).PlayerUpdates);
        Assert.Equal(new byte[10], update.Item.ExtraData);
        Assert.Equal(63, update.Item.Count);
        Assert.Equal(99, update.Item.StackNetworkId);
        Assert.Equal(row, update.Response);
        Assert.NotSame(row, update.Response);
    }

    [Theory]
    [InlineData("id-positive")]
    [InlineData("id-even")]
    [InlineData("count")]
    [InlineData("legacy")]
    [InlineData("cause")]
    [InlineData("source-container")]
    [InlineData("destination-container")]
    [InlineData("source-slot")]
    [InlineData("destination-slot")]
    [InlineData("source-id")]
    [InlineData("destination-id")]
    [InlineData("entry")]
    [InlineData("definition")]
    [InlineData("version")]
    [InlineData("unknown-cursor")]
    [InlineData("unknown-player")]
    [InlineData("occupied")]
    [InlineData("source-count")]
    public void UnqualifiedInputRejects(string change)
    {
        Input input = Valid();
        input = change switch
        {
            "id-positive" => input with { Packet = Packet(id: 3) },
            "id-even" => input with { Packet = Packet(id: -4) },
            "count" => input with { Packet = Packet(count: 1) },
            "legacy" => input with { Packet = Packet(legacy: 0) },
            "cause" => input with { Packet = Packet(cause: 0) },
            "source-container" => input with { Packet = Packet(source: 60) },
            "destination-container" => input with { Packet = Packet(destination: 29) },
            "source-slot" => input with { Packet = Packet(sourceSlot: 50) },
            "destination-slot" => input with { Packet = Packet(destinationSlot: 1) },
            "source-id" => input with { Packet = Packet(sourceId: 89) },
            "destination-id" => input with { Packet = Packet(destinationId: 1) },
            "entry" => input with { Entry = new(input.Entry.EntryId, input.Entry.Item, 0) },
            "definition" => input with { Item = input.Item with { ComponentBased = true } },
            "version" => input with { Item = input.Item with { Version = 3 } },
            "unknown-cursor" => input with { Cursor = input.Cursor with { KnownSlots = Enumerable.Repeat(false, 54).ToImmutableArray() } },
            "unknown-player" => input with { Player = input.Player with { KnownSlots = Enumerable.Repeat(false, 36).ToImmutableArray() } },
            "occupied" => input with { Player = input.Player with { Items = input.Player.Items.SetItem(0, Oak()) } },
            "source-count" => input with { Cursor = input.Cursor with { Items = input.Cursor.Items.SetItem(0, Oak(63)) } },
            _ => throw new InvalidOperationException(),
        };
        Assert.Throws<InvalidOperationException>(() => Create(input));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("status")]
    [InlineData("screen")]
    [InlineData("catalog")]
    [InlineData("generation")]
    public void StaleOrUnrelatedResponseIsIneligible(string change)
    {
        Input input = Valid();
        var owner = change switch
        {
            "screen" => input.Ownership with { ScreenLifetime = new object() },
            "catalog" => input.Ownership with { Catalog = new([], [input.Entry]) },
            "generation" => input.Ownership with { InventoryGeneration = 2 },
            _ => input.Ownership,
        };
        var result = Create(input).Reconcile(Response(id: change == "request" ? -7 : -5, status: change == "status" ? (byte)7 : (byte)0), owner);
        Assert.False(result.IsEligible);
        Assert.Empty(result.CursorUpdates);
        Assert.Empty(result.PlayerUpdates);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("dynamic")]
    [InlineData("hotbar")]
    [InlineData("conflict")]
    [InlineData("identical")]
    public void OnlyQualifiedRowsBecomeUpdates(string variant)
    {
        Input input = Valid();
        var row = Row(64, 88);
        var container = variant switch
        {
            "missing" => new BedrockItemStackResponseContainer(new(29, null), [row]),
            "dynamic" => new BedrockItemStackResponseContainer(new(28, 1), [row]),
            "hotbar" => new BedrockItemStackResponseContainer(new(28, null), [new(0, 1, 64, 88, "", "", 0)]),
            "conflict" => new BedrockItemStackResponseContainer(new(28, null), [row, Row(64, 88, "different")]),
            _ => new BedrockItemStackResponseContainer(new(28, null), [row, row]),
        };
        var result = Create(input).Reconcile(new(0, -5, true, [container]), input.Ownership);
        Assert.True(result.IsEligible);
        Assert.Empty(result.CursorUpdates);
        Assert.Equal(variant == "identical" ? 1 : 0, result.PlayerUpdates.Length);
    }
    [Theory]
    [InlineData("registry")]
    [InlineData("inventory")]
    [InlineData("window")]
    [InlineData("type")]
    public void UninitializedOwnershipCannotQualifyTransfer(string field)
    {
        Input input = Valid();
        var owner = field switch
        {
            "registry" => input.Ownership with { Registry = new(0, input.Ownership.Registry.Items, input.Ownership.Registry.Entities) },
            "inventory" => input.Ownership with { InventoryGeneration = 0 },
            "window" => input.Ownership with { Screen = input.Ownership.Screen with { WindowId = 0 } },
            _ => input.Ownership with { Screen = input.Ownership.Screen with { WindowType = 0 } },
        };
        Assert.Throws<InvalidOperationException>(() => Create(input with { Ownership = owner }));
    }

}
