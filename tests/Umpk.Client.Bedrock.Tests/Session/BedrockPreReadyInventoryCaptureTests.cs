using Umpk.Client.Bedrock.State.Inventory;
using Umpk.Protocol.Bedrock;
using Xunit;
using Fixture = Umpk.Client.Bedrock.Session.BedrockItemStackOwnershipTests.OwnershipSession;

namespace Umpk.Client.Bedrock.Session;

[CollectionDefinition("Bedrock pre-ready inventory capture", DisableParallelization = true)]
public sealed class BedrockPreReadyInventoryCaptureCollection;

[Collection("Bedrock pre-ready inventory capture")]
public sealed class BedrockPreReadyInventoryCaptureTests
{
    private const string CaptureVariable = "UMPK_BEDROCK_CAPTURE_INVENTORY";
    private static readonly BedrockContainerAddress UiPool = new(124, 0, null);
    private static BedrockItemInstance Air() => new(0, 0, 0, null, 0, []);
    private static BedrockInventoryContentPacket Content() => new(124, Enumerable.Range(0, 54).Select(_ => Air()).ToArray(), UiPool, Air());
    private static BedrockInventorySlotPacket Slot() => new(124, 7, UiPool, Air(), new BedrockItemInstance(306, 2, 3, 9, 0, new byte[10]));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdmittedPreReadyInventoryRetainsRawPayloadsOnlyWhenEnabledAndAppliesDecodedBaseline(bool capture)
    {
        string? previous = Environment.GetEnvironmentVariable(CaptureVariable);
        Environment.SetEnvironmentVariable(CaptureVariable, capture ? "1" : null);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            BedrockInventoryContentPacket content = Content();
            BedrockInventorySlotPacket slot = Slot();
            byte[] contentBytes = BedrockInventoryCodecs.Encode(content);
            byte[] slotBytes = BedrockInventoryCodecs.Encode(slot);
            bool observedBeforeReady = false;
            await using Fixture fixture = await Fixture.CreateAsync(ct, preReadyInventory: [(49, contentBytes), (50, slotBytes)], beforeReady: (session, _) =>
            {
                Assert.False(session.Snapshot.IsReady);
                Assert.False(session.Completion.IsCompleted);
                BedrockInventorySnapshot saved = session.InventoryStore.GetSnapshot(UiPool)!.Value;
                Assert.Equal(54, saved.Items.Length);
                Assert.Equal(Air(), saved.Items[0]);
                Assert.True(saved.IsSlotKnown(0));
                Assert.True(saved.IsSlotKnown(50));
                Assert.Equal(slot.Item, saved.Items[7]);
                Assert.True(saved.IsSlotKnown(7));
                Assert.Equal(content.StorageItem, saved.StorageItem);
                Assert.Equal(1, session.InventoryStore.Generation);
                if (capture)
                {
                    Assert.Equal(content, Assert.Single(session.CapturedInventoryContents));
                    Assert.Equal(slot, Assert.Single(session.CapturedInventorySlots));
                    byte[] retainedContent = Assert.Single(session.CapturedInventoryContentPayloads);
                    byte[] retainedSlot = Assert.Single(session.CapturedInventorySlotPayloads);
                    Assert.Equal(contentBytes, retainedContent);
                    Assert.Equal(slotBytes, retainedSlot);
                    Assert.Equal(content, BedrockInventoryCodecs.DecodeContent(retainedContent));
                    Assert.Equal(slot, BedrockInventoryCodecs.DecodeSlot(retainedSlot));
                    Assert.Equal(retainedContent, BedrockInventoryCodecs.Encode(BedrockInventoryCodecs.DecodeContent(retainedContent)));
                    Assert.Equal(retainedSlot, BedrockInventoryCodecs.Encode(BedrockInventoryCodecs.DecodeSlot(retainedSlot)));
                    retainedContent[0] ^= 0xff;
                    retainedSlot[0] ^= 0xff;
                    Assert.Equal(contentBytes, Assert.Single(session.CapturedInventoryContentPayloads));
                    Assert.Equal(slotBytes, Assert.Single(session.CapturedInventorySlotPayloads));
                }
                else
                {
                    Assert.Empty(session.CapturedInventoryContentPayloads);
                    Assert.Empty(session.CapturedInventorySlotPayloads);
                    Assert.Empty(session.CapturedInventoryContents);
                    Assert.Empty(session.CapturedInventorySlots);
                }
                observedBeforeReady = true;
                return Task.CompletedTask;
            });
            Assert.True(observedBeforeReady);
            Assert.True(fixture.Session.Snapshot.IsReady);
            Assert.Equal(slot.Item, fixture.Session.InventoryStore.GetSnapshot(UiPool)!.Value.Items[7]);
            if (capture)
            {
                Assert.Equal(2, fixture.Session.CapturedInventoryContentPayloads.Count);
                Assert.Equal(contentBytes, fixture.Session.CapturedInventoryContentPayloads[0]);
                Assert.Equal(slotBytes, Assert.Single(fixture.Session.CapturedInventorySlotPayloads));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaptureVariable, previous);
        }
    }

    [Theory]
    [InlineData(49)]
    [InlineData(50)]
    public async Task DecodeFailureBeforeAdmissionDoesNotRetainMalformedInventoryPayload(int packetId)
    {
        string? previous = Environment.GetEnvironmentVariable(CaptureVariable);
        Environment.SetEnvironmentVariable(CaptureVariable, "1");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            CancellationToken ct = timeout.Token;
            byte[] complete = packetId == 49 ? BedrockInventoryCodecs.Encode(Content()) : BedrockInventoryCodecs.Encode(Slot());
            byte[] truncated = complete[..^1];
            BedrockClientSession? observed = null;
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using Fixture fixture = await Fixture.CreateAsync(ct, beforeJoin: (session, _) =>
                {
                    observed = session;
                    return Task.CompletedTask;
                }, preReadyInventory: [(packetId, truncated)]);
            });
            Assert.NotNull(observed);
            Assert.Equal(BedrockSessionEndReason.ProtocolFailure, (await observed.Completion.WaitAsync(ct)).Reason);
            Assert.Empty(observed.CapturedInventoryContentPayloads);
            Assert.Empty(observed.CapturedInventorySlotPayloads);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaptureVariable, previous);
        }
    }
}
