using System.Buffers;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class LegacyCustomModelMiningIdentitySessionTests
{
    // Exact native set-slot payloads, decoded scalar observations and original post-stream stack equality.
    public static IEnumerable<object[]> NativeCases()
    {
        yield return [766, "same-zero", "AAcAJAHHBgEADQA=", "AAcAJAHHBgEADQA=", 0, 0, false, 0, 0, true];
        yield return [766, "same-small-positive", "AAcAJAHHBgEADSo=", "AAcAJAHHBgEADSo=", 42, 42, false, 1109917696, 1109917696, true];
        yield return [766, "same-small-negative", "AAcAJAHHBgEADdb///8P", "AAcAJAHHBgEADdb///8P", -42, -42, false, -1037565952, -1037565952, true];
        yield return [766, "same-largest-exact", "AAcAJAHHBgEADf///wc=", "AAcAJAHHBgEADf///wc=", 16777215, 16777215, false, 1266679807, 1266679807, true];
        yield return [766, "same-first-rounded", "AAcAJAHHBgEADYGAgAg=", "AAcAJAHHBgEADYGAgAg=", 16777217, 16777217, false, 1266679808, 1266679808, true];
        yield return [766, "rounded-neighbors-positive", "AAcAJAHHBgEADYCAgAg=", "AAcAJAHHBgEADYGAgAg=", 16777216, 16777217, false, 1266679808, 1266679808, false];
        yield return [766, "rounded-neighbors-negative", "AAcAJAHHBgEADYCAgPgP", "AAcAJAHHBgEADf////cP", -16777216, -16777217, false, -880803840, -880803840, false];
        yield return [766, "same-max", "AAcAJAHHBgEADf////8H", "AAcAJAHHBgEADf////8H", 2147483647, 2147483647, false, 1325400064, 1325400064, true];
        yield return [766, "max-neighbors", "AAcAJAHHBgEADf7///8H", "AAcAJAHHBgEADf////8H", 2147483646, 2147483647, false, 1325400064, 1325400064, false];
        yield return [766, "same-min", "AAcAJAHHBgEADYCAgIAI", "AAcAJAHHBgEADYCAgIAI", -2147483648, -2147483648, false, -822083584, -822083584, true];
        yield return [766, "min-neighbors", "AAcAJAHHBgEADYCAgIAI", "AAcAJAHHBgEADYGAgIAI", -2147483648, -2147483647, false, -822083584, -822083584, false];
        yield return [766, "same-boundary-exact", "AAcAJAHHBgEADYCAgAg=", "AAcAJAHHBgEADYCAgAg=", 16777216, 16777216, false, 1266679808, 1266679808, true];
        yield return [766, "zero-versus-absent", "AAcAJAHHBgEADQA=", "AAcAJAHHBgAA", 0, 0, true, 0, 0, false];
        yield return [767, "same-zero", "AAcAJAHIBgEADQA=", "AAcAJAHIBgEADQA=", 0, 0, false, 0, 0, true];
        yield return [767, "same-small-positive", "AAcAJAHIBgEADSo=", "AAcAJAHIBgEADSo=", 42, 42, false, 1109917696, 1109917696, true];
        yield return [767, "same-small-negative", "AAcAJAHIBgEADdb///8P", "AAcAJAHIBgEADdb///8P", -42, -42, false, -1037565952, -1037565952, true];
        yield return [767, "same-largest-exact", "AAcAJAHIBgEADf///wc=", "AAcAJAHIBgEADf///wc=", 16777215, 16777215, false, 1266679807, 1266679807, true];
        yield return [767, "same-first-rounded", "AAcAJAHIBgEADYGAgAg=", "AAcAJAHIBgEADYGAgAg=", 16777217, 16777217, false, 1266679808, 1266679808, true];
        yield return [767, "rounded-neighbors-positive", "AAcAJAHIBgEADYCAgAg=", "AAcAJAHIBgEADYGAgAg=", 16777216, 16777217, false, 1266679808, 1266679808, false];
        yield return [767, "rounded-neighbors-negative", "AAcAJAHIBgEADYCAgPgP", "AAcAJAHIBgEADf////cP", -16777216, -16777217, false, -880803840, -880803840, false];
        yield return [767, "same-max", "AAcAJAHIBgEADf////8H", "AAcAJAHIBgEADf////8H", 2147483647, 2147483647, false, 1325400064, 1325400064, true];
        yield return [767, "max-neighbors", "AAcAJAHIBgEADf7///8H", "AAcAJAHIBgEADf////8H", 2147483646, 2147483647, false, 1325400064, 1325400064, false];
        yield return [767, "same-min", "AAcAJAHIBgEADYCAgIAI", "AAcAJAHIBgEADYCAgIAI", -2147483648, -2147483648, false, -822083584, -822083584, true];
        yield return [767, "min-neighbors", "AAcAJAHIBgEADYCAgIAI", "AAcAJAHIBgEADYGAgIAI", -2147483648, -2147483647, false, -822083584, -822083584, false];
        yield return [767, "same-boundary-exact", "AAcAJAHIBgEADYCAgAg=", "AAcAJAHIBgEADYCAgAg=", 16777216, 16777216, false, 1266679808, 1266679808, true];
        yield return [767, "zero-versus-absent", "AAcAJAHIBgEADQA=", "AAcAJAHIBgAA", 0, 0, true, 0, 0, false];
        yield return [768, "same-zero", "AAcAJAHeBgEADgA=", "AAcAJAHeBgEADgA=", 0, 0, false, 0, 0, true];
        yield return [768, "same-small-positive", "AAcAJAHeBgEADio=", "AAcAJAHeBgEADio=", 42, 42, false, 1109917696, 1109917696, true];
        yield return [768, "same-small-negative", "AAcAJAHeBgEADtb///8P", "AAcAJAHeBgEADtb///8P", -42, -42, false, -1037565952, -1037565952, true];
        yield return [768, "same-largest-exact", "AAcAJAHeBgEADv///wc=", "AAcAJAHeBgEADv///wc=", 16777215, 16777215, false, 1266679807, 1266679807, true];
        yield return [768, "same-first-rounded", "AAcAJAHeBgEADoGAgAg=", "AAcAJAHeBgEADoGAgAg=", 16777217, 16777217, false, 1266679808, 1266679808, true];
        yield return [768, "rounded-neighbors-positive", "AAcAJAHeBgEADoCAgAg=", "AAcAJAHeBgEADoGAgAg=", 16777216, 16777217, false, 1266679808, 1266679808, false];
        yield return [768, "rounded-neighbors-negative", "AAcAJAHeBgEADoCAgPgP", "AAcAJAHeBgEADv////cP", -16777216, -16777217, false, -880803840, -880803840, false];
        yield return [768, "same-max", "AAcAJAHeBgEADv////8H", "AAcAJAHeBgEADv////8H", 2147483647, 2147483647, false, 1325400064, 1325400064, true];
        yield return [768, "max-neighbors", "AAcAJAHeBgEADv7///8H", "AAcAJAHeBgEADv////8H", 2147483646, 2147483647, false, 1325400064, 1325400064, false];
        yield return [768, "same-min", "AAcAJAHeBgEADoCAgIAI", "AAcAJAHeBgEADoCAgIAI", -2147483648, -2147483648, false, -822083584, -822083584, true];
        yield return [768, "min-neighbors", "AAcAJAHeBgEADoCAgIAI", "AAcAJAHeBgEADoGAgIAI", -2147483648, -2147483647, false, -822083584, -822083584, false];
        yield return [768, "same-boundary-exact", "AAcAJAHeBgEADoCAgAg=", "AAcAJAHeBgEADoCAgAg=", 16777216, 16777216, false, 1266679808, 1266679808, true];
        yield return [768, "zero-versus-absent", "AAcAJAHeBgEADgA=", "AAcAJAHeBgAA", 0, 0, true, 0, 0, false];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public async Task FreshNativeIntegerFramesControlHeldMining(int protocol, string scenario, string before, string after, int beforeInteger, int afterInteger, bool afterAbsent, int beforeBits, int afterBits, bool nativeEqual)
    {
        Assert.NotEmpty(scenario);
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        await Deliver(session, before, beforeInteger, beforeBits, absent: false);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await Start(session, action);
            await Deliver(session, after, afterInteger, afterBits, afterAbsent);
            Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36));
            List<IPacket> continued = await session.Tick();
            if (nativeEqual)
                AssertSwing(Assert.Single(continued));
            else
                Assert.Collection(continued, packet => AssertAction(packet, 1), packet => AssertAction(packet, 0), AssertSwing);
            await Finish(session, action, reset: !nativeEqual);
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await Stop(session, caller, action);
        }
    }

    public static IEnumerable<object[]> ProjectionCases()
    {
        foreach (int protocol in new[] { 766, 767, 768 })
            foreach (string scenario in new[] { "record-copy", "changed-small", "changed-ambiguous", "copied-list", "unknown-equal-records" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(ProjectionCases))]
    public async Task ModeledProjectionChangesDoNotInventTheLostNativeInteger(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        object[] row = Assert.Single(NativeCases(), value => (int)value[0] == protocol && (string)value[1] == "same-first-rounded");
        string payload = (string)row[2];
        await Deliver(session, payload, 16_777_217, 1266679808, absent: false);
        ItemStack native = session.Client.State.Inventory.GetSlot(36);
        CustomModelDataComponent received = Assert.IsType<CustomModelDataComponent>(native.Components.Get(DataComponents.CustomModelData));
        if (scenario == "unknown-equal-records")
        {
            CustomModelDataComponent unknown = received with { Floats = received.Floats.ToArray() };
            await SetModeled(session, native.With(DataComponents.CustomModelData, unknown));
        }
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await Start(session, action);
            CustomModelDataComponent changed;
            if (scenario == "unknown-equal-records")
            {
                var current = Assert.IsType<CustomModelDataComponent>(original.Components.Get(DataComponents.CustomModelData));
                changed = current with { };
                Assert.Same(current.Floats, changed.Floats);
                Assert.NotSame(current, changed);
            }
            else
            {
                // A separately received component keeps the destroy-target snapshot unchanged while the current public projection is edited.
                ClientboundContainerSetSlotPacket fresh = Decode(session, payload);
                changed = Assert.IsType<CustomModelDataComponent>(fresh.Item.Components.Get(DataComponents.CustomModelData));
                switch (scenario)
                {
                    case "record-copy": changed = changed with { }; break;
                    case "changed-small": Assert.IsType<float[]>(changed.Floats)[0] = 42; break;
                    case "changed-ambiguous": Assert.IsType<float[]>(changed.Floats)[0] = -16_777_216; break;
                    case "copied-list": changed = changed with { Floats = changed.Floats.ToArray() }; break;
                }
            }
            ItemStack replacement = original.With(DataComponents.CustomModelData, changed);
            await SetModeled(session, replacement);
            Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36));
            bool unknownResult = scenario is "changed-ambiguous" or "copied-list" or "unknown-equal-records";
            List<IPacket> packets = await session.Tick();
            if (unknownResult)
            {
                var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(packets));
                Assert.Equal(1, abort.Action);
                Assert.Equal(Session.Target, abort.Position);
                Assert.Equal((byte)Direction.Down, abort.Direction);
                Assert.Equal(0, abort.Sequence);
                InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
                Assert.Contains("native component identity is unavailable", error.Message, StringComparison.Ordinal);
                Assert.Empty(await session.Tick());
                Assert.Empty(await session.Tick());
            }
            else
            {
                bool reset = scenario == "changed-small";
                if (reset)
                    Assert.Collection(packets, packet => AssertAction(packet, 1), packet => AssertAction(packet, 0), AssertSwing);
                else
                    AssertSwing(Assert.Single(packets));
                await Finish(session, action, reset);
            }
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await Stop(session, caller, action);
        }
    }

    private static async Task Start(Session session, Task action)
    {
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Collection(await session.Tick(), packet => AssertAction(packet, 0), AssertSwing, AssertSwing);
        AssertSwing(Assert.Single(await session.Tick()));
        Assert.False(action.IsCompleted);
    }

    private static async Task Finish(Session session, Task action, bool reset)
    {
        int remaining = reset ? 6 : 3;
        for (int tick = 1; tick <= remaining; tick++)
        {
            List<IPacket> packets = await session.Tick();
            if (tick < remaining)
            {
                AssertSwing(Assert.Single(packets));
                Assert.False(action.IsCompleted);
            }
            else
            {
                Assert.Collection(packets, packet => AssertAction(packet, 2), AssertSwing);
                Assert.False(action.IsCompleted);
                await session.Complete(action, Assert.IsType<ServerboundPlayerActionPacket>(packets[0]));
                Assert.True(action.IsCompletedSuccessfully);
            }
        }
    }

    private static async Task Stop(Session session, CancellationTokenSource caller, Task action)
    {
        await caller.CancelAsync();
        if (!action.IsCompleted)
            await session.Tick();
        try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private static ClientboundContainerSetSlotPacket Decode(Session session, string original)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "container_set_slot");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        return Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(Convert.FromBase64String(original), context));
    }

    private static async Task Deliver(Session session, string payload, int integer, int bits, bool absent)
    {
        ClientboundContainerSetSlotPacket decoded = Decode(session, payload);
        Assert.Equal(0, decoded.ContainerId);
        Assert.Equal(7, decoded.StateId);
        Assert.Equal(36, decoded.Slot);
        Assert.Equal(Identifier.Minecraft("diamond_pickaxe"), decoded.Item.Item.Id);
        Assert.Equal(1, decoded.Item.Count);
        if (absent)
            Assert.Null(decoded.Item.Components.Get(DataComponents.CustomModelData));
        else
        {
            var data = Assert.IsType<CustomModelDataComponent>(decoded.Item.Components.Get(DataComponents.CustomModelData));
            Assert.Equal(bits, BitConverter.SingleToInt32Bits(Assert.Single(data.Floats)));
            Assert.Empty(data.Flags);
            Assert.Empty(data.Strings);
            Assert.Empty(data.Colors);
            Assert.Equal(bits, BitConverter.SingleToInt32Bits((float)integer));
        }
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "container_set_slot");
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, decoded, new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty));
        Assert.Equal(Convert.FromBase64String(payload), bytes.WrittenSpan.ToArray());
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private static async Task SetModeled(Session session, ItemStack stack)
    {
        await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, 36, stack); return true; }, session.Token);
        Assert.Empty(await session.Barrier());
    }

    private static void AssertAction(IPacket packet, int expected)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(packet);
        Assert.Equal(expected, action.Action);
        Assert.Equal(Session.Target, action.Position);
        Assert.Equal((byte)Direction.North, action.Direction);
        if (expected == 1)
            Assert.Equal(0, action.Sequence);
        else
            Assert.True(action.Sequence > 0);
    }

    private static void AssertSwing(IPacket packet)
    {
        var swing = Assert.IsType<ServerboundSwingPacket>(packet);
        Assert.True(swing.HasHand);
        Assert.Equal(0, swing.Hand);
    }
}
