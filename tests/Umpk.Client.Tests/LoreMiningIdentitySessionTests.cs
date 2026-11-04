using System.Buffers;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class LoreMiningIdentitySessionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int protocol in new[] { 766, 769, 770, 777 })
            foreach (string scenario in new[] { "same-plain", "same-rich", "same-translated", "same-two-lines", "same-empty", "changed-text", "changed-style", "changed-children", "reordered-lines", "added-line", "empty-versus-default", "empty-versus-removed" })
                yield return [protocol, scenario];
        foreach (int protocol in new[] { 767, 768, 771, 772, 773, 774, 775, 776 })
            yield return [protocol, "same-plain"];
        foreach (string scenario in new[] { "legacy-same", "legacy-changed-style", "legacy-json-order" })
            yield return [765, scenario];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FreshFramedLoreUsesNativeDestroyTargetIdentity(int protocol, string scenario)
    {
        void CheckSwing(IPacket packet) => AssertSwing(packet, protocol);

        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        ItemStack before = Stack(session, scenario, replacement: false);
        ItemStack after = Stack(session, scenario, replacement: true);
        var native = NativePayloads(protocol, scenario);
        await Deliver(session, before, native?.Before);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Collection(await session.Tick(), packet => AssertAction(packet, protocol, 0), CheckSwing, CheckSwing);
            CheckSwing(Assert.Single(await session.Tick()));
            Assert.False(action.IsCompleted);

            await Deliver(session, after, native?.After);
            ItemStack replacement = session.Client.State.Inventory.GetSlot(36);
            Assert.NotSame(original, replacement);
            if (protocol >= 766 && original.Components.Get(DataComponents.Lore) is { Lines.Count: > 0 } originalLore
                && replacement.Components.Get(DataComponents.Lore) is { } replacementLore)
            {
                Assert.NotSame(originalLore, replacementLore);
                Assert.NotSame(originalLore.Lines, replacementLore.Lines);
                if (scenario is "changed-style" or "changed-children")
                    Assert.Equal(originalLore.Lines.Select(line => line.ToPlainText()), replacementLore.Lines.Select(line => line.ToPlainText()));
            }
            bool reset = !scenario.StartsWith("same-", StringComparison.Ordinal) && scenario is not ("empty-versus-default" or "legacy-same");
            if (native is { } witness)
                Assert.Equal(!reset, witness.Equal);
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

    [Theory]
    [InlineData(766, "same-numeric")]
    [InlineData(766, "same-boolean")]
    [InlineData(766, "same-nested-numeric")]
    [InlineData(777, "same-numeric")]
    [InlineData(777, "same-boolean")]
    [InlineData(777, "same-nested-numeric")]
    public async Task UnmodeledNativeLoreEndsHeldRequestWithoutGuessingAChangedItem(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        await DeliverPrimitiveLore(session, scenario);
        ItemStack original = session.Client.State.Inventory.GetSlot(36);
        int targetState = session.Client.State.World.GetBlock(Session.Target).StateId;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Collection(await session.Tick(), packet => AssertAction(packet, protocol, 0), packet => AssertSwing(packet, protocol), packet => AssertSwing(packet, protocol));
            AssertSwing(Assert.Single(await session.Tick()), protocol);
            Assert.False(action.IsCompleted);
            Assert.Same(original, session.Client.State.Inventory.GetSlot(36));

            // Native post-stream equality is true, but the public text model has lost the primitive argument's native branch.
            await DeliverPrimitiveLore(session, scenario);
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
            Assert.False(session.Client.State.Inventory.GetSlot(36).IsEmpty);
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await session.Tick();
            try { await action.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }
    }

    private static async Task DeliverPrimitiveLore(Session session, string scenario)
    {
        byte[] original = Convert.FromBase64String(PrimitivePayload(session.Protocol, scenario));
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "container_set_slot");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(original, context));
        Assert.Equal(36, packet.Slot);
        Assert.Equal(Identifier.Minecraft("diamond_pickaxe"), packet.Item.Item.Id);
        Assert.Equal(1, packet.Item.Count);
        Component line = Assert.Single(Assert.IsType<LoreComponent>(packet.Item.Components.Get(DataComponents.Lore)).Lines);
        Component translated = scenario == "same-nested-numeric" ? Assert.Single(line.Children) : line;
        if (scenario == "same-nested-numeric")
            Assert.Equal("Root", Assert.IsType<TextContent>(line.Content).Text);
        var content = Assert.IsType<TranslatableContent>(translated.Content);
        Assert.Equal("probe.native", content.Key);
        if (session.Protocol == 766)
            Assert.Empty(content.Args);
        else
            Assert.Equal("1", Assert.IsType<TextContent>(Assert.Single(content.Args).Content).Text);
        var roundTrip = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(roundTrip);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(original, roundTrip.WrittenSpan.ToArray());
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
    }

    // Original fourteen-row witness confirms same primitive values remain equal after stream decode; these exact packets retain the unmodeled branch.
    private static string PrimitivePayload(int protocol, string scenario)
        => (protocol, scenario) switch
        {
            (766, "same-numeric") => "AAcAJAHHBgEABwEKBwAEd2l0aAAAAAEBCAAJdHJhbnNsYXRlAAxwcm9iZS5uYXRpdmUA",
            (766, "same-boolean") => "AAcAJAHHBgEABwEKBwAEd2l0aAAAAAEBCAAJdHJhbnNsYXRlAAxwcm9iZS5uYXRpdmUA",
            (766, "same-nested-numeric") => "AAcAJAHHBgEABwEKCQAFZXh0cmEKAAAAAQcABHdpdGgAAAABAQgACXRyYW5zbGF0ZQAMcHJvYmUubmF0aXZlAAgABHRleHQABFJvb3QA",
            (777, "same-numeric") => "AAcAJAGcCAEACwEKCQAEd2l0aAEAAAABAQgACXRyYW5zbGF0ZQAMcHJvYmUubmF0aXZlAA==",
            (777, "same-boolean") => "AAcAJAGcCAEACwEKCQAEd2l0aAEAAAABAQgACXRyYW5zbGF0ZQAMcHJvYmUubmF0aXZlAA==",
            (777, "same-nested-numeric") => "AAcAJAGcCAEACwEKCQAFZXh0cmEKAAAAAQkABHdpdGgBAAAAAQEIAAl0cmFuc2xhdGUADHByb2JlLm5hdGl2ZQAIAAR0ZXh0AARSb290AA==",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static ItemStack Stack(Session session, string scenario, bool replacement)
    {
        ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        if (session.Protocol == 765)
        {
            string value = replacement && scenario == "legacy-changed-style" ? "{\"text\":\"Alpha\",\"bold\":false}"
                : replacement && scenario == "legacy-json-order" ? "{\"bold\":true,\"text\":\"Alpha\"}"
                : "{\"text\":\"Alpha\",\"bold\":true}";
            return stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(new NbtCompound
            {
                ["display"] = new NbtCompound { ["Lore"] = new NbtList(NbtTagType.String) { new NbtString(value) } },
            }));
        }
        if (replacement && scenario == "empty-versus-default")
            return stack;
        if (replacement && scenario == "empty-versus-removed")
        {
            // The native prototype has Lore.EMPTY; the bounded UMPK item registry intentionally omits this default.
            var prototype = stack.Item.Value.DefaultComponents.ToDictionary(pair => pair.Key, pair => pair.Value);
            prototype[DataComponents.Lore] = new LoreComponent([]);
            return stack.WithComponents(DataComponentMap.Create(prototype, [DataComponentEntry.Remove(DataComponents.Lore)]));
        }
        Component[] lines = scenario switch
        {
            "same-empty" or "empty-versus-default" or "empty-versus-removed" => [],
            "same-rich" or "changed-style" => [Rich(replacement && scenario == "changed-style")],
            "same-translated" => [new Component(new TranslatableContent("chat.type.text", null, [Component.Text("Alex"), Component.Text("Hello")]), Style.Empty, [Component.Text("!")])],
            "same-two-lines" => [Component.Text("Alpha"), Component.Text("Beta")],
            "reordered-lines" => replacement ? [Component.Text("Beta"), Component.Text("Alpha")] : [Component.Text("Alpha"), Component.Text("Beta")],
            "added-line" when replacement => [Component.Text("Alpha"), Component.Text("Beta")],
            "changed-text" when replacement => [Component.Text("Beta")],
            "changed-children" => replacement ? [Component.Text("Alpha child")] : [new Component(new TextContent("Alpha"), Style.Empty, [Component.Text(" child")])],
            _ => [Component.Text("Alpha")],
        };
        return stack.With(DataComponents.Lore, new LoreComponent(lines));

        static Component Rich(bool changed) => new(new TextContent("Alpha"), new Style { Color = changed ? TextColor.Red : TextColor.Gold, Bold = true }, [new Component(new TextContent(" child"), new Style { Italic = false })]);
    }

    private static async Task Deliver(Session session, ItemStack stack, string? originalPayload)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(session.Protocol, "container_set_slot");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = new ClientboundContainerSetSlotPacket(0, 7, 36, stack);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        byte[] bytes = originalPayload is null ? output.WrittenSpan.ToArray() : Convert.FromBase64String(originalPayload);
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
        Assert.Equal(36, decoded.Slot);
        Assert.Equal(stack.Item.Id, decoded.Item.Item.Id);
        Assert.Equal(stack.Count, decoded.Item.Count);
        if (session.Protocol >= 766)
        {
            LoreComponent? expected = stack.Components.Get(DataComponents.Lore);
            LoreComponent? actual = decoded.Item.Components.Get(DataComponents.Lore);
            bool removed = stack.Components.Patch.Any(entry => entry.Type.Equals(DataComponents.Lore) && entry.IsRemoval);
            Assert.Equal(removed, decoded.Item.Components.Patch.Any(entry => entry.Type.Equals(DataComponents.Lore) && entry.IsRemoval));
            if (expected is { Lines.Count: > 0 })
            {
                Assert.NotNull(actual);
                Assert.Equal(expected.Lines, actual.Lines);
            }
            else
            {
                // A native default-empty patch can decode to the bounded registry's absent Lore value; explicit removal is checked independently above.
                Assert.Equal(0, actual?.Lines.Count ?? 0);
            }
        }
        else
            Assert.Equal(stack.Components.Get(DataComponents.LegacyNbt)!.Nbt, decoded.Item.Components.Get(DataComponents.LegacyNbt)!.Nbt);
        var roundTrip = new ArrayBufferWriter<byte>();
        var roundTripWriter = new PacketWriter(roundTrip);
        codec.Encode(ref roundTripWriter, decoded, context);
        Assert.Equal(bytes, roundTrip.WrittenSpan.ToArray());
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
    }

    // Literal full packet payloads from original766/777 codecs, including their measured default/removal patches.
    private static (string Before, string After, bool Equal)? NativePayloads(int protocol, string scenario)
        => (protocol, scenario) switch
        {
            (766, "same-plain") => ("AAcAJAHHBgEABwEIAAVBbHBoYQ==", "AAcAJAHHBgEABwEIAAVBbHBoYQ==", true),
            (766, "same-rich") => ("AAcAJAHHBgEABwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAHHBgEABwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", true),
            (766, "same-translated") => ("AAcAJAHHBgEABwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", "AAcAJAHHBgEABwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", true),
            (766, "same-two-lines") => ("AAcAJAHHBgEABwIIAAVBbHBoYQgABEJldGE=", "AAcAJAHHBgEABwIIAAVBbHBoYQgABEJldGE=", true),
            (766, "same-empty") => ("AAcAJAHHBgAA", "AAcAJAHHBgAA", true),
            (766, "changed-text") => ("AAcAJAHHBgEABwEIAAVBbHBoYQ==", "AAcAJAHHBgEABwEIAARCZXRh", false),
            (766, "changed-style") => ("AAcAJAHHBgEABwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAHHBgEABwEKCAAFY29sb3IAA3JlZAkABWV4dHJhCgAAAAEIAAR0ZXh0AAYgY2hpbGQBAAZpdGFsaWMAAAgABHRleHQABUFscGhhAQAEYm9sZAEA", false),
            (766, "changed-children") => ("AAcAJAHHBgEABwEKCQAFZXh0cmEIAAAAAQAGIGNoaWxkCAAEdGV4dAAFQWxwaGEA", "AAcAJAHHBgEABwEIAAtBbHBoYSBjaGlsZA==", false),
            (766, "reordered-lines") => ("AAcAJAHHBgEABwIIAAVBbHBoYQgABEJldGE=", "AAcAJAHHBgEABwIIAARCZXRhCAAFQWxwaGE=", false),
            (766, "added-line") => ("AAcAJAHHBgEABwEIAAVBbHBoYQ==", "AAcAJAHHBgEABwIIAAVBbHBoYQgABEJldGE=", false),
            (766, "empty-versus-default") => ("AAcAJAHHBgAA", "AAcAJAHHBgAA", true),
            (766, "empty-versus-removed") => ("AAcAJAHHBgAA", "AAcAJAHHBgABBw==", false),
            (777, "same-plain") => ("AAcAJAGcCAEACwEIAAVBbHBoYQ==", "AAcAJAGcCAEACwEIAAVBbHBoYQ==", true),
            (777, "same-rich") => ("AAcAJAGcCAEACwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAGcCAEACwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", true),
            (777, "same-translated") => ("AAcAJAGcCAEACwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", "AAcAJAGcCAEACwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", true),
            (777, "same-two-lines") => ("AAcAJAGcCAEACwIIAAVBbHBoYQgABEJldGE=", "AAcAJAGcCAEACwIIAAVBbHBoYQgABEJldGE=", true),
            (777, "same-empty") => ("AAcAJAGcCAAA", "AAcAJAGcCAAA", true),
            (777, "changed-text") => ("AAcAJAGcCAEACwEIAAVBbHBoYQ==", "AAcAJAGcCAEACwEIAARCZXRh", false),
            (777, "changed-style") => ("AAcAJAGcCAEACwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAGcCAEACwEKCAAFY29sb3IAA3JlZAkABWV4dHJhCgAAAAEIAAR0ZXh0AAYgY2hpbGQBAAZpdGFsaWMAAAgABHRleHQABUFscGhhAQAEYm9sZAEA", false),
            (777, "changed-children") => ("AAcAJAGcCAEACwEKCQAFZXh0cmEIAAAAAQAGIGNoaWxkCAAEdGV4dAAFQWxwaGEA", "AAcAJAGcCAEACwEIAAtBbHBoYSBjaGlsZA==", false),
            (777, "reordered-lines") => ("AAcAJAGcCAEACwIIAAVBbHBoYQgABEJldGE=", "AAcAJAGcCAEACwIIAARCZXRhCAAFQWxwaGE=", false),
            (777, "added-line") => ("AAcAJAGcCAEACwEIAAVBbHBoYQ==", "AAcAJAGcCAEACwIIAAVBbHBoYQgABEJldGE=", false),
            (777, "empty-versus-default") => ("AAcAJAGcCAAA", "AAcAJAGcCAAA", true),
            (777, "empty-versus-removed") => ("AAcAJAGcCAAA", "AAcAJAGcCAABCw==", false),
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
