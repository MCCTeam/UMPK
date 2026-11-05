using System.Buffers;
using System.Text.Json;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = ComponentAdventureMiningSessionTests.Session;

public sealed class EnchantmentMiningIdentitySessionTests
{
    public static IEnumerable<object[]> Replacements()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777 })
            foreach (string scenario in new[] { "equal", "reordered", "changed-level", "tooltip", "shadowed-duplicate", "canonical-duplicate", "changed-final-duplicate", "zero-versus-absent", "changed-key" })
                yield return [protocol, scenario];
        foreach (int protocol in new[] { 766, 770, 777 })
            yield return [protocol, "stored-equal"];
        foreach (string scenario in new[] { "equal", "reordered", "shadowed-duplicate" })
            yield return [765, scenario];
    }

    [Theory]
    [MemberData(nameof(Replacements))]
    public async Task FramedEnchantmentReplacementUsesNativeDestroyTargetIdentity(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol, configurationEnchantments: NativeMiningEnchantmentRegistryFixture.Create(protocol));
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Info(GameMode.Survival);
        (Entry[] before, Entry[] after) = Values(scenario);
        bool stored = scenario == "stored-equal";
        (byte[] firstBytes, ItemStack first) = await Deliver(session, protocol, before, showInTooltip: true, stored: stored);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task action = session.Client.Actions.Interaction.DigBlockAsync(Session.Target, Direction.North, caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Collection(await session.Tick(),
                packet => AssertDestroyAction(packet, protocol, 0),
                packet => AssertSwing(packet, protocol),
                packet => AssertSwing(packet, protocol));
            AssertSwing(Assert.Single(await session.Tick()), protocol);
            Assert.False(action.IsCompleted);

            bool tooltip = scenario != "tooltip";
            (byte[] secondBytes, ItemStack second) = await Deliver(session, protocol, after, tooltip, stored);
            Assert.NotSame(first, second);
            if (stored)
            {
                StoredEnchantmentsComponent original = first.Components.Get(DataComponents.StoredEnchantments)!;
                StoredEnchantmentsComponent replacement = second.Components.Get(DataComponents.StoredEnchantments)!;
                Assert.NotSame(original, replacement);
                Assert.NotSame(original.Enchantments, replacement.Enchantments);
            }
            else if (protocol >= 766)
            {
                EnchantmentsComponent original = first.Components.Get(DataComponents.Enchantments)!;
                EnchantmentsComponent replacement = second.Components.Get(DataComponents.Enchantments)!;
                Assert.NotSame(original, replacement);
                Assert.NotSame(original.Enchantments, replacement.Enchantments);
            }
            else
                Assert.NotSame(first.Components.Get(DataComponents.LegacyNbt)!.Nbt, second.Components.Get(DataComponents.LegacyNbt)!.Nbt);
            bool sameWire = (scenario is "equal" or "stored-equal") || scenario == "tooltip" && protocol >= 770;
            Assert.Equal(sameWire, firstBytes.AsSpan().SequenceEqual(secondBytes));
            bool reset = scenario is "changed-level" or "changed-final-duplicate" or "zero-versus-absent" or "changed-key"
                || scenario == "tooltip" && protocol < 770
                || protocol == 765 && (scenario is "reordered" or "shadowed-duplicate");
            List<IPacket> continued = await session.Tick();
            if (reset)
            {
                Assert.Collection(continued,
                    packet => AssertDestroyAction(packet, protocol, protocol == 777 ? 2 : 1),
                    packet => AssertDestroyAction(packet, protocol, 0),
                    packet => AssertSwing(packet, protocol));
            }
            else
            {
                Assert.Empty(continued.OfType<ServerboundPlayerActionPacket>());
                AssertSwing(Assert.Single(continued), protocol);
            }
            Assert.False(action.IsCompleted);
            int stopAfter = reset ? 6 : 3;
            for (int tick = 1; tick <= stopAfter; tick++)
            {
                List<IPacket> sent = await session.Tick();
                if (tick < stopAfter)
                {
                    AssertSwing(Assert.Single(sent), protocol);
                    Assert.False(action.IsCompleted);
                }
                else
                {
                    Assert.Collection(sent,
                        packet => AssertDestroyAction(packet, protocol, protocol == 777 ? 3 : 2),
                        packet => AssertSwing(packet, protocol));
                    Assert.False(action.IsCompleted);
                    await session.Complete(action, Assert.IsType<ServerboundPlayerActionPacket>(sent[0]));
                    Assert.True(action.IsCompletedSuccessfully);
                }
            }
        }
        finally
        {
            await caller.CancelAsync();
            if (!action.IsCompleted)
                await session.Tick();
            try
            {
                await action.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static void AssertDestroyAction(IPacket packet, int protocol, int expected)
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
            Assert.Equal(0, swing.Hand);
            Assert.True(swing.HasHand);
        }
    }

    private readonly record struct Entry(string Name, int Level);

    private static (Entry[] Before, Entry[] After) Values(string scenario)
        => scenario switch
        {
            "equal" or "tooltip" or "stored-equal" => ([new("unbreaking", 1), new("fortune", 1)], [new("unbreaking", 1), new("fortune", 1)]),
            "reordered" => ([new("unbreaking", 1), new("fortune", 1)], [new("fortune", 1), new("unbreaking", 1)]),
            "changed-level" => ([new("unbreaking", 1), new("fortune", 1)], [new("unbreaking", 2), new("fortune", 1)]),
            "shadowed-duplicate" => ([new("unbreaking", 0), new("unbreaking", 1), new("fortune", 1)], [new("unbreaking", 9), new("unbreaking", 1), new("fortune", 1)]),
            "canonical-duplicate" => ([new("unbreaking", 0), new("unbreaking", 1), new("fortune", 1)], [new("unbreaking", 1), new("fortune", 1)]),
            "changed-final-duplicate" => ([new("unbreaking", 1), new("unbreaking", 2), new("fortune", 1)], [new("unbreaking", 2), new("unbreaking", 1), new("fortune", 1)]),
            "zero-versus-absent" => ([new("unbreaking", 0)], []),
            "changed-key" => ([new("unbreaking", 1)], [new("fortune", 1)]),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

    private static async Task<(byte[] Payload, ItemStack Item)> Deliver(Session session, int protocol, Entry[] entries, bool showInTooltip, bool stored)
    {
        var registries = session.Client.State.Registries!;
        ItemStack stack = new(registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        if (protocol >= 766)
        {
            EnchantmentInstance[] values = entries.Select(entry => new EnchantmentInstance(registries.Enchantments[Identifier.Minecraft(entry.Name)], entry.Level)).ToArray();
            stack = stored
                ? stack.With(DataComponents.StoredEnchantments, new StoredEnchantmentsComponent(values, showInTooltip))
                : stack.With(DataComponents.Enchantments, new EnchantmentsComponent(values, showInTooltip));
        }
        else
        {
            var values = new NbtList(NbtTagType.Compound);
            foreach (Entry entry in entries)
                values.Add(new NbtCompound() { ["id"] = new NbtString("minecraft:" + entry.Name), ["lvl"] = new NbtInt(entry.Level) });
            stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(new NbtCompound() { ["Enchantments"] = values }));
        }
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion version));
        var packetEntry = Assert.Single(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).Packets, pair => pair.Type.Id == Identifier.Minecraft("container_set_slot"));
        Assert.True(version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).TryGetInbound(packetEntry.WireId, out BoundPacketCodec codec));
        Assert.True(codec.IsImplemented);
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        var packet = new ClientboundContainerSetSlotPacket(0, 19, 36, stack);
        byte[] payload = Encode(codec, packet, context);
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(payload, context));
        Assert.Equal(payload, Encode(codec, decoded, context));
        AssertEntries(decoded.Item, entries, showInTooltip, protocol, stored);
        var received = new TaskCompletionSource<ClientboundContainerSetSlotPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        int observedFrames = 0;
        void Observe(PacketObservation observation)
        {
            if (observation.Flow == PacketFlow.Clientbound && observation.Phase == ProtocolPhase.Play && observation.WireId == packetEntry.WireId && observation.RawPayload.SequenceEqual(payload))
                Interlocked.Increment(ref observedFrames);
        }
        using IDisposable subscription = session.Client.Events.Subscribe<PacketReceived>(message =>
        {
            if (message.Packet is ClientboundContainerSetSlotPacket { ContainerId: 0, StateId: 19, Slot: 36 } slot)
                received.TrySetResult(slot);
        });
        session.Client.PacketFrameObserved += Observe;
        try
        {
            await session.Server.SendFrameAsync(packetEntry.WireId, payload, session.Token);
            ClientboundContainerSetSlotPacket arrived = await received.Task.WaitAsync(session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Equal(1, Volatile.Read(ref observedFrames));
            Assert.Equal(payload, Encode(codec, arrived, context));
            ItemStack applied = session.Client.State.Inventory.GetSlot(36);
            Assert.NotSame(decoded.Item, applied);
            Assert.Equal("minecraft:diamond_pickaxe", applied.Item.Id.ToString());
            Assert.Equal(1, applied.Count);
            AssertEntries(applied, entries, showInTooltip, protocol, stored);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
            Assert.Empty(await session.Barrier());
            return (payload, applied);
        }
        finally
        {
            session.Client.PacketFrameObserved -= Observe;
        }
    }

    private static void AssertEntries(ItemStack actual, Entry[] expected, bool tooltip, int protocol, bool stored)
    {
        if (protocol >= 766)
        {
            IReadOnlyList<EnchantmentInstance> values;
            bool actualTooltip;
            if (stored)
            {
                StoredEnchantmentsComponent component = actual.Components.Get(DataComponents.StoredEnchantments)!;
                Assert.NotNull(component);
                values = component.Enchantments;
                actualTooltip = component.ShowInTooltip;
            }
            else
            {
                EnchantmentsComponent component = actual.Components.Get(DataComponents.Enchantments)!;
                Assert.NotNull(component);
                values = component.Enchantments;
                actualTooltip = component.ShowInTooltip;
            }
            Assert.Equal(protocol >= 770 || tooltip, actualTooltip);
            Assert.Equal(expected.Length, values.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.Equal(Identifier.Minecraft(expected[index].Name), values[index].Enchantment.Id);
                Assert.Equal(expected[index].Level, values[index].Level);
            }
        }
        else
        {
            NbtList list = actual.Components.Get(DataComponents.LegacyNbt)!.Nbt.GetList("Enchantments")!;
            Assert.Equal(expected.Length, list.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                var entry = Assert.IsType<NbtCompound>(list[index]);
                Assert.Equal("minecraft:" + expected[index].Name, entry.GetString("id"));
                Assert.Equal(expected[index].Level, entry.GetInt("lvl"));
            }
        }
    }

    private static byte[] Encode(BoundPacketCodec codec, IPacket packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }
}

internal static class NativeMiningEnchantmentRegistryFixture
{
    internal static ClientboundConfigRegistryDataPacket? Create(int protocol)
    {
        if (protocol < 767)
            return null;
        (string Unbreaking, string Fortune) source = protocol switch
        {
            767 or 768 or 769 or 770 or 771 or 772 or 773 or 774 or 775 or 776 => (Unbreaking0, Fortune0),
            777 => (Unbreaking1, Fortune1),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };
        return new ClientboundConfigRegistryDataPacket(RegistryIds.Enchantment,
        [
            new PackedRegistryEntry(Identifier.Minecraft("unbreaking"), ReadDefinition(source.Unbreaking)),
            new PackedRegistryEntry(Identifier.Minecraft("fortune"), ReadDefinition(source.Fortune)),
        ]);
    }

    private static NbtCompound ReadDefinition(string source)
    {
        using JsonDocument document = JsonDocument.Parse(source);
        var value = Assert.IsType<NbtCompound>(Convert(document.RootElement));
        AssertDefinition(document.RootElement, value);
        Assert.Equal(3, value.GetInt("max_level"));
        return value;
    }

    private static NbtTag Convert(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var compound = new NbtCompound();
                foreach (JsonProperty property in value.EnumerateObject())
                    compound.Put(property.Name, Convert(property.Value));
                return compound;
            case JsonValueKind.Array:
                var list = new NbtList();
                foreach (JsonElement entry in value.EnumerateArray())
                    list.Add(Convert(entry));
                return list;
            case JsonValueKind.String:
                return new NbtString(value.GetString()!);
            case JsonValueKind.Number:
                if (value.TryGetInt32(out int integer))
                    return new NbtInt(integer);
                if (value.TryGetInt64(out long wide))
                    return new NbtLong(wide);
                return new NbtDouble(value.GetDouble());
            case JsonValueKind.True:
                return new NbtByte(1);
            case JsonValueKind.False:
                return new NbtByte(0);
            default:
                throw new InvalidOperationException("The retained native enchantment definition has an unsupported JSON value.");
        }
    }

    private static void AssertDefinition(JsonElement expected, NbtTag actual)
    {
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var compound = Assert.IsType<NbtCompound>(actual);
                Assert.Equal(expected.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal), compound.Select(pair => pair.Key).Order(StringComparer.Ordinal));
                foreach (JsonProperty property in expected.EnumerateObject())
                    AssertDefinition(property.Value, compound[property.Name]);
                break;
            case JsonValueKind.Array:
                var list = Assert.IsType<NbtList>(actual);
                Assert.Equal(expected.GetArrayLength(), list.Count);
                int index = 0;
                foreach (JsonElement entry in expected.EnumerateArray())
                    AssertDefinition(entry, list[index++]);
                break;
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), Assert.IsType<NbtString>(actual).Value);
                break;
            case JsonValueKind.Number:
                if (expected.TryGetInt32(out int integer))
                    Assert.Equal(integer, Assert.IsType<NbtInt>(actual).Value);
                else if (expected.TryGetInt64(out long wide))
                    Assert.Equal(wide, Assert.IsType<NbtLong>(actual).Value);
                else
                    Assert.Equal(expected.GetDouble(), Assert.IsType<NbtDouble>(actual).Value);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.Equal(expected.GetBoolean() ? (sbyte)1 : (sbyte)0, Assert.IsType<NbtByte>(actual).Value);
                break;
            default:
                throw new InvalidOperationException("The retained native enchantment definition has an unsupported JSON value.");
        }
    }

    private const string Unbreaking0 = """
        {"anvil_cost":2,"description":{"translate":"enchantment.minecraft.unbreaking"},"effects":{"minecraft:item_damage":[{"effect":{"type":"minecraft:remove_binomial","chance":{"type":"minecraft:fraction","denominator":{"type":"minecraft:linear","base":10.0,"per_level_above_first":5.0},"numerator":{"type":"minecraft:linear","base":2.0,"per_level_above_first":2.0}}},"requirements":{"condition":"minecraft:match_tool","predicate":{"items":"#minecraft:enchantable/armor"}}},{"effect":{"type":"minecraft:remove_binomial","chance":{"type":"minecraft:fraction","denominator":{"type":"minecraft:linear","base":2.0,"per_level_above_first":1.0},"numerator":{"type":"minecraft:linear","base":1.0,"per_level_above_first":1.0}}},"requirements":{"condition":"minecraft:inverted","term":{"condition":"minecraft:match_tool","predicate":{"items":"#minecraft:enchantable/armor"}}}}]},"max_cost":{"base":55,"per_level_above_first":8},"max_level":3,"min_cost":{"base":5,"per_level_above_first":8},"slots":["any"],"supported_items":"#minecraft:enchantable/durability","weight":5}
        """;

    private const string Fortune0 = """
        {"anvil_cost":4,"description":{"translate":"enchantment.minecraft.fortune"},"exclusive_set":"#minecraft:exclusive_set/mining","max_cost":{"base":65,"per_level_above_first":9},"max_level":3,"min_cost":{"base":15,"per_level_above_first":9},"slots":["mainhand"],"supported_items":"#minecraft:enchantable/mining_loot","weight":2}
        """;

    private const string Unbreaking1 = """
        {"anvil_cost":2,"description":{"translate":"enchantment.minecraft.unbreaking"},"effects":{"minecraft:item_damage":[{"effect":{"type":"minecraft:remove_binomial","chance":{"type":"minecraft:fraction","denominator":{"type":"minecraft:linear","base":10.0,"per_level_above_first":5.0},"numerator":{"type":"minecraft:linear","base":2.0,"per_level_above_first":2.0}}},"requirements":{"type":"minecraft:match_tool","predicate":{"items":"#minecraft:enchantable/armor"}}},{"effect":{"type":"minecraft:remove_binomial","chance":{"type":"minecraft:fraction","denominator":{"type":"minecraft:linear","base":2.0,"per_level_above_first":1.0},"numerator":{"type":"minecraft:linear","base":1.0,"per_level_above_first":1.0}}},"requirements":{"type":"minecraft:inverted","term":{"type":"minecraft:match_tool","predicate":{"items":"#minecraft:enchantable/armor"}}}}]},"max_cost":{"base":55,"per_level_above_first":8},"max_level":3,"min_cost":{"base":5,"per_level_above_first":8},"slots":["any"],"supported_items":"#minecraft:enchantable/durability","weight":5}
        """;

    private const string Fortune1 = """
        {"anvil_cost":4,"description":{"translate":"enchantment.minecraft.fortune"},"exclusive_set":"#minecraft:exclusive_set/mining","max_cost":{"base":65,"per_level_above_first":9},"max_level":3,"min_cost":{"base":15,"per_level_above_first":9},"slots":["mainhand"],"supported_items":"#minecraft:enchantable/mining_loot","weight":2}
        """;
}
