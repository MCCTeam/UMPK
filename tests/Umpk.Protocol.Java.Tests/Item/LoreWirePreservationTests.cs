using System.Buffers;
using System.Text.Json;
using Umpk.Data.Java;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

public sealed class LoreWirePreservationTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        yield return ["766-changed-children", 766, "AAcAJAHHBgEABwEKCQAFZXh0cmEIAAAAAQAGIGNoaWxkCAAEdGV4dAAFQWxwaGEA", "AAcAJAHHBgEABwEIAAtBbHBoYSBjaGlsZA==", "[{\"text\":\"Alpha\",\"extra\":[{\"text\":\" child\"}]}]", "[\"Alpha child\"]"];
        yield return ["766-changed-style", 766, "AAcAJAHHBgEABwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAHHBgEABwEKCAAFY29sb3IAA3JlZAkABWV4dHJhCgAAAAEIAAR0ZXh0AAYgY2hpbGQBAAZpdGFsaWMAAAgABHRleHQABUFscGhhAQAEYm9sZAEA", "[{\"text\":\"Alpha\",\"color\":\"gold\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]", "[{\"text\":\"Alpha\",\"color\":\"red\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]"];
        yield return ["766-same-rich", 766, "AAcAJAHHBgEABwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAHHBgEABwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "[{\"text\":\"Alpha\",\"color\":\"gold\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]", "[{\"text\":\"Alpha\",\"color\":\"gold\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]"];
        yield return ["766-same-translated", 766, "AAcAJAHHBgEABwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", "AAcAJAHHBgEABwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", "[{\"translate\":\"chat.type.text\",\"with\":[{\"text\":\"Alex\"},{\"text\":\"Hello\"}],\"extra\":[{\"text\":\"!\"}]}]", "[{\"translate\":\"chat.type.text\",\"with\":[{\"text\":\"Alex\"},{\"text\":\"Hello\"}],\"extra\":[{\"text\":\"!\"}]}]"];
        yield return ["777-changed-children", 777, "AAcAJAGcCAEACwEKCQAFZXh0cmEIAAAAAQAGIGNoaWxkCAAEdGV4dAAFQWxwaGEA", "AAcAJAGcCAEACwEIAAtBbHBoYSBjaGlsZA==", "[{\"text\":\"Alpha\",\"extra\":[{\"text\":\" child\"}]}]", "[\"Alpha child\"]"];
        yield return ["777-changed-style", 777, "AAcAJAGcCAEACwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAGcCAEACwEKCAAFY29sb3IAA3JlZAkABWV4dHJhCgAAAAEIAAR0ZXh0AAYgY2hpbGQBAAZpdGFsaWMAAAgABHRleHQABUFscGhhAQAEYm9sZAEA", "[{\"text\":\"Alpha\",\"color\":\"gold\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]", "[{\"text\":\"Alpha\",\"color\":\"red\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]"];
        yield return ["777-same-rich", 777, "AAcAJAGcCAEACwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "AAcAJAGcCAEACwEKCAAFY29sb3IABGdvbGQJAAVleHRyYQoAAAABCAAEdGV4dAAGIGNoaWxkAQAGaXRhbGljAAAIAAR0ZXh0AAVBbHBoYQEABGJvbGQBAA==", "[{\"text\":\"Alpha\",\"color\":\"gold\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]", "[{\"text\":\"Alpha\",\"color\":\"gold\",\"bold\":true,\"extra\":[{\"text\":\" child\",\"italic\":false}]}]"];
        yield return ["777-same-translated", 777, "AAcAJAGcCAEACwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", "AAcAJAGcCAEACwEKCQAEd2l0aAgAAAACAARBbGV4AAVIZWxsbwkABWV4dHJhCAAAAAEAASEIAAl0cmFuc2xhdGUADmNoYXQudHlwZS50ZXh0AA==", "[{\"translate\":\"chat.type.text\",\"with\":[{\"text\":\"Alex\"},{\"text\":\"Hello\"}],\"extra\":[{\"text\":\"!\"}]}]", "[{\"translate\":\"chat.type.text\",\"with\":[{\"text\":\"Alex\"},{\"text\":\"Hello\"}],\"extra\":[{\"text\":\"!\"}]}]"];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void OriginalNativeLoreFramesPreserveFieldsAndExactBytes(string witness, int protocol, string before, string after, string beforeJson, string afterJson)
    {
        Assert.StartsWith($"{protocol}-", witness);
        Check(before, beforeJson);
        Check(after, afterJson);

        void Check(string payload, string expectedJson)
        {
            byte[] bytes = Convert.FromBase64String(payload);
            var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
            BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
            var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
            Assert.Equal((0, 7, (short)36), (packet.ContainerId, packet.StateId, packet.Slot));
            Assert.Equal(1, packet.Item.Count);
            LoreComponent lore = Assert.IsType<LoreComponent>(packet.Item.Components.Get(DataComponents.Lore));
            using JsonDocument source = JsonDocument.Parse(expectedJson);
            Component[] expected = source.RootElement.EnumerateArray().Select(line => ComponentJson.Parse(line.GetRawText())).ToArray();
            Assert.Equal(expected, lore.Lines);
            Assert.Equal(bytes, Encode(codec, packet, context));
        }
    }

    [Theory]
    [InlineData(766)]
    [InlineData(777)]
    public void EditingDecodedLoreCannotReplayStaleWireBytes(int protocol)
    {
        object[] row = NativeCases().Single(value => (int)value[1] == protocol && ((string)value[0]).EndsWith("same-rich", StringComparison.Ordinal));
        byte[] bytes = Convert.FromBase64String((string)row[2]);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
        LoreComponent lore = Assert.IsType<LoreComponent>(packet.Item.Components.Get(DataComponents.Lore));
        Component[] lines = Assert.IsType<Component[]>(lore.Lines);
        lines[0] = Component.Text("Changed after decoding");

        byte[] changed = Encode(codec, packet, context);
        Assert.False(bytes.AsSpan().SequenceEqual(changed));
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(changed, context));
        LoreComponent actual = Assert.IsType<LoreComponent>(decoded.Item.Components.Get(DataComponents.Lore));
        Assert.Equal("Changed after decoding", Assert.Single(actual.Lines).ToPlainText());
    }

    [Theory]
    [InlineData(770)]
    [InlineData(777)]
    public void OriginalWireLayoutDoesNotChangeLoreDataHash(int protocol)
    {
        object[] row = NativeCases().Single(value => (int)value[1] == 777 && ((string)value[0]).EndsWith("same-rich", StringComparison.Ordinal));
        var nativeContext = new PacketCodecContext(JavaGameData.Registries(777), IConnectionCodecState.Empty);
        BoundPacketCodec codec = BoundCodec.At(777, PacketFlow.Clientbound, "minecraft:container_set_slot");
        var decoded = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(Convert.FromBase64String((string)row[2]), nativeContext));
        LoreComponent nativeLore = Assert.IsType<LoreComponent>(decoded.Item.Components.Get(DataComponents.Lore));
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        ItemComponentTable table = protocol == 770 ? ItemComponentTable.V1_21_5() : ItemComponentTable.V26_3();
        ItemStack stack = new(context.Registries.Items[Identifier.Minecraft("diamond_pickaxe")], 1);
        HashedStackValue actual = ItemStackCodecs.ToHashedStack(stack.With(DataComponents.Lore, nativeLore), table, context);
        HashedStackValue expected = ItemStackCodecs.ToHashedStack(stack.With(DataComponents.Lore, new LoreComponent(nativeLore.Lines.ToArray())), table, context);
        Assert.Equal(expected.AddedComponentHashes, actual.AddedComponentHashes);
    }

    [Theory]
    [InlineData(766, false)]
    [InlineData(766, true)]
    [InlineData(777, false)]
    [InlineData(777, true)]
    public void PrimitiveTranslationArgumentsRoundTripWithoutInventingADataHash(int protocol, bool boolean)
    {
        var argument = boolean ? (NbtTag)new NbtByte(1) : new NbtInt(1);
        var arguments = new NbtList { argument };
        var component = new NbtCompound();
        component.Put("with", arguments);
        component.PutString("translate", "umpk.lore.primitive");
        byte[] original = EncodeRawLore(component);
        var context = Context(protocol);
        ItemComponentCodec codec = LoreCodec(protocol);
        LoreComponent lore = DecodeLore(codec, original, context);
        var content = Assert.IsType<TranslatableContent>(Assert.Single(lore.Lines).Content);
        Assert.Equal("1", Assert.Single(content.Args).ToPlainText());

        Assert.Equal(original, EncodeLore(codec, lore, context));
        Assert.Throws<ComponentHashUnavailableException>(() => codec.Hash(new HashOps(), lore, context));
        if (protocol == 777)
        {
            ItemComponentTable table = ItemComponentTable.V26_3();
            ItemStack stack = new ItemStack(context.Registries.Items[Identifier.Minecraft("stone")], 1)
                .With(DataComponents.Lore, lore)
                .With(DataComponents.MapId, new MapIdComponent(29));
            HashedStackValue hashed = ItemStackCodecs.ToHashedStack(stack, table, context);
            (int componentId, int hash) = Assert.Single(hashed.AddedComponentHashes);
            Assert.Equal(table.WireId(DataComponents.MapId), componentId);
            Assert.Equal(new HashOps().Int(29), hash);
        }
    }

    [Theory]
    [InlineData(766)]
    [InlineData(777)]
    public void EditingOpaqueHoverNbtCannotReplayAnEarlierSnapshot(int protocol)
    {
        var data = new NbtCompound();
        data.PutString("minecraft:custom_name", "before");
        var contents = new NbtCompound();
        contents.Put("components", data);
        contents.PutInt("count", 1);
        contents.PutString("id", "minecraft:stone");
        var hover = new NbtCompound();
        if (protocol == 766)
            hover.Put("contents", contents);
        else
            foreach ((string key, NbtTag value) in contents)
                hover.Put(key, value);
        hover.PutString("action", "show_item");
        var component = new NbtCompound();
        component.Put(protocol == 766 ? "hoverEvent" : "hover_event", hover);
        component.PutString("text", "Alpha");
        byte[] original = EncodeRawLore(component);
        var context = Context(protocol);
        ItemComponentCodec codec = LoreCodec(protocol);
        LoreComponent lore = DecodeLore(codec, original, context);
        Component line = Assert.Single(lore.Lines);
        var decodedHover = Assert.IsType<HoverShowItem>(line.Style.HoverEvent);
        var decodedData = Assert.IsType<NbtCompound>(decodedHover.Data);
        Assert.Equal(original, EncodeLore(codec, lore, context));

        decodedData.PutString("minecraft:custom_name", "after");
        byte[] changed = EncodeLore(codec, lore, context);
        Assert.False(original.AsSpan().SequenceEqual(changed));
        Assert.Same(line, Assert.Single(lore.Lines));
        LoreComponent decoded = DecodeLore(codec, changed, context);
        var actualHover = Assert.IsType<HoverShowItem>(Assert.Single(decoded.Lines).Style.HoverEvent);
        Assert.Equal("after", Assert.IsType<NbtCompound>(actualHover.Data).GetString("minecraft:custom_name"));
    }

    [Theory]
    [InlineData(766, 777)]
    [InlineData(777, 766)]
    public void ReceivedLoreNeverReplaysTheOtherErasInteractionDialect(int sourceProtocol, int destinationProtocol)
    {
        var click = new NbtCompound();
        click.PutString(sourceProtocol == 766 ? "value" : "command", "/say lore");
        click.PutString("action", "run_command");
        var component = new NbtCompound();
        component.Put(sourceProtocol == 766 ? "clickEvent" : "click_event", click);
        component.PutString("text", "Alpha");
        LoreComponent lore = DecodeLore(LoreCodec(sourceProtocol), EncodeRawLore(component), Context(sourceProtocol));

        byte[] encoded = EncodeLore(LoreCodec(destinationProtocol), lore, Context(destinationProtocol));
        var reader = new PacketReader(encoded);
        Assert.Equal(1, reader.ReadVarInt());
        var actual = Assert.IsType<NbtCompound>(reader.ReadNbt(ItemCodecPrimitives.NetworkNbt));
        Assert.Equal(0, reader.Remaining);
        string key = destinationProtocol == 766 ? "clickEvent" : "click_event";
        var actualClick = Assert.IsType<NbtCompound>(actual.GetCompound(key));
        Assert.False(actual.ContainsKey(destinationProtocol == 766 ? "click_event" : "clickEvent"));
        Assert.Equal("/say lore", actualClick.GetString(destinationProtocol == 766 ? "value" : "command"));
        Assert.Equal("run_command", actualClick.GetString("action"));
    }

    [Theory]
    [InlineData(false, "AQoHAAR3aXRoAAAAAQEIAAl0cmFuc2xhdGUADHByb2JlLm5hdGl2ZQA=")]
    [InlineData(true, "AQoJAAVleHRyYQoAAAABBwAEd2l0aAAAAAEBCAAJdHJhbnNsYXRlAAxwcm9iZS5uYXRpdmUACAAEdGV4dAAEUm9vdAA=")]
    public void OriginalLegacyNumericArrayArgumentsStayUnresolvedAfterTheirModelIsLost(bool nested, string payload)
    {
        var context = Context(766);
        byte[] original = Convert.FromBase64String(payload);
        LoreComponent lore = DecodeLore(ItemComponentCodecs.LoreLegacy, original, context);
        Component line = Assert.Single(lore.Lines);
        Component translated = nested ? Assert.Single(line.Children) : line;
        var content = Assert.IsType<TranslatableContent>(translated.Content);
        Assert.Equal("probe.native", content.Key);
        Assert.Empty(content.Args);

        Assert.Equal(original, EncodeLore(ItemComponentCodecs.LoreLegacy, lore, context));
        Assert.Throws<ComponentHashUnavailableException>(() => ItemComponentCodecs.LoreLegacy.Hash(new HashOps(), lore, context));
        Assert.Throws<ComponentHashUnavailableException>(() => ItemComponentCodecs.Lore.Hash(new HashOps(), lore, Context(777)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherNumericArrayArgumentsCannotAcquireInventedSemanticHashes(bool longArray)
    {
        NbtTag arguments = longArray ? new NbtLongArray([42L]) : new NbtIntArray([42]);
        var component = new NbtCompound();
        component.Put("with", arguments);
        component.PutString("translate", "umpk.lore.numeric-array");
        byte[] original = EncodeRawLore(component);
        var context = Context(766);
        LoreComponent lore = DecodeLore(ItemComponentCodecs.LoreLegacy, original, context);
        Assert.Empty(Assert.IsType<TranslatableContent>(Assert.Single(lore.Lines).Content).Args);

        Assert.Equal(original, EncodeLore(ItemComponentCodecs.LoreLegacy, lore, context));
        Assert.Throws<ComponentHashUnavailableException>(() => ItemComponentCodecs.LoreLegacy.Hash(new HashOps(), lore, context));
    }

    private static PacketCodecContext Context(int protocol) => new(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);

    private static ItemComponentCodec LoreCodec(int protocol) => protocol == 766 ? ItemComponentCodecs.LoreLegacy : ItemComponentCodecs.Lore;

    private static LoreComponent DecodeLore(ItemComponentCodec codec, byte[] bytes, PacketCodecContext context)
    {
        var reader = new PacketReader(bytes);
        LoreComponent lore = Assert.IsType<LoreComponent>(codec.Decode(ref reader, context));
        Assert.Equal(0, reader.Remaining);
        return lore;
    }

    private static byte[] EncodeRawLore(NbtTag line)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        writer.WriteVarInt(1);
        writer.WriteNbt(line, ItemCodecPrimitives.NetworkNbt);
        return output.WrittenSpan.ToArray();
    }

    private static byte[] EncodeLore(ItemComponentCodec codec, LoreComponent lore, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, lore, context);
        return output.WrittenSpan.ToArray();
    }

    private static byte[] Encode(BoundPacketCodec codec, IPacket packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
