using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The 1.20.2+ chunk-batch handshake. The server starts with <c>maxUnacknowledgedBatches = 1</c>: it sends one batch and then sends nothing further until the client replies with <c>chunk_batch_received</c>. Chunks it never sends stay pending, and entities in pending chunks are not broadcast. A client that never acknowledges therefore gets roughly nine chunks of terrain and no entities beyond them, on every version from 1.20.2 onward.</summary>
public sealed class ChunkBatchAckTests
{
    // One per era of the batch packets: 764 is 1.20.2 where the handshake was introduced, 767 is 1.21, 772 is 1.21.5, and 776 is 26.2. The packets have not changed shape, so this is a binding check.
    public static TheoryData<int> Protocols => [764, 767, 772, 776];

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task Finished_Batch_Is_Acknowledged(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol), time: new ManualTimeProvider());

        await harness.ApplyAsync(new ClientboundChunkBatchStartPacket());
        await harness.ApplyAsync(new ClientboundChunkBatchFinishedPacket(9));

        ServerboundChunkBatchReceivedPacket ack = Assert.Single(
            harness.Recorder.Packets.OfType<ServerboundChunkBatchReceivedPacket>());
        Assert.True(ack.DesiredChunksPerTick > 0f);
    }

    [Fact]
    public async Task Acknowledgement_Does_Not_Require_The_Terrain_Feature()
    {
        // The stalled sender also stops entity broadcasts, so a client running with terrain off but entities on still needs the handshake. The world applier is not even in the chain here.
        var harness = new ApplierHarness(
            Version(776),
            new ClientFeatures { Terrain = false, Entities = true },
            new ManualTimeProvider());

        await harness.ApplyAsync(new ClientboundChunkBatchStartPacket());
        await harness.ApplyAsync(new ClientboundChunkBatchFinishedPacket(9));

        Assert.Single(harness.Recorder.Packets.OfType<ServerboundChunkBatchReceivedPacket>());
    }

    [Fact]
    public async Task Every_Batch_Is_Acknowledged_Not_Just_The_First()
    {
        var harness = new ApplierHarness(Version(776), time: new ManualTimeProvider());

        for (int i = 0; i < 4; i++)
        {
            await harness.ApplyAsync(new ClientboundChunkBatchStartPacket());
            await harness.ApplyAsync(new ClientboundChunkBatchFinishedPacket(9));
        }

        Assert.Equal(4, harness.Recorder.Packets.OfType<ServerboundChunkBatchReceivedPacket>().Count());
    }

    [Fact]
    public async Task A_Finished_Batch_Without_A_Start_Is_Still_Acknowledged()
    {
        // Leaving it unanswered is precisely what stalls the stream, so a missing start must not be a reason to stay silent; the starting estimate is used instead.
        var harness = new ApplierHarness(Version(776), time: new ManualTimeProvider());

        await harness.ApplyAsync(new ClientboundChunkBatchFinishedPacket(4));

        Assert.Single(harness.Recorder.Packets.OfType<ServerboundChunkBatchReceivedPacket>());
    }

    [Fact]
    public async Task A_Slower_Batch_Reports_A_Lower_Rate()
    {
        float fast = await RateAfterBatchAsync(TimeSpan.FromMilliseconds(1), batchSize: 9);
        float slow = await RateAfterBatchAsync(TimeSpan.FromMilliseconds(300), batchSize: 9);

        Assert.True(slow < fast, $"expected the slower batch to report a lower rate, got {slow} >= {fast}");
    }

    [Fact]
    public void Empty_Batches_Carry_No_Timing_And_Do_Not_Move_The_Estimate()
    {
        var time = new ManualTimeProvider();
        var calculator = new ChunkBatchSizeCalculator(time);
        float initial = calculator.DesiredChunksPerTick;

        calculator.OnBatchStart();
        time.Advance(TimeSpan.FromSeconds(5));
        calculator.OnBatchFinished(0);

        Assert.Equal(initial, calculator.DesiredChunksPerTick);
    }

    [Fact]
    public void The_Starting_Rate_Matches_Vanilla()
    {
        // Vanilla starts at 2,000,000 nanos per chunk against a 7,000,000 nanos-per-tick budget.
        var calculator = new ChunkBatchSizeCalculator(new ManualTimeProvider());

        Assert.Equal(3.5f, calculator.DesiredChunksPerTick, 4);
    }

    [Fact]
    public void One_Stalled_Batch_Cannot_Swing_The_Estimate_By_More_Than_Vanillas_Clamp()
    {
        // Vanilla clamps each sample to within a factor of three of the running mean, so a single pathological batch degrades the rate gently instead of collapsing it.
        var time = new ManualTimeProvider();
        var calculator = new ChunkBatchSizeCalculator(time);

        calculator.OnBatchStart();
        time.Advance(TimeSpan.FromSeconds(30));
        calculator.OnBatchFinished(1);

        // Mean moves from 2ms to (2ms * 1 + clamp(30s, 0.667ms, 6ms)) / 2 = 4ms, so 7ms / 4ms = 1.75.
        Assert.Equal(1.75f, calculator.DesiredChunksPerTick, 4);
    }

    private static async Task<float> RateAfterBatchAsync(TimeSpan elapsed, int batchSize)
    {
        var time = new ManualTimeProvider();
        var harness = new ApplierHarness(Version(776), time: time);

        await harness.ApplyAsync(new ClientboundChunkBatchStartPacket());
        time.Advance(elapsed);
        await harness.ApplyAsync(new ClientboundChunkBatchFinishedPacket(batchSize));

        return harness.Recorder.Packets.OfType<ServerboundChunkBatchReceivedPacket>().Single().DesiredChunksPerTick;
    }

    private static JavaVersion Version(int protocol)
    {
        Assert.True(Umpk.Data.Java.JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }
}
