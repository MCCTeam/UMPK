using System.Reflection;
using Umpk.Client.Appliers;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class PacketPathAllocationTests
{
    [Fact]
    public async Task ApplyingAReusablePacketDoesNotAllocateACapabilityContainer()
    {
        Assert.True(JavaVersions.TryGetByProtocol(777, out JavaVersion version));
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.Empty, "AllocationTest"))
            .UseStaticRegistries(JavaGameData.Registries(777))
            .Build();
        var apply = typeof(UmpkClient).GetMethod("ApplyPacketAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<object, ProtocolPhase, int, int, CancellationToken, bool, ValueTask>>(client);
        var packet = new ClientboundRotateHeadPacket(123, 0);
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
            await apply(packet, ProtocolPhase.Play, 0, 0, CancellationToken.None, false);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            await apply(packet, ProtocolPhase.Play, 0, 0, CancellationToken.None, false);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated <= iterations * 100, $"Packet application allocated {allocated / iterations} bytes per packet; budget is 100 bytes.");
    }

    [Fact]
    public async Task SuspendedEventHandlersDoNotDuplicateTheCapabilityContainer()
    {
        Assert.True(JavaVersions.TryGetByProtocol(777, out JavaVersion version));
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.Empty, "AllocationTest"))
            .UseStaticRegistries(JavaGameData.Registries(777))
            .Build();
        var apply = typeof(UmpkClient).GetMethod("ApplyPacketAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<object, ProtocolPhase, int, int, CancellationToken, bool, ValueTask>>(client);
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        await apply(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, null), ProtocolPhase.Play, 0, 0, CancellationToken.None, false);
        TaskCompletionSource<bool> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int observed = 0;
        using IDisposable subscription = client.Events.Subscribe<TimeChanged>(_ =>
        {
            observed++;
            return new ValueTask(gate.Task);
        });
        var packet = new ClientboundSetTimePacket(10, 20, false, []);
        long measured = 0;
        const int iterations = 1000;
        for (int i = 0; i < iterations * 2; i++)
        {
            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            long before = GC.GetAllocatedBytesForCurrentThread();
            ValueTask pending = apply(packet, ProtocolPhase.Play, 0, 0, CancellationToken.None, false);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(pending.IsCompleted);
            if (i >= iterations)
                measured += allocated;
            gate.SetResult(true);
            await pending;
        }

        Assert.Equal(iterations * 2, observed);
        Assert.True(measured <= iterations * 880, $"Suspended packet application allocated {measured / iterations} bytes before suspension; budget is 880 bytes.");
    }

    [Fact]
    public async Task UnrelatedWorldPacketsDoNotAllocateBorderClosures()
    {
        ApplierHarness harness = await JoinedAsync();
        var context = (ApplierContext)typeof(ApplierHarness)
            .GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(harness)!;
        var applier = new WorldApplier();
        var packet = new ClientboundRotateHeadPacket(123, 0);
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
            await applier.TryApplyAsync(packet, context, CancellationToken.None);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int owned = 0;
        for (int i = 0; i < iterations; i++)
            if (await applier.TryApplyAsync(packet, context, CancellationToken.None))
                owned++;

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, owned);
        Assert.True(allocated <= iterations * 8, $"Unrelated world packets allocated {allocated / iterations} bytes per packet.");
    }

    [Fact]
    public async Task UnrelatedBrandFramesDoNotAllocateScheduledWork()
    {
        Assert.True(JavaVersions.TryGetByProtocol(777, out JavaVersion version));
        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(version)
            .UseProfile(new GameProfile(Guid.Empty, "AllocationTest"))
            .UseStaticRegistries(JavaGameData.Registries(777))
            .Build();
        Assert.NotEqual(0, new WireIndex(version).ClientboundPlay(Identifier.Minecraft("custom_payload")));
        var inspect = typeof(UmpkClient).GetMethod("TryCapturePlayBrand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<PacketObservation>>(client);
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
            inspect(default);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            inspect(default);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Null(client.State.Server.Brand);
        Assert.True(allocated <= iterations * 8, $"Unrelated brand frames allocated {allocated / iterations} bytes per frame.");
    }

    [Fact]
    public async Task BorderUpdatesPreserveOtherFieldsAndPublishTheUpdatedState()
    {
        ApplierHarness harness = await JoinedAsync();
        await harness.ApplyAsync(new ClientboundInitializeBorderPacket(2, 3, 4, 5, 6, 29_999_984, 7, 8));
        var seen = new List<(double X, double Z, double Size, double Target, long Lerp, int Blocks, double Seconds)>();
        harness.Events.Subscribe<WorldBorderChanged>(_ =>
        {
            var border = harness.State.World.Border;
            seen.Add((border.CenterX, border.CenterZ, border.Size, border.TargetSize, border.LerpTimeMillis, border.WarningBlocks, border.WarningTimeSeconds));
        });

        await harness.ApplyAsync(new ClientboundSetBorderCenterPacket(-11, 13));
        await harness.ApplyAsync(new ClientboundSetBorderSizePacket(17));
        await harness.ApplyAsync(new ClientboundSetBorderLerpSizePacket(19, 23, 29));
        await harness.ApplyAsync(new ClientboundSetBorderWarningDelayPacket(31));
        await harness.ApplyAsync(new ClientboundSetBorderWarningDistancePacket(37));

        Assert.Equal(new (double, double, double, double, long, int, double)[]
        {
            (-11, 13, 4, 5, 6, 7, 8),
            (-11, 13, 17, 17, 0, 7, 8),
            (-11, 13, 19, 23, 29, 7, 8),
            (-11, 13, 19, 23, 29, 7, 31),
            (-11, 13, 19, 23, 29, 37, 31),
        }, seen);
    }

    private static async Task<ApplierHarness> JoinedAsync()
    {
        Assert.True(JavaVersions.TryGetByProtocol(777, out JavaVersion version));
        var harness = new ApplierHarness(version);
        harness.State.Registries = JavaGameData.Registries(777);
        var spawn = new CommonPlayerSpawnInfo(0, "minecraft:overworld", 0, 0, -1, false, false, null, 0, 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(1, false, ["minecraft:overworld"], 20, 8, 8, false, true, false, spawn, false, false, null));
        return harness;
    }
}
