using Umpk.Protocol.Bedrock;
using Xunit;
using Cases = Umpk.Client.Bedrock.State.Inventory.BedrockCursorPlaceReconciliationTests;

namespace Umpk.Client.Bedrock.State.Inventory;

public sealed class BedrockCursorPlaceStoreTests
{
    private static BedrockInventoryReconciledSlot Update(byte count, int? id) => new(0, count == 0 ? Cases.Air() : Cases.Oak(count, id!.Value), Cases.Row(count, id));

    [Fact]
    public void CurrentPairCommitsBothAndPreservesStorageAndSiblings()
    {
        var input = Cases.Valid();
        Assert.True(input.Store.TryApplyCursorPlaceResponse(input.Cursor, input.Player, [Update(0, 88)], [Update(64, 88)], 1));
        var cursor = input.Store.GetSnapshot(Cases.Pool)!.Value;
        var player = input.Store.GetSnapshot(Cases.Player)!.Value;
        Assert.Equal(Cases.Air(), cursor.Items[0]);
        Assert.Equal(Cases.Oak(), player.Items[0]);
        Assert.True(cursor.IsSlotKnown(0));
        Assert.True(player.IsSlotKnown(0));
        Assert.Equal(input.Cursor.StorageItem, cursor.StorageItem);
        Assert.Equal(input.Player.Items.Skip(1).ToArray(), player.Items.Skip(1).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameValueAuthoritativeReplacementCannotBeOverwrittenOrInvalidated(bool player)
    {
        var input = Cases.Valid();
        BedrockInventorySnapshot before = player ? input.Player : input.Cursor;
        input.Store.ApplyContent(new(before.Address.WindowId, before.Items, before.Address, before.StorageItem), 1);
        Assert.False(input.Store.TryApplyCursorPlaceResponse(input.Cursor, input.Player, [Update(0, 88)], [Update(64, 88)], 1));
        input.Store.InvalidateCursorPlaceSlots(input.Cursor, input.Player, 1);
        var replacement = input.Store.GetSnapshot(before.Address)!.Value;
        Assert.True(replacement.IsSlotKnown(0));
        Assert.Equal(before.Items[0], replacement.Items[0]);
        Assert.False(input.Store.GetSnapshot(player ? Cases.Pool : Cases.Player)!.Value.IsSlotKnown(0));
    }

    [Fact]
    public void UnrelatedSlotPublicationDoesNotRetireCursorPair()
    {
        var input = Cases.Valid();
        input.Store.ApplySlot(new(124, 50, Cases.Pool, null, Cases.Oak(1, 55)), Cases.Pool, 1);
        Assert.True(input.Store.MatchesCursorPlaceSnapshots(input.Cursor, input.Player, 1));
        Assert.True(input.Store.TryApplyCursorPlaceResponse(input.Cursor, input.Player, [Update(0, 88)], [Update(64, 88)], 1));
        Assert.Equal(Cases.Oak(1, 55), input.Store.GetSnapshot(Cases.Pool)!.Value.Items[50]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentIncompleteResponseMakesOnlyMissingRowUnavailable(bool missingCursor)
    {
        var input = Cases.Valid();
        Assert.True(input.Store.TryApplyCursorPlaceResponse(input.Cursor, input.Player, missingCursor ? [] : [Update(0, null)], missingCursor ? [Update(64, 88)] : [], 1));
        Assert.Equal(!missingCursor, input.Store.GetSnapshot(Cases.Pool)!.Value.IsSlotKnown(0));
        Assert.Equal(missingCursor, input.Store.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
    }

    [Fact]
    public void MalformedSecondGroupCannotPartiallyPublishFirst()
    {
        var input = Cases.Valid();
        Assert.Throws<InvalidOperationException>(() => input.Store.TryApplyCursorPlaceResponse(input.Cursor, input.Player, [Update(0, null)], [new(1, Cases.Oak(), Cases.Row(64, 88))], 1));
        Assert.Equal(Cases.Oak(), input.Store.GetSnapshot(Cases.Pool)!.Value.Items[0]);
        Assert.True(input.Store.GetSnapshot(Cases.Pool)!.Value.IsSlotKnown(0));
    }

    [Fact]
    public void ActualEventSharesReceiptAcrossTouchedSlotsOnly()
    {
        var input = Cases.Valid();
        Assert.False(input.Cursor.AuthorityReceipts.IsDefault);
        Assert.Same(input.Cursor.AuthorityReceipts[0], input.Cursor.AuthorityReceipts[50]);
        input.Store.ApplySlot(new(124, 50, Cases.Pool, null, Cases.Air()), Cases.Pool, 1);
        var after = input.Store.GetSnapshot(Cases.Pool)!.Value;
        Assert.Same(input.Cursor.AuthorityReceipts[0], after.AuthorityReceipts[0]);
        Assert.NotSame(input.Cursor.AuthorityReceipts[50], after.AuthorityReceipts[50]);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public void ResizedActualContentRemainsKnownAndRejectsOldPair(int size)
    {
        var input = Cases.Valid();
        input.Store.ApplyContent(new(124, Enumerable.Range(0, size).Select(_ => Cases.Air()).ToArray(), Cases.Pool, Cases.Air()), 1);
        Assert.False(input.Store.TryApplyCursorPlaceResponse(input.Cursor, input.Player, [Update(0, 88)], [Update(64, 88)], 1));
        input.Store.InvalidateCursorPlaceSlots(input.Cursor, input.Player, 1);
        Assert.Equal(size, input.Store.GetSnapshot(Cases.Pool)!.Value.Items.Length);
    }

    [Theory]
    [InlineData("corrections")]
    [InlineData("tokens")]
    [InlineData("known")]
    public void MalformedRetainedArraysRejectWithoutPartialPublication(string field)
    {
        var input = Cases.Valid();
        var before = field switch
        {
            "corrections" => input.Cursor with { ResponseCorrections = [] },
            "tokens" => input.Cursor with { AuthorityReceipts = [] },
            _ => input.Cursor with { KnownSlots = [] },
        };
        Assert.False(input.Store.TryApplyCursorPlaceResponse(before, input.Player, [Update(0, 88)], [Update(64, 88)], 1));
        Assert.Equal(Cases.Oak(), input.Store.GetSnapshot(Cases.Pool)!.Value.Items[0]);
        Assert.True(input.Store.GetSnapshot(Cases.Player)!.Value.IsSlotKnown(0));
    }

    [Fact]
    public void CorrectedSameItemPublicationSurvivesRetirementOfOldPair()
    {
        var input = Cases.Valid();
        var corrected = new BedrockInventoryReconciledSlot(0, Cases.Oak(), Cases.Row(64, 88, "corrected"));
        input.Store.ApplyResponseUpdates(input.Cursor, [corrected], [0], 1);
        Assert.False(input.Store.TryApplyCursorPlaceResponse(input.Cursor, input.Player, [Update(0, 88)], [Update(64, 88)], 1));
        input.Store.InvalidateCursorPlaceSlots(input.Cursor, input.Player, 1);
        var after = input.Store.GetSnapshot(Cases.Pool)!.Value;
        Assert.True(after.IsSlotKnown(0));
        Assert.Equal("corrected", after.ResponseCorrections[0]!.CustomName);
    }

}
