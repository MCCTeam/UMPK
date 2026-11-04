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

public sealed class CustomModelMiningIdentitySessionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int protocol in new[] { 769, 770, 771, 772, 773, 774, 775, 776, 777 })
            foreach (string scenario in new[] { "same-four-lists", "same-explicit-empty", "changed-float", "changed-flag", "changed-string", "changed-color", "reordered-floats", "reordered-strings", "positive-versus-negative-zero", "distinct-NaN-payloads", "NaN-versus-finite", "explicit-empty-versus-absent" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FreshFramedCustomModelUsesNativeDestroyTargetIdentity(int protocol, string scenario)
    {
        void CheckSwing(IPacket packet) => AssertSwing(packet, protocol);

        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        ItemStack before = Stack(session, scenario, replacement: false);
        ItemStack after = Stack(session, scenario, replacement: true);
        NativePair? native = NativePayloads(protocol, scenario);
        await Deliver(session, before, native?.Before, native?.BeforeFloatBits);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Collection(await session.Tick(), packet => AssertAction(packet, protocol, 0), CheckSwing, CheckSwing);
            CheckSwing(Assert.Single(await session.Tick()));
            Assert.False(action.IsCompleted);

            await Deliver(session, after, native?.After, native?.AfterFloatBits);
            ItemStack replacement = session.Client.State.Inventory.GetSlot(36);
            Assert.NotSame(original, replacement);
            if (original.Components.Get(DataComponents.CustomModelData) is { } originalData
                && replacement.Components.Get(DataComponents.CustomModelData) is { } replacementData)
            {
                Assert.NotSame(originalData, replacementData);
                if (originalData.Floats.Count > 0)
                    Assert.NotSame(originalData.Floats, replacementData.Floats);
            }
            bool reset = scenario is not ("same-four-lists" or "same-explicit-empty" or "distinct-NaN-payloads");
            if (native is not null)
                Assert.Equal(!reset, native.Equal);
            List<IPacket> continued = await session.Tick();
            if (reset)
                Assert.Collection(continued, packet => AssertAction(packet, protocol, protocol == 777 ? 2 : 1), packet => AssertAction(packet, protocol, 0), CheckSwing);
            else
                CheckSwing(Assert.Single(continued));
            Assert.False(action.IsCompleted);

            int stopAfter = reset ? 6 : 3;
            for (int tick = 1; tick <= stopAfter; tick++)
            {
                List<IPacket> packets = await session.Tick();
                if (tick < stopAfter)
                {
                    CheckSwing(Assert.Single(packets));
                    Assert.False(action.IsCompleted);
                }
                else
                {
                    Assert.Collection(packets, packet => AssertAction(packet, protocol, protocol == 777 ? 3 : 2), CheckSwing);
                    Assert.False(action.IsCompleted);
                    await session.Complete(action, Assert.IsType<ServerboundPlayerActionPacket>(packets[0]));
                    Assert.True(action.IsCompletedSuccessfully);
                }
            }
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await session.Tick();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    private static ItemStack Stack(Session session, string scenario, bool replacement)
    {
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        if (replacement && scenario == "explicit-empty-versus-absent")
            return stack;
        float[] floats = [1.0F, 2.5F];
        bool[] flags = [true, false];
        string[] strings = ["alpha", "beta"];
        int[] colors = [0x112233, 0x445566];
        if (scenario is "same-explicit-empty" or "explicit-empty-versus-absent")
        {
            floats = [];
            flags = [];
            strings = [];
            colors = [];
        }
        if (replacement)
        {
            switch (scenario)
            {
                case "changed-float": floats[0] = 3.0F; break;
                case "changed-flag": flags[0] = false; break;
                case "changed-string": strings[0] = "gamma"; break;
                case "changed-color": colors[0] = 0x778899; break;
                case "reordered-floats": floats = [2.5F, 1.0F]; break;
                case "reordered-strings": strings = ["beta", "alpha"]; break;
            }
        }
        if (scenario == "positive-versus-negative-zero")
            floats = [replacement ? -0.0F : 0.0F];
        if (scenario == "distinct-NaN-payloads")
            floats = [BitConverter.Int32BitsToSingle(replacement ? 0x7FC00002 : 0x7FC00001)];
        if (scenario == "NaN-versus-finite")
            floats = [replacement ? 1.0F : BitConverter.Int32BitsToSingle(0x7FC00001)];
        return stack.With(DataComponents.CustomModelData, new CustomModelDataComponent(floats, flags, strings, colors));
    }

    private static async Task Deliver(Session session, ItemStack stack, string? originalPayload, IReadOnlyList<int>? nativeFloatBits)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "container_set_slot");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = new ClientboundContainerSetSlotPacket(0, 7, 36, stack);
        var encoded = new ArrayBufferWriter<byte>();
        var encoder = new PacketWriter(encoded);
        codec.Encode(ref encoder, packet, context);
        byte[] bytes = originalPayload is null ? encoded.WrittenSpan.ToArray() : Convert.FromBase64String(originalPayload);
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
        Assert.Equal(36, decoded.Slot);
        Assert.Equal(stack.Item.Id, decoded.Item.Item.Id);
        Assert.Equal(1, decoded.Item.Count);
        CustomModelDataComponent? expected = stack.Components.Get(DataComponents.CustomModelData);
        CustomModelDataComponent? actual = decoded.Item.Components.Get(DataComponents.CustomModelData);
        if (expected is null)
            Assert.Null(actual);
        else
        {
            Assert.NotNull(actual);
            Assert.Equal(nativeFloatBits ?? expected.Floats.Select(BitConverter.SingleToInt32Bits).ToArray(), actual.Floats.Select(BitConverter.SingleToInt32Bits).ToArray());
            Assert.Equal(expected.Flags, actual.Flags);
            Assert.Equal(expected.Strings, actual.Strings);
            Assert.Equal(expected.Colors, actual.Colors);
        }
        var roundTrip = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(roundTrip);
        codec.Encode(ref writer, decoded, context);
        Assert.Equal(bytes, roundTrip.WrittenSpan.ToArray());
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private sealed record NativePair(string Before, string After, bool Equal, int[] BeforeFloatBits, int[] AfterFloatBits);

    // Exact full packet payloads and decoded float bits from original native 769/777 methods.
    private static NativePair? NativePayloads(int protocol, string scenario)
        => (protocol, scenario) switch
        {
            (769, "same-four-lists") => new("AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", true, [1065353216, 1075838976], [1065353216, 1075838976]),
            (769, "same-explicit-empty") => new("AAcAJAHnBgEADgAAAAA=", "AAcAJAHnBgEADgAAAAA=", true, [], []),
            (769, "changed-float") => new("AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAHnBgEADgJAQAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1077936128, 1075838976]),
            (769, "changed-flag") => new("AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAHnBgEADgI/gAAAQCAAAAIAAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (769, "changed-string") => new("AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFZ2FtbWEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (769, "changed-color") => new("AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAd4iZAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (769, "reordered-floats") => new("AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAHnBgEADgJAIAAAP4AAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1075838976, 1065353216]),
            (769, "reordered-strings") => new("AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAHnBgEADgI/gAAAQCAAAAIBAAIEYmV0YQVhbHBoYQIAESIzAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (769, "positive-versus-negative-zero") => new("AAcAJAHnBgEADgEAAAAAAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", "AAcAJAHnBgEADgGAAAAAAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", false, [0], [-2147483648]),
            (769, "distinct-NaN-payloads") => new("AAcAJAHnBgEADgF/wAABAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", "AAcAJAHnBgEADgF/wAACAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", true, [2143289345], [2143289346]),
            (769, "NaN-versus-finite") => new("AAcAJAHnBgEADgF/wAABAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", "AAcAJAHnBgEADgE/gAAAAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", false, [2143289345], [1065353216]),
            (769, "explicit-empty-versus-absent") => new("AAcAJAHnBgEADgAAAAA=", "AAcAJAHnBgAA", false, [], []),
            (777, "same-four-lists") => new("AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", true, [1065353216, 1075838976], [1065353216, 1075838976]),
            (777, "same-explicit-empty") => new("AAcAJAGcCAEAEQAAAAA=", "AAcAJAGcCAEAEQAAAAA=", true, [], []),
            (777, "changed-float") => new("AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAGcCAEAEQJAQAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1077936128, 1075838976]),
            (777, "changed-flag") => new("AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAGcCAEAEQI/gAAAQCAAAAIAAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (777, "changed-string") => new("AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFZ2FtbWEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (777, "changed-color") => new("AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAd4iZAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (777, "reordered-floats") => new("AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAGcCAEAEQJAIAAAP4AAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", false, [1065353216, 1075838976], [1075838976, 1065353216]),
            (777, "reordered-strings") => new("AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIFYWxwaGEEYmV0YQIAESIzAERVZg==", "AAcAJAGcCAEAEQI/gAAAQCAAAAIBAAIEYmV0YQVhbHBoYQIAESIzAERVZg==", false, [1065353216, 1075838976], [1065353216, 1075838976]),
            (777, "positive-versus-negative-zero") => new("AAcAJAGcCAEAEQEAAAAAAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", "AAcAJAGcCAEAEQGAAAAAAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", false, [0], [-2147483648]),
            (777, "distinct-NaN-payloads") => new("AAcAJAGcCAEAEQF/wAABAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", "AAcAJAGcCAEAEQF/wAACAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", true, [2143289345], [2143289346]),
            (777, "NaN-versus-finite") => new("AAcAJAGcCAEAEQF/wAABAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", "AAcAJAGcCAEAEQE/gAAAAgEAAgVhbHBoYQRiZXRhAgARIjMARFVm", false, [2143289345], [1065353216]),
            (777, "explicit-empty-versus-absent") => new("AAcAJAGcCAEAEQAAAAA=", "AAcAJAGcCAAA", false, [], []),
            _ => null,
        };

    private static void AssertAction(IPacket packet, int protocol, int expected)
    {
        var action = Assert.IsType<ServerboundPlayerActionPacket>(packet);
        Assert.Equal(expected, action.Action);
        Assert.Equal(Session.Target, action.Position);
        Assert.Equal((byte)Direction.North, action.Direction);
        if (expected == (protocol == 777 ? 2 : 1))
            Assert.Equal(0, action.Sequence);
        else
            Assert.True(action.Sequence > 0);
    }

    private static void AssertSwing(IPacket packet, int protocol)
    {
        if (protocol == 777)
            Assert.IsType<ServerboundPunchPacket>(packet);
        else
        {
            var swing = Assert.IsType<ServerboundSwingPacket>(packet);
            Assert.True(swing.HasHand);
            Assert.Equal(0, swing.Hand);
        }
    }
}
