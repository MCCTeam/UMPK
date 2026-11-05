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

public sealed class TooltipDisplayMiningIdentitySessionTests
{
    private static readonly int[] Protocols = [770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly string[] Scenarios = ["same-single", "same-two", "reordered-set", "duplicate-input", "changed-hide", "changed-member", "subset", "empty-versus-default", "default-versus-removed", "both-removed", "true-empty-versus-default", "shared-live-mutation"];

    public static IEnumerable<object[]> NativeCases()
    {
        foreach (int protocol in Protocols)
            foreach (string scenario in Scenarios)
                yield return [protocol, scenario];
    }

    public static IEnumerable<object[]> MutableCases()
    {
        foreach (int protocol in Protocols)
            foreach (string scenario in new[] { "same-stack-shared-valid", "fresh-record-shared-valid", "fresh-array-equal", "fresh-array-changed" })
                yield return [protocol, scenario];
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (int protocol in Protocols)
            foreach (string scenario in new[] { "same-stack-invalid", "fresh-array-invalid", "fresh-record-shared-invalid" })
                yield return [protocol, scenario];
    }

    public static IEnumerable<object[]> SuppliedDefinitionCases()
    {
        foreach (int protocol in new[] { 770, 777 })
            foreach (string scenario in new[] { "clone-omitted-versus-empty", "override-omitted-versus-removal", "both-omitted", "explicit-base-equal", "explicit-base-changed" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(SuppliedDefinitionCases))]
    public async Task UnprovenPrototypeDefaultsRemainUnknownWithoutRejectingKnownValues(int protocol, string scenario)
    {
        await using Session session = await Create(protocol);
        RegistryEntry<ItemDefinition> canonical = session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")];
        ItemDefinition definition = canonical.Value with { };
        if (scenario == "override-omitted-versus-removal")
            definition = definition with { MaxStackSize = canonical.Value.MaxStackSize + 1 };
        if (scenario.StartsWith("explicit-base-", StringComparison.Ordinal))
        {
            var defaults = canonical.Value.DefaultComponents.ToDictionary(pair => pair.Key, pair => pair.Value);
            defaults[DataComponents.TooltipDisplay] = new TooltipDisplayComponent(true, [SelectedIds(protocol).Lore]);
            definition = definition with { DefaultComponents = defaults };
        }
        var builder = new RegistryBuilder<ItemDefinition>(RegistryIds.Item);
        builder.Add(canonical.NetworkId, canonical.Id, definition);
        RegistryEntry<ItemDefinition> supplied = builder.Build()[canonical.Id];
        Assert.Equal(canonical.NetworkId, supplied.NetworkId);
        Assert.Equal(canonical.Id, supplied.Id);
        Assert.NotSame(canonical.Value, supplied.Value);
        ItemStack original = new(supplied, 1);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, 36, original); return true; }, session.Token);
        Assert.Empty(await session.Barrier());
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await Start(session, action);
            ItemStack replacement = scenario switch
            {
                "clone-omitted-versus-empty" => original.With(DataComponents.TooltipDisplay, new TooltipDisplayComponent(false, [])),
                "both-omitted" => new ItemStack(supplied, 1),
                "explicit-base-equal" => original.With(DataComponents.TooltipDisplay, new TooltipDisplayComponent(true, [SelectedIds(protocol).Lore])),
                "explicit-base-changed" => original.With(DataComponents.TooltipDisplay, new TooltipDisplayComponent(false, [3])),
                _ => WithRemoval(original),
            };
            await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, 36, replacement); return true; }, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36));
            if (scenario is "clone-omitted-versus-empty" or "override-omitted-versus-removal")
            {
                var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(await session.Tick()));
                Assert.Equal(protocol == 777 ? 2 : 1, abort.Action);
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
                bool reset = scenario == "explicit-base-changed";
                await Continue(session, action, reset);
                await Finish(session, action, reset);
            }
            Assert.Same(replacement, session.Client.State.Inventory.GetSlot(36));
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally { await Stop(session, caller, action); }
    }

    public static IEnumerable<object[]> SuppliedValueCases()
    {
        foreach (int protocol in new[] { 770, 777 })
            foreach (string scenario in new[] { "duplicate-array", "canonical-explicit-empty" })
                yield return [protocol, scenario];
    }

    [Theory]
    [MemberData(nameof(SuppliedValueCases))]
    public async Task SuppliedDuplicateAndCanonicalEmptyValuesKeepNativeMiningIdentity(int protocol, string scenario)
    {
        await using Session session = await Create(protocol);
        await Deliver(session, Native(protocol, scenario == "duplicate-array" ? "same-two" : "empty-versus-default").Before);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await Start(session, action);
            TooltipDisplayComponent supplied = scenario == "duplicate-array"
                ? new TooltipDisplayComponent(false, [3, SelectedIds(protocol).Lore, 3])
                : new TooltipDisplayComponent(false, []);
            if (scenario == "duplicate-array")
            {
                Assert.Equal(2, Assert.IsType<TooltipDisplayComponent>(original.Components.Get(DataComponents.TooltipDisplay)).HiddenComponents.Count);
                Assert.Equal(3, supplied.HiddenComponents.Count);
            }
            else
                Assert.Null(original.Components.Get(DataComponents.TooltipDisplay));
            ItemStack replacement = original.With(DataComponents.TooltipDisplay, supplied);
            await session.Client.InvokeAsync(client => { client.State.Inventory.SetSlot(0, 36, replacement); return true; }, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.Same(supplied, session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.TooltipDisplay));
            Assert.Contains(replacement.Components.Patch, entry => !entry.IsRemoval && entry.Type.Equals(DataComponents.TooltipDisplay));
            await Continue(session, action, reset: false);
            await Finish(session, action, reset: false);
            Assert.Same(replacement, session.Client.State.Inventory.GetSlot(36));
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally { await Stop(session, caller, action); }
    }

    private static ItemStack WithRemoval(ItemStack stack)
    {
        var prototype = stack.Item.Value.DefaultComponents.ToDictionary(pair => pair.Key, pair => pair.Value);
        prototype[DataComponents.TooltipDisplay] = new TooltipDisplayComponent(false, []);
        return stack.WithComponents(DataComponentMap.Create(prototype, [DataComponentEntry.Remove(DataComponents.TooltipDisplay)]));
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public async Task FreshFramedTooltipUsesOriginalSetAndDefaultIdentity(int protocol, string scenario)
    {
        await using Session session = await Create(protocol);
        NativePair native = Native(protocol, scenario);
        await Deliver(session, native.Before);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await Start(session, action);
            await Deliver(session, native.After);
            ItemStack replacement = session.Client.State.Inventory.GetSlot(36);
            Assert.NotSame(original, replacement);
            if (original.Components.Get(DataComponents.TooltipDisplay) is { } display
                && replacement.Components.Get(DataComponents.TooltipDisplay) is { } other)
            {
                Assert.NotSame(display, other);
                Assert.NotSame(display.HiddenComponents, other.HiddenComponents);
            }
            bool reset = !native.PostStreamEqual;
            await Continue(session, action, reset);
            await Finish(session, action, reset);
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally { await Stop(session, caller, action); }
    }

    [Theory]
    [MemberData(nameof(MutableCases))]
    public async Task ValidMutableValuesPreserveNativeSharingAndFreshValueChanges(int protocol, string scenario)
    {
        await using Session session = await Create(protocol);
        await Deliver(session, Native(protocol, "same-single").Before);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        TooltipDisplayComponent display = Assert.IsType<TooltipDisplayComponent>(original.Components.Get(DataComponents.TooltipDisplay));
        int[] hidden = Assert.IsType<int[]>(display.HiddenComponents);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await Start(session, action);
            await session.Client.InvokeAsync(client =>
            {
                if (scenario is "same-stack-shared-valid" or "fresh-record-shared-valid")
                    hidden[0] = SelectedIds(protocol).Lore;
                TooltipDisplayComponent changed = scenario switch
                {
                    "fresh-record-shared-valid" => display with { },
                    "fresh-array-equal" => display with { HiddenComponents = hidden.ToArray() },
                    "fresh-array-changed" => display with { HiddenComponents = [SelectedIds(protocol).Lore] },
                    _ => display,
                };
                if (scenario != "same-stack-shared-valid")
                    client.State.Inventory.SetSlot(0, 36, original.With(DataComponents.TooltipDisplay, changed));
                return true;
            }, session.Token);
            Assert.Empty(await session.Barrier());
            if (scenario == "same-stack-shared-valid")
                Assert.Same(original, session.Client.State.Inventory.GetSlot(36));
            else
                Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36));
            bool reset = scenario == "fresh-array-changed";
            await Continue(session, action, reset);
            await Finish(session, action, reset);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally { await Stop(session, caller, action); }
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task UndeclaredIdsRemainUnavailableBeforeReferenceShortcuts(int protocol, string scenario)
    {
        await using Session session = await Create(protocol);
        await Deliver(session, Native(protocol, "same-single").Before);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        TooltipDisplayComponent display = Assert.IsType<TooltipDisplayComponent>(original.Components.Get(DataComponents.TooltipDisplay));
        int[] hidden = Assert.IsType<int[]>(display.HiddenComponents);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await Start(session, action);
            await session.Client.InvokeAsync(client =>
            {
                if (scenario is "same-stack-invalid" or "fresh-record-shared-invalid")
                    hidden[0] = -1;
                if (scenario != "same-stack-invalid")
                {
                    TooltipDisplayComponent changed = scenario == "fresh-array-invalid"
                        ? display with { HiddenComponents = [SelectedIds(protocol).Count] }
                        : display with { };
                    client.State.Inventory.SetSlot(0, 36, original.With(DataComponents.TooltipDisplay, changed));
                }
                return true;
            }, session.Token);
            Assert.Empty(await session.Barrier());
            if (scenario == "same-stack-invalid")
                Assert.Same(original, session.Client.State.Inventory.GetSlot(36));
            else
                Assert.NotSame(original, session.Client.State.Inventory.GetSlot(36));
            var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(await session.Tick()));
            Assert.Equal(protocol == 777 ? 2 : 1, abort.Action);
            Assert.Equal(Session.Target, abort.Position);
            Assert.Equal((byte)Direction.Down, abort.Direction);
            Assert.Equal(0, abort.Sequence);
            InvalidOperationException error = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await action.WaitAsync(session.Token));
            Assert.Contains("native component identity is unavailable", error.Message, StringComparison.Ordinal);
            Assert.Empty(await session.Tick());
            Assert.Empty(await session.Tick());
            Assert.Equal(targetState, session.Client.State.World.GetBlock(Session.Target).StateId);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
        finally { await Stop(session, caller, action); }
    }

    private static async Task<Session> Create(int protocol)
    {
        Session session = await Session.Create(protocol);
        try
        {
            await session.Mode(GameMode.Survival, instantBuild: false);
            await session.Info(GameMode.Survival);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }

    private static async Task Deliver(Session session, Snapshot snapshot)
    {
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        if (snapshot.Patch == "value")
            stack = stack.With(DataComponents.TooltipDisplay, new TooltipDisplayComponent(snapshot.Hide, snapshot.Hidden));
        else if (snapshot.Patch == "removal")
        {
            var prototype = stack.Item.Value.DefaultComponents.ToDictionary(pair => pair.Key, pair => pair.Value);
            prototype[DataComponents.TooltipDisplay] = new TooltipDisplayComponent(false, []);
            stack = stack.WithComponents(DataComponentMap.Create(prototype, [DataComponentEntry.Remove(DataComponents.TooltipDisplay)]));
        }
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "container_set_slot");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, new ClientboundContainerSetSlotPacket(0, 7, 36, stack), context);
        byte[] bytes = snapshot.NativePayload is null ? output.WrittenSpan.ToArray() : Convert.FromBase64String(snapshot.NativePayload);
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
        Assert.Equal(0, decoded.ContainerId);
        Assert.Equal(7, decoded.StateId);
        Assert.Equal(36, decoded.Slot);
        Assert.Equal(stack.Item.Id, decoded.Item.Item.Id);
        Assert.Equal(1, decoded.Item.Count);
        Assert.Equal(snapshot.Patch == "removal", decoded.Item.Components.Patch.Any(entry => entry.IsRemoval && entry.Type.Equals(DataComponents.TooltipDisplay)));
        if (snapshot.Patch == "value")
        {
            TooltipDisplayComponent display = Assert.IsType<TooltipDisplayComponent>(decoded.Item.Components.Get(DataComponents.TooltipDisplay));
            Assert.Equal(snapshot.Hide, display.HideTooltip);
            Assert.Equal(snapshot.Hidden, display.HiddenComponents);
        }
        else
            Assert.Null(decoded.Item.Components.Get(DataComponents.TooltipDisplay));
        var roundTrip = new ArrayBufferWriter<byte>();
        var secondWriter = new PacketWriter(roundTrip);
        codec.Encode(ref secondWriter, decoded, context);
        Assert.Equal(bytes, roundTrip.WrittenSpan.ToArray());
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    private static async Task Start(Session session, Task action)
    {
        await session.Client.InvokeAsync(_ => true, session.Token);
        Assert.Collection(await session.Tick(), packet => AssertAction(packet, session.Protocol, 0), packet => AssertSwing(packet, session.Protocol), packet => AssertSwing(packet, session.Protocol));
        AssertSwing(Assert.Single(await session.Tick()), session.Protocol);
        Assert.False(action.IsCompleted);
    }

    private static async Task Continue(Session session, Task action, bool reset)
    {
        List<IPacket> packets = await session.Tick();
        if (reset)
            Assert.Collection(packets, packet => AssertAction(packet, session.Protocol, session.Protocol == 777 ? 2 : 1), packet => AssertAction(packet, session.Protocol, 0), packet => AssertSwing(packet, session.Protocol));
        else
            AssertSwing(Assert.Single(packets), session.Protocol);
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
                AssertSwing(Assert.Single(packets), session.Protocol);
                Assert.False(action.IsCompleted);
            }
            else
            {
                Assert.Collection(packets, packet => AssertAction(packet, session.Protocol, session.Protocol == 777 ? 3 : 2), packet => AssertSwing(packet, session.Protocol));
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

    private sealed record Snapshot(string Patch, bool Hide, int[] Hidden, string? NativePayload);
    private sealed record NativePair(Snapshot Before, Snapshot After, bool PostStreamEqual);

    // NATIVE-VECTORS-BEGIN
    private static NativePair Native(int protocol, string scenario)
        => (protocol, scenario) switch
        {
            (770, "same-single") => new(new("value", false, [3], "AAcAJAHwBgEADwABAw=="), new("value", false, [3], "AAcAJAHwBgEADwABAw=="), true),
            (770, "same-two") => new(new("value", false, [3, 8], "AAcAJAHwBgEADwACAwg="), new("value", false, [3, 8], "AAcAJAHwBgEADwACAwg="), true),
            (770, "reordered-set") => new(new("value", false, [3, 8], "AAcAJAHwBgEADwACAwg="), new("value", false, [8, 3], "AAcAJAHwBgEADwACCAM="), true),
            (770, "duplicate-input") => new(new("value", false, [3, 8], "AAcAJAHwBgEADwACAwg="), new("value", false, [3, 8], "AAcAJAHwBgEADwACAwg="), true),
            (770, "changed-hide") => new(new("value", false, [3], "AAcAJAHwBgEADwABAw=="), new("value", true, [3], "AAcAJAHwBgEADwEBAw=="), false),
            (770, "changed-member") => new(new("value", false, [3], "AAcAJAHwBgEADwABAw=="), new("value", false, [8], "AAcAJAHwBgEADwABCA=="), false),
            (770, "subset") => new(new("value", false, [3, 8], "AAcAJAHwBgEADwACAwg="), new("value", false, [3], "AAcAJAHwBgEADwABAw=="), false),
            (770, "empty-versus-default") => new(new("none", false, [], "AAcAJAHwBgAA"), new("none", false, [], "AAcAJAHwBgAA"), true),
            (770, "default-versus-removed") => new(new("none", false, [], "AAcAJAHwBgAA"), new("removal", false, [], "AAcAJAHwBgABDw=="), false),
            (770, "both-removed") => new(new("removal", false, [], "AAcAJAHwBgABDw=="), new("removal", false, [], "AAcAJAHwBgABDw=="), true),
            (770, "true-empty-versus-default") => new(new("value", true, [], "AAcAJAHwBgEADwEA"), new("none", false, [], "AAcAJAHwBgAA"), false),
            (770, "shared-live-mutation") => new(new("value", false, [3], "AAcAJAHwBgEADwABAw=="), new("value", false, [3, 8], "AAcAJAHwBgEADwACAwg="), false),
            (771, "same-single") => new(new("value", false, [3], null), new("value", false, [3], null), true),
            (771, "same-two") => new(new("value", false, [3, 8], null), new("value", false, [3, 8], null), true),
            (771, "reordered-set") => new(new("value", false, [3, 8], null), new("value", false, [8, 3], null), true),
            (771, "duplicate-input") => new(new("value", false, [3, 8], null), new("value", false, [3, 8], null), true),
            (771, "changed-hide") => new(new("value", false, [3], null), new("value", true, [3], null), false),
            (771, "changed-member") => new(new("value", false, [3], null), new("value", false, [8], null), false),
            (771, "subset") => new(new("value", false, [3, 8], null), new("value", false, [3], null), false),
            (771, "empty-versus-default") => new(new("none", false, [], null), new("none", false, [], null), true),
            (771, "default-versus-removed") => new(new("none", false, [], null), new("removal", false, [], null), false),
            (771, "both-removed") => new(new("removal", false, [], null), new("removal", false, [], null), true),
            (771, "true-empty-versus-default") => new(new("value", true, [], null), new("none", false, [], null), false),
            (771, "shared-live-mutation") => new(new("value", false, [3], null), new("value", false, [3, 8], null), false),
            (772, "same-single") => new(new("value", false, [3], null), new("value", false, [3], null), true),
            (772, "same-two") => new(new("value", false, [3, 8], null), new("value", false, [3, 8], null), true),
            (772, "reordered-set") => new(new("value", false, [3, 8], null), new("value", false, [8, 3], null), true),
            (772, "duplicate-input") => new(new("value", false, [3, 8], null), new("value", false, [3, 8], null), true),
            (772, "changed-hide") => new(new("value", false, [3], null), new("value", true, [3], null), false),
            (772, "changed-member") => new(new("value", false, [3], null), new("value", false, [8], null), false),
            (772, "subset") => new(new("value", false, [3, 8], null), new("value", false, [3], null), false),
            (772, "empty-versus-default") => new(new("none", false, [], null), new("none", false, [], null), true),
            (772, "default-versus-removed") => new(new("none", false, [], null), new("removal", false, [], null), false),
            (772, "both-removed") => new(new("removal", false, [], null), new("removal", false, [], null), true),
            (772, "true-empty-versus-default") => new(new("value", true, [], null), new("none", false, [], null), false),
            (772, "shared-live-mutation") => new(new("value", false, [3], null), new("value", false, [3, 8], null), false),
            (773, "same-single") => new(new("value", false, [3], null), new("value", false, [3], null), true),
            (773, "same-two") => new(new("value", false, [3, 8], null), new("value", false, [3, 8], null), true),
            (773, "reordered-set") => new(new("value", false, [3, 8], null), new("value", false, [8, 3], null), true),
            (773, "duplicate-input") => new(new("value", false, [3, 8], null), new("value", false, [3, 8], null), true),
            (773, "changed-hide") => new(new("value", false, [3], null), new("value", true, [3], null), false),
            (773, "changed-member") => new(new("value", false, [3], null), new("value", false, [8], null), false),
            (773, "subset") => new(new("value", false, [3, 8], null), new("value", false, [3], null), false),
            (773, "empty-versus-default") => new(new("none", false, [], null), new("none", false, [], null), true),
            (773, "default-versus-removed") => new(new("none", false, [], null), new("removal", false, [], null), false),
            (773, "both-removed") => new(new("removal", false, [], null), new("removal", false, [], null), true),
            (773, "true-empty-versus-default") => new(new("value", true, [], null), new("none", false, [], null), false),
            (773, "shared-live-mutation") => new(new("value", false, [3], null), new("value", false, [3, 8], null), false),
            (774, "same-single") => new(new("value", false, [3], null), new("value", false, [3], null), true),
            (774, "same-two") => new(new("value", false, [3, 11], null), new("value", false, [3, 11], null), true),
            (774, "reordered-set") => new(new("value", false, [3, 11], null), new("value", false, [11, 3], null), true),
            (774, "duplicate-input") => new(new("value", false, [3, 11], null), new("value", false, [3, 11], null), true),
            (774, "changed-hide") => new(new("value", false, [3], null), new("value", true, [3], null), false),
            (774, "changed-member") => new(new("value", false, [3], null), new("value", false, [11], null), false),
            (774, "subset") => new(new("value", false, [3, 11], null), new("value", false, [3], null), false),
            (774, "empty-versus-default") => new(new("none", false, [], null), new("none", false, [], null), true),
            (774, "default-versus-removed") => new(new("none", false, [], null), new("removal", false, [], null), false),
            (774, "both-removed") => new(new("removal", false, [], null), new("removal", false, [], null), true),
            (774, "true-empty-versus-default") => new(new("value", true, [], null), new("none", false, [], null), false),
            (774, "shared-live-mutation") => new(new("value", false, [3], null), new("value", false, [3, 11], null), false),
            (775, "same-single") => new(new("value", false, [3], null), new("value", false, [3], null), true),
            (775, "same-two") => new(new("value", false, [3, 11], null), new("value", false, [3, 11], null), true),
            (775, "reordered-set") => new(new("value", false, [3, 11], null), new("value", false, [11, 3], null), true),
            (775, "duplicate-input") => new(new("value", false, [3, 11], null), new("value", false, [3, 11], null), true),
            (775, "changed-hide") => new(new("value", false, [3], null), new("value", true, [3], null), false),
            (775, "changed-member") => new(new("value", false, [3], null), new("value", false, [11], null), false),
            (775, "subset") => new(new("value", false, [3, 11], null), new("value", false, [3], null), false),
            (775, "empty-versus-default") => new(new("none", false, [], null), new("none", false, [], null), true),
            (775, "default-versus-removed") => new(new("none", false, [], null), new("removal", false, [], null), false),
            (775, "both-removed") => new(new("removal", false, [], null), new("removal", false, [], null), true),
            (775, "true-empty-versus-default") => new(new("value", true, [], null), new("none", false, [], null), false),
            (775, "shared-live-mutation") => new(new("value", false, [3], null), new("value", false, [3, 11], null), false),
            (776, "same-single") => new(new("value", false, [3], null), new("value", false, [3], null), true),
            (776, "same-two") => new(new("value", false, [3, 11], null), new("value", false, [3, 11], null), true),
            (776, "reordered-set") => new(new("value", false, [3, 11], null), new("value", false, [11, 3], null), true),
            (776, "duplicate-input") => new(new("value", false, [3, 11], null), new("value", false, [3, 11], null), true),
            (776, "changed-hide") => new(new("value", false, [3], null), new("value", true, [3], null), false),
            (776, "changed-member") => new(new("value", false, [3], null), new("value", false, [11], null), false),
            (776, "subset") => new(new("value", false, [3, 11], null), new("value", false, [3], null), false),
            (776, "empty-versus-default") => new(new("none", false, [], null), new("none", false, [], null), true),
            (776, "default-versus-removed") => new(new("none", false, [], null), new("removal", false, [], null), false),
            (776, "both-removed") => new(new("removal", false, [], null), new("removal", false, [], null), true),
            (776, "true-empty-versus-default") => new(new("value", true, [], null), new("none", false, [], null), false),
            (776, "shared-live-mutation") => new(new("value", false, [3], null), new("value", false, [3, 11], null), false),
            (777, "same-single") => new(new("value", false, [3], "AAcAJAGcCAEAEgABAw=="), new("value", false, [3], "AAcAJAGcCAEAEgABAw=="), true),
            (777, "same-two") => new(new("value", false, [3, 11], "AAcAJAGcCAEAEgACAws="), new("value", false, [3, 11], "AAcAJAGcCAEAEgACAws="), true),
            (777, "reordered-set") => new(new("value", false, [3, 11], "AAcAJAGcCAEAEgACAws="), new("value", false, [11, 3], "AAcAJAGcCAEAEgACCwM="), true),
            (777, "duplicate-input") => new(new("value", false, [3, 11], "AAcAJAGcCAEAEgACAws="), new("value", false, [3, 11], "AAcAJAGcCAEAEgACAws="), true),
            (777, "changed-hide") => new(new("value", false, [3], "AAcAJAGcCAEAEgABAw=="), new("value", true, [3], "AAcAJAGcCAEAEgEBAw=="), false),
            (777, "changed-member") => new(new("value", false, [3], "AAcAJAGcCAEAEgABAw=="), new("value", false, [11], "AAcAJAGcCAEAEgABCw=="), false),
            (777, "subset") => new(new("value", false, [3, 11], "AAcAJAGcCAEAEgACAws="), new("value", false, [3], "AAcAJAGcCAEAEgABAw=="), false),
            (777, "empty-versus-default") => new(new("none", false, [], "AAcAJAGcCAAA"), new("none", false, [], "AAcAJAGcCAAA"), true),
            (777, "default-versus-removed") => new(new("none", false, [], "AAcAJAGcCAAA"), new("removal", false, [], "AAcAJAGcCAABEg=="), false),
            (777, "both-removed") => new(new("removal", false, [], "AAcAJAGcCAABEg=="), new("removal", false, [], "AAcAJAGcCAABEg=="), true),
            (777, "true-empty-versus-default") => new(new("value", true, [], "AAcAJAGcCAEAEgEA"), new("none", false, [], "AAcAJAGcCAAA"), false),
            (777, "shared-live-mutation") => new(new("value", false, [3], "AAcAJAGcCAEAEgABAw=="), new("value", false, [3, 11], "AAcAJAGcCAEAEgACAws="), false),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };

    private static (int Lore, int Count) SelectedIds(int protocol)
        => protocol switch
        {
            770 => (8, 96),
            771 => (8, 96),
            772 => (8, 96),
            773 => (8, 96),
            774 => (11, 104),
            775 => (11, 110),
            776 => (11, 111),
            777 => (11, 122),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };
    // NATIVE-VECTORS-END
}
