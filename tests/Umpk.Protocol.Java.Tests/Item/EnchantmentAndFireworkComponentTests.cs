using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Protocol.Java.Tests.Transport;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary><c>minecraft:enchantments</c>, <c>minecraft:stored_enchantments</c>, <c>minecraft:fireworks</c> and <c>minecraft:firework_explosion</c> were listed-but-untyped, so an enchanted item, an enchanted book, a firework rocket or a firework star anywhere in a decoded container patch raised <see cref="UnmodeledItemComponentException"/> and the WHOLE packet was dropped. A compact component payload has no length prefix: its stream codec decodes immediately after the type id, so there is no skip-the-unknown escape: the only way to stop losing packets is to type the component.</summary>
/// <remarks>
/// <para>The known blind spots are all live here, so nothing below is a bare round trip:</para>
/// <list type="bullet">
/// <item>The registration fixture pins codec-versus-marker, not WHICH codec, so every era assertion is
/// either a specific wire-id BYTE, a frame LENGTH, or a cross-era rejection.</item>
/// <item>A round trip through the wrong enchantments codec agrees with itself perfectly: the pre-1.21.5
/// shape differs from the 1.21.5 shape by ONE trailing bool and the wire id is 10 on both 768/769 and 770. Only <see cref="Enchantments_ShowInTooltipBoolIsTheProtocolsOwn"/> (a length) and <see cref="EnchantmentsFrames_DoNotCrossThe770Boundary"/> (both directions of rejection) can see a codec bound one era early or late.</item>
/// <item>Every sample is POPULATED. An empty enchantment map or an explosion list of zero encodes
/// identically under both shapes on the byte that matters and would prove nothing.</item>
/// </list>
/// </remarks>
public class EnchantmentAndFireworkComponentTests
{
    private const string SetSlot = "minecraft:container_set_slot";

    // container_set_slot header for the values used below: container id (1 byte, a signed byte on 766/767 and a VarInt from 768), state id (1), slot (2), count (1), item holder id (1), added (1), removed (1).
    private const int HeaderBytes = 8;

    // Wire ids, restated from each version's component registration order rather than read from ComponentIds, so that changing the production array is what a failure reports.

    /// <summary>(protocol, <c>minecraft:enchantments</c> wire id) for every era that has a table.</summary>
    public static TheoryData<int, int> EnchantmentsWireIds => new()
    {
        { 766, 9 }, { 767, 9 }, { 768, 10 }, { 769, 10 }, { 770, 10 }, { 771, 10 }, { 773, 10 },
        { 774, 13 }, { 775, 13 }, { 776, 13 },
    };

    /// <summary>(protocol, <c>minecraft:fireworks</c> wire id).</summary>
    public static TheoryData<int, int> FireworksWireIds => new()
    {
        { 766, 45 }, { 767, 46 }, { 768, 56 }, { 769, 56 }, { 770, 60 }, { 771, 60 }, { 773, 60 },
        { 774, 67 }, { 775, 69 }, { 776, 69 },
    };

    /// <summary>(protocol, <c>minecraft:firework_explosion</c> wire id), always fireworks minus one.</summary>
    public static TheoryData<int, int> FireworkExplosionWireIds => new()
    {
        { 766, 44 }, { 767, 45 }, { 768, 55 }, { 769, 55 }, { 770, 59 }, { 771, 59 }, { 773, 59 },
        { 774, 66 }, { 775, 68 }, { 776, 68 },
    };

    /// <summary>(protocol, <c>minecraft:stored_enchantments</c> wire id).</summary>
    public static TheoryData<int, int> StoredEnchantmentsWireIds => new()
    {
        { 766, 23 }, { 767, 23 }, { 768, 33 }, { 769, 33 }, { 770, 34 }, { 776, 42 },
    };

    // The packet must survive decoding.

    /// <summary>A firework rocket in a container slot must decode on every component era without raising <see cref="UnmodeledItemComponentException"/> or dropping the frame.</summary>
    [Theory]
    [MemberData(nameof(FireworksWireIds))]
    public void FireworkRocket_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(
            protocol, slot: 4, itemId: ItemTestRegistries.DiamondSword, componentWireId: wireId, write: WriteSampleFireworks);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(DataComponents.Fireworks, out FireworksComponent? fireworks));
        Assert.Equal(2, fireworks!.FlightDuration);
        FireworkExplosion layer = Assert.Single(fireworks.Explosions);
        Assert.Equal("star", layer.Shape);
        Assert.Equal(new[] { 0xFF0000, 0x00FF00 }, layer.Colors);
        Assert.Equal(new[] { 0x0000FF }, layer.FadeColors);
        Assert.True(layer.HasTrail);
        Assert.False(layer.HasTwinkle);

        // Frame-exact both ways.
        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>A firework STAR (<c>minecraft:firework_explosion</c>, one bare layer) is the sibling component and was equally untyped everywhere.</summary>
    [Theory]
    [MemberData(nameof(FireworkExplosionWireIds))]
    public void FireworkStar_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(
            protocol, slot: 5, itemId: ItemTestRegistries.Stone, componentWireId: wireId, write: WriteSampleExplosion);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(
            DataComponents.FireworkExplosion, out FireworkExplosionComponent? star));
        Assert.Equal("star", star!.Explosion.Shape);
        Assert.Equal(new[] { 0xFF0000, 0x00FF00 }, star.Explosion.Colors);
        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>An enchanted diamond sword, on every era. 766-769 carry the trailing <c>showInTooltip</c> bool; 770+ do not.</summary>
    [Theory]
    [MemberData(nameof(EnchantmentsWireIds))]
    public void EnchantedItem_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(
            protocol,
            slot: 0,
            itemId: ItemTestRegistries.DiamondSword,
            componentWireId: wireId,
            write: (ref PacketWriter w) => WriteSampleEnchantments(ref w, protocol < 770));

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? ench));
        EnchantmentInstance instance = Assert.Single(ench!.Enchantments);
        Assert.Equal(Identifier.Minecraft("sharpness"), instance.Enchantment.Id);
        Assert.Equal(5, instance.Level);
        Assert.Equal(frame, bound.Encode(packet));

        // The value reaches its EFFECT, not just its variable: the model-level enchantments accessor used by the client and legacy NBT bridge contains it.
        Assert.Equal(5, Assert.Single(packet.Item.Enchantments).Level);
    }

    /// <summary>An enchanted BOOK carries <c>stored_enchantments</c>, which was untyped on 766-769 too.</summary>
    [Theory]
    [MemberData(nameof(StoredEnchantmentsWireIds))]
    public void EnchantedBook_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(
            protocol,
            slot: 1,
            itemId: ItemTestRegistries.Stone,
            componentWireId: wireId,
            write: (ref PacketWriter w) => WriteSampleEnchantments(ref w, protocol < 770));

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(
            DataComponents.StoredEnchantments, out StoredEnchantmentsComponent? stored));
        Assert.Equal(5, Assert.Single(stored!.Enchantments).Level);
        Assert.Equal(frame, bound.Encode(packet));
    }

    // Codec selection: frame LENGTH and wire-id bytes, which a round trip cannot expose.

    /// <summary>The 1.21.5 boundary on <c>enchantments</c>, asserted as a payload width. Protocols 768/769 end with a <c>showInTooltip</c> boolean; protocol 770 carries the map alone. The component's wire id is 10 on 768, 769 and 770, so the trailing byte is the ONLY thing separating the two codecs on that triple.</summary>
    [Theory]
    [InlineData(766, 1)]
    [InlineData(767, 1)]
    [InlineData(768, 1)]
    [InlineData(769, 1)]
    [InlineData(770, 0)]
    [InlineData(771, 0)]
    [InlineData(773, 0)]
    [InlineData(774, 0)]
    [InlineData(776, 0)]
    public void Enchantments_ShowInTooltipBoolIsTheProtocolsOwn(int protocol, int tooltipBytes)
    {
        byte[] frame = EncodeSlot(protocol, OneEnchantment);

        // map = VarInt count(1) + VarInt holder id(1) + VarInt level(1).
        Assert.Equal(HeaderBytes + 1 + 3 + tooltipBytes, frame.Length);
    }

    /// <summary>The pre-1.21.5 bool is not merely written, it is ROUND-TRIPPED: a server that suppresses the enchantment tooltip must re-encode as suppressed. A codec that consumed the byte and dropped the value would still be frame-exact for the default and would fail only here.</summary>
    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public void Enchantments_ShowInTooltipFalse_RoundTripsOn766To769(int protocol)
    {
        var hidden = new EnchantmentsComponent([Sharpness5], ShowInTooltip: false);
        byte[] frame = EncodeSlot(protocol, DataComponentMap.Empty.With(DataComponents.Enchantments, hidden));

        // The last payload byte IS the flag.
        Assert.Equal(0, frame[^1]);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));
        Assert.True(packet.Item.Components.TryGet(DataComponents.Enchantments, out EnchantmentsComponent? decoded));
        Assert.False(decoded!.ShowInTooltip);
        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>Cross-era rejection in BOTH directions on the ONE pair that shares a wire id (768/769 and 770 all call <c>enchantments</c> id 10). A 768 frame read under 770 leaves the tooltip byte unread and the frame-exact entry point rejects it; a 770 frame read under 768 runs off the end looking for it. This is the assertion that would catch the codec being bound one protocol early or late, which is exactly the shape that has bitten this project repeatedly.</summary>
    [Fact]
    public void EnchantmentsFrames_DoNotCrossThe770Boundary()
    {
        byte[] legacyFrame = EncodeSlot(768, OneEnchantment);
        byte[] modernFrame = EncodeSlot(770, OneEnchantment);

        // Same component wire id on both eras: nothing but the payload width differs.
        Assert.Equal(10, legacyFrame[HeaderBytes]);
        Assert.Equal(10, modernFrame[HeaderBytes]);
        Assert.Equal(legacyFrame.Length - 1, modernFrame.Length);

        Assert.ThrowsAny<Exception>(() => BoundCodec.At(770, PacketFlow.Clientbound, SetSlot).DecodeFrame(legacyFrame));
        Assert.ThrowsAny<Exception>(() => BoundCodec.At(768, PacketFlow.Clientbound, SetSlot).DecodeFrame(modernFrame));
    }

    /// <summary>The firework pair has no wire-shape variant from 1.20.6 through 26.2, so what can move is the wire id, and it moves on six of the ten eras. This pins the id byte and the exact payload width, so binding the pair through a neighbouring era's ordering fails here.</summary>
    [Theory]
    [MemberData(nameof(FireworksWireIds))]
    public void Fireworks_WireIdAndPayloadWidthAreTheProtocolsOwn(int protocol, int expectedWireId)
    {
        byte[] frame = EncodeSlot(protocol, OneFirework);

        Assert.Equal(expectedWireId, frame[HeaderBytes]);

        // VarInt flightDuration(1) + VarInt explosion count(1)
        //   + [VarInt shape(1) + VarInt colors(1) + 2*int(8) + VarInt fades(1) + 1*int(4) + bool + bool]
        Assert.Equal(HeaderBytes + 1 + 1 + 1 + 17, frame.Length);
    }

    /// <summary>A 776 firework frame must not decode under 770. 26.2 writes wire id 69, which the 1.21.5 ordering calls <c>minecraft:lock</c>, so the wrong table dispatches a completely different codec. Only the era table can prevent this; a round trip through the wrong table would agree with itself.</summary>
    [Fact]
    public void FireworkFrameFrom776_DoesNotDecodeUnder770()
    {
        byte[] frame = EncodeSlot(776, OneFirework);

        Assert.Equal(69, frame[HeaderBytes]);
        Assert.Equal(Identifier.Minecraft("lock"), ItemComponentTable.V1_21_5().KeyByWireId(69));
        Assert.ThrowsAny<Exception>(() => BoundCodec.At(770, PacketFlow.Clientbound, SetSlot).DecodeFrame(frame));
    }

    // The DATA (hash) form, which the wire round trip cannot see at all.

    /// <summary>The 1.21.5+ hashed slot hashes a component's DATA form, and for <c>enchantments</c> that form is the bare map with no wrapper; the <c>{ levels: .. }</c> wrapper is the PRE-1.21.5 full form and a hashed stack does not exist below 1.21.5. UMPK used the wrapper, so every container click on an enchanted item sent a hash the server could not match.</summary>
    [Fact]
    public void EnchantmentsHash_IsTheBareMap_Not1_21_4sLevelsWrapper()
    {
        var ops = new HashOps();
        var stack = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1, OneEnchantment);

        HashedStackValue hashed = ItemStackCodecs.ToHashedStack(
            stack, ItemStackCodecs.ComponentsV1_21_5, ItemTestRegistries.Context);

        (int componentId, int hash) = Assert.Single(hashed.AddedComponentHashes);
        Assert.Equal(10, componentId);

        int bareMap = ops.Map([(ops.String("minecraft:sharpness"), ops.Int(5))]);
        Assert.Equal(bareMap, hash);

        // This explicit negative assertion names the incompatible 1.21.4 FULL_CODEC wrapper, so a failure identifies the selected alternative rather than only reporting that two integers differ.
        int levelsWrapper = ops.Map([(ops.String("levels"), bareMap)]);
        Assert.NotEqual(levelsWrapper, hash);
    }

    /// <summary>The firework DATA form, rebuilt independently of the production codec: <c>flight_duration</c> hashes as a BYTE and not an int; the colour fields are LISTS of ints and not int ARRAYS; and every optional field is omitted at its default. Binding the codec makes this reachable for the first time, so it has to be right or a container click on a firework desyncs the slot.</summary>
    [Fact]
    public void FireworksHash_MatchesTheVanillaDataForm()
    {
        var ops = new HashOps();
        var stack = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1, OneFirework);

        HashedStackValue hashed = ItemStackCodecs.ToHashedStack(
            stack, ItemStackCodecs.ComponentsV1_21_5, ItemTestRegistries.Context);

        (int componentId, int hash) = Assert.Single(hashed.AddedComponentHashes);
        Assert.Equal(60, componentId);

        int explosion = ops.Map(
        [
            (ops.String("shape"), ops.String("star")),
            (ops.String("colors"), ops.List([ops.Int(0xFF0000), ops.Int(0x00FF00)])),
            (ops.String("fade_colors"), ops.List([ops.Int(0x0000FF)])),
            (ops.String("has_trail"), ops.Boolean(true)),
        ]);
        int expected = ops.Map(
        [
            (ops.String("flight_duration"), ops.Byte(2)),
            (ops.String("explosions"), ops.List([explosion])),
        ]);

        Assert.Equal(expected, hash);

        // Implied by the Assert.Equal above, kept deliberately as documentation: it states the one alternative shape a reader is most likely to assume (writing has_twinkle as an explicit false) and shows it is a different hash. FireworksHash_OmitsEachFieldAtItsDefault is what actually pins each omission branch.
        int withTwinkle = ops.Map(
        [
            (ops.String("shape"), ops.String("star")),
            (ops.String("colors"), ops.List([ops.Int(0xFF0000), ops.Int(0x00FF00)])),
            (ops.String("fade_colors"), ops.List([ops.Int(0x0000FF)])),
            (ops.String("has_trail"), ops.Boolean(true)),
            (ops.String("has_twinkle"), ops.Boolean(false)),
        ]);
        Assert.NotEqual(withTwinkle, explosion);
    }

    /// <summary>A firework explosion has its own hash form. A wrong hash on 1.21.5+ causes a full slot resync when a firework star is clicked.</summary>
    [Fact]
    public void FireworkExplosionHash_MatchesTheVanillaDataForm()
    {
        var ops = new HashOps();
        var stack = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.Stone), 1, OneFireworkStar);

        HashedStackValue hashed = ItemStackCodecs.ToHashedStack(
            stack, ItemStackCodecs.ComponentsV1_21_5, ItemTestRegistries.Context);

        (int componentId, int hash) = Assert.Single(hashed.AddedComponentHashes);
        Assert.Equal(59, componentId); // minecraft:firework_explosion on 770

        int expected = ops.Map(
        [
            (ops.String("shape"), ops.String("creeper")),
            (ops.String("colors"), ops.List([ops.Int(0x123456)])),
            (ops.String("has_twinkle"), ops.Boolean(true)),
        ]);
        Assert.Equal(expected, hash);

        // The exact mutation this test exists to kill: a body replaced by ops.Empty.
        Assert.NotEqual(ops.Empty, hash);
    }

    /// <summary>Every optional firework field OMITS the entry at its default. One fully populated sample pins none of those branches, so each is exercised here with a value chosen to make exactly one field default. Each expectation is rebuilt from the vanilla data shape rather than read back from the codec.</summary>
    /// <remarks>The empty-explosions case is not a corner: a plain elytra-boost rocket (paper plus gunpowder, no star) is exactly <c>fireworks={flight_duration:N}</c> with an EMPTY explosions list, and it is by far the most-handled firework in the game.</remarks>
    [Theory]
    [InlineData("elytra_rocket")]
    [InlineData("zero_flight_duration")]
    [InlineData("no_colors")]
    [InlineData("no_trail")]
    public void FireworksHash_OmitsEachFieldAtItsDefault(string shape)
    {
        var ops = new HashOps();

        (FireworksComponent value, int expected) = shape switch
        {
            // explosions omitted when empty: the map carries flight_duration and nothing else.
            "elytra_rocket" => (
                new FireworksComponent(3, []),
                ops.Map([(ops.String("flight_duration"), ops.Byte(3))])),

            // flight_duration omitted at 0: the map carries explosions and nothing else.
            "zero_flight_duration" => (
                new FireworksComponent(0, [new FireworkExplosion("burst", [], [], HasTrail: true)]),
                ops.Map(
                [
                    (ops.String("explosions"), ops.List(
                    [
                        ops.Map(
                        [
                            (ops.String("shape"), ops.String("burst")),
                            (ops.String("has_trail"), ops.Boolean(true)),
                        ]),
                    ])),
                ])),

            // colors AND fade_colors omitted when empty.
            "no_colors" => (
                new FireworksComponent(1, [new FireworkExplosion("large_ball", [], [], HasTwinkle: true)]),
                ops.Map(
                [
                    (ops.String("flight_duration"), ops.Byte(1)),
                    (ops.String("explosions"), ops.List(
                    [
                        ops.Map(
                        [
                            (ops.String("shape"), ops.String("large_ball")),
                            (ops.String("has_twinkle"), ops.Boolean(true)),
                        ]),
                    ])),
                ])),

            // has_trail omitted when false, with everything around it present so only that branch moves.
            _ => (
                new FireworksComponent(2, [new FireworkExplosion("star", [7], [8], HasTrail: false, HasTwinkle: true)]),
                ops.Map(
                [
                    (ops.String("flight_duration"), ops.Byte(2)),
                    (ops.String("explosions"), ops.List(
                    [
                        ops.Map(
                        [
                            (ops.String("shape"), ops.String("star")),
                            (ops.String("colors"), ops.List([ops.Int(7)])),
                            (ops.String("fade_colors"), ops.List([ops.Int(8)])),
                            (ops.String("has_twinkle"), ops.Boolean(true)),
                        ]),
                    ])),
                ])),
        };

        var stack = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.Stone),
            1,
            DataComponentMap.Empty.With(DataComponents.Fireworks, value));

        HashedStackValue hashed = ItemStackCodecs.ToHashedStack(
            stack, ItemStackCodecs.ComponentsV1_21_5, ItemTestRegistries.Context);
        (_, int hash) = Assert.Single(hashed.AddedComponentHashes);

        Assert.Equal(expected, hash);
    }

    /// <summary>The elytra-boost rocket on the WIRE, on every era: an empty explosions list is a VarInt zero and must round-trip as one. The populated sample never exercises the empty list, and this is the single most common firework a session will actually carry.</summary>
    [Theory]
    [MemberData(nameof(FireworksWireIds))]
    public void PlainElytraRocket_RoundTripsOnEveryWireLayout(int protocol, int expectedWireId)
    {
        byte[] frame = EncodeSlot(
            protocol,
            DataComponentMap.Empty.With(DataComponents.Fireworks, new FireworksComponent(3, [])));

        Assert.Equal(expectedWireId, frame[HeaderBytes]);

        // VarInt flight duration (1) + VarInt explosion count 0 (1). Nothing else.
        Assert.Equal(HeaderBytes + 1 + 2, frame.Length);
        Assert.Equal(3, frame[HeaderBytes + 1]);
        Assert.Equal(0, frame[HeaderBytes + 2]);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));
        Assert.True(packet.Item.Components.TryGet(DataComponents.Fireworks, out FireworksComponent? decoded));
        Assert.Equal(3, decoded!.FlightDuration);
        Assert.Empty(decoded.Explosions);
        Assert.Equal(frame, bound.Encode(packet));
    }

    // The 256-explosion cap applies on BOTH sides.

    /// <summary>Decode side. The explosion list is capped at 256 entries and rejects a larger count. This frame is otherwise WELL FORMED: it declares 300 explosions and actually contains 300 of them, so the generic plausibility bound (count versus bytes remaining) passes cleanly and the cap is the only thing that can reject it. That is what makes this an assertion about the cap and not about the length guard.</summary>
    [Fact]
    public void FireworksDecode_RefusesMoreThan256Explosions()
    {
        byte[] frame = SetSlotFrame(
            770,
            slot: 0,
            itemId: ItemTestRegistries.DiamondSword,
            componentWireId: 60,
            write: static (ref PacketWriter w) =>
            {
                w.WriteVarInt(1);   // flight duration
                w.WriteVarInt(300); // ... and 300 real explosions follow, so the frame is self-consistent
                for (int i = 0; i < 300; i++)
                {
                    w.WriteVarInt(0); // small_ball
                    w.WriteVarInt(0); // no colors
                    w.WriteVarInt(0); // no fade colors
                    w.WriteBool(false);
                    w.WriteBool(false);
                }
            });

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, SetSlot);

        var ex = Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame(frame));
        Assert.Contains("max size of: 256", ex.Message, StringComparison.Ordinal);

        // 256 exactly is legal and must still decode, so the cap is a boundary and not an approximation.
        byte[] atTheCap = SetSlotFrame(
            770,
            slot: 0,
            itemId: ItemTestRegistries.DiamondSword,
            componentWireId: 60,
            write: static (ref PacketWriter w) =>
            {
                w.WriteVarInt(1);
                w.WriteVarInt(256);
                for (int i = 0; i < 256; i++)
                {
                    w.WriteVarInt(0);
                    w.WriteVarInt(0);
                    w.WriteVarInt(0);
                    w.WriteBool(false);
                    w.WriteBool(false);
                }
            });

        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(atTheCap));
        Assert.True(packet.Item.Components.TryGet(DataComponents.Fireworks, out FireworksComponent? ok));
        Assert.Equal(256, ok!.Explosions.Count);
    }

    /// <summary>Decode side, the allocation case. A hostile count is refused by the CAP and not merely by the plausibility bound, so nothing sized from the claimed count is ever allocated. The frame here is tiny, so a reader that checked plausibility first would report a different message; asserting the message is what proves the cap fired first.</summary>
    [Fact]
    public void FireworksDecode_RefusesAHostileCountBeforeAllocating()
    {
        byte[] frame = SetSlotFrame(
            770,
            slot: 0,
            itemId: ItemTestRegistries.DiamondSword,
            componentWireId: 60,
            write: static (ref PacketWriter w) =>
            {
                w.WriteVarInt(1);
                w.WriteVarInt(500_000); // and then nothing at all
            });

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, SetSlot);

        var ex = Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame(frame));
        Assert.Contains("500000 elements exceeded max size of: 256", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>Encode side, which matters just as much. The same 256-entry cap applies when writing. Without this, UMPK would happily emit 300 explosions in a creative-slot set, the server would reject it while reading, and a locally recoverable mistake would become a remote disconnect. Refusing before a byte is written is the honest failure.</summary>
    [Fact]
    public void FireworksEncode_RefusesToEmitMoreThan256Explosions()
    {
        var tooMany = new FireworksComponent(
            1, [.. Enumerable.Repeat(new FireworkExplosion("small_ball", [], []), 300)]);

        var ex = Assert.Throws<ProtocolViolationException>(
            () => EncodeSlot(770, DataComponentMap.Empty.With(DataComponents.Fireworks, tooMany)));
        Assert.Contains("300 elements exceeded max size of: 256", ex.Message, StringComparison.Ordinal);

        // 256 encodes, so the refusal is the boundary vanilla draws and not a stricter one of our own.
        var atTheCap = new FireworksComponent(
            1, [.. Enumerable.Repeat(new FireworkExplosion("small_ball", [], []), 256)]);
        byte[] frame = EncodeSlot(770, DataComponentMap.Empty.With(DataComponents.Fireworks, atTheCap));
        Assert.Equal(HeaderBytes + 1 + 1 + 2 + (256 * 5), frame.Length);
    }

    // End to end at the connection: the packet is DELIVERED, not merely decodable.

    /// <summary>The read-loop assertion. A second frame follows the firework frame to prove both delivery and ordering.</summary>
    [Fact]
    public async Task FireworkRocketFrame_IsDeliveredByTheReadLoop()
    {
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Throw,
            ReadIdleTimeout = TimeSpan.Zero,
        });

        var builder = new ProtocolDescriptorBuilder(
            new GameVersion(GameEdition.Java, "1.21.5", 770), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, SetSlotWireId, SetSlot);
        conn.BindCodec(new DescriptorFrameCodecBinding(builder.Build(), ItemTestRegistries.Context), PacketFlow.Clientbound);
        conn.SetPhase(ProtocolPhase.Play);
        conn.Start();

        byte[] rocket = SetSlotFrame(
            770, slot: 7, itemId: ItemTestRegistries.DiamondSword, componentWireId: 60, write: WriteSampleFireworks);
        byte[] plain = SetSlotFrame(770, slot: 8, itemId: ItemTestRegistries.Stone, componentWireId: null, write: null);

        await FramingTests.WriteRawFrameAsync(pair.Right.Output, WithWireId(rocket));
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, WithWireId(plain));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        InboundItem first = await conn.ReceiveAsync(cts.Token);
        var firstPacket = Assert.IsType<ClientboundContainerSetSlotPacket>(first.Packet);
        Assert.Equal(7, firstPacket.Slot);
        Assert.True(firstPacket.Item.Components.TryGet(DataComponents.Fireworks, out FireworksComponent? fireworks));
        Assert.Equal(2, fireworks!.FlightDuration);

        InboundItem second = await conn.ReceiveAsync(cts.Token);
        Assert.Equal(8, Assert.IsType<ClientboundContainerSetSlotPacket>(second.Packet).Slot);
    }

    /// <summary>This guard preserves packet-scoped failure for a component the era declares but UMPK does not model (<c>minecraft:weapon</c>, wire id 26 on 770) must STILL cost the packet, because the compact patch carries no length prefix and there is genuinely nothing to skip. Typing more components does not turn the recovery into a catch-all.</summary>
    /// <remarks><c>minecraft:weapon</c> is intentionally unmodeled in every era, so it remains a stable probe.</remarks>
    [Fact]
    public void AStillUnmodeledComponent_StillRaisesThePacketScopedFault()
    {
        byte[] frame = SetSlotFrame(
            770,
            slot: 0,
            itemId: ItemTestRegistries.DiamondSword,
            componentWireId: 26,
            write: static (ref PacketWriter w) => w.WriteVarInt(0));

        BoundPacketCodec bound = BoundCodec.At(770, PacketFlow.Clientbound, SetSlot);
        var ex = Assert.Throws<UnmodeledItemComponentException>(() => bound.DecodeFrame(frame));
        Assert.Equal(Identifier.Minecraft("weapon"), ex.ComponentId);
    }

    // The size of the remaining class, pinned so it can only shrink knowingly.

    /// <summary>How many components each era DECLARES versus how many UMPK types. Every untyped one is a packet-dropper the moment it appears on a compact patch, so this number is the real blast radius and it belongs in the suite rather than in a report that goes stale. Measured by walking each table's wire ids until <see cref="ProtocolViolationException"/> and counting the ids with no codec.</summary>
    /// <remarks>
    /// Typing <c>enchantments</c>, <c>stored_enchantments</c>, <c>fireworks</c> and <c>firework_explosion</c> moved these from 18/19/25/25/40/40/40/47/51/51. Typing <c>pot_decorations</c> on <c>BuildPreV1_21_5</c> then moved 766/767/768/769 down by one more each (14/15/21/21 -&gt; 13/14/20/20).
    /// <para>The current numbers come from typing the fifteen components that had no codec on ANY protocol (<c>banner_patterns, bees, can_break, can_place_on, consumable, damage_resistant, death_protection, enchantable, equippable, instrument, jukebox_playable, lodestone_tracker, repairable, suspicious_stew_effects, use_cooldown</c>), moving the untyped counts <c>13/14/20/20/38/38/38/45/49/49</c> to <c>6/6/5/5/25/25/25/32/36/36</c> for 766/767/768/769/770/771/773/774/775/776. The per-era deltas differ because the eras declare different subsets: 766 gains 7 (<c>banner_patterns, bees, can_break, can_place_on, instrument, lodestone_tracker, suspicious_stew_effects</c>), 767 gains those 8 with <c>jukebox_playable</c>, 768/769 gain all 15, and 770-776 gain 13 - everything except <c>can_break</c> and <c>can_place_on</c>, which stay untyped there because 1.21.5 appended <c>DataComponentMatchers</c> to <c>BlockPredicate</c> (see <c>ItemComponentCodecs.AdventureModePredicateCodecImpl</c>'s remarks).</para>
    /// <para>What is still untyped is now dominated by 1.21.5+ additions no normal session needs to decode (<c>weapon</c>, <c>blocks_attacks</c>, the mob <c>*/variant</c> family, <c>break_sound</c>), plus <c>can_break</c>/<c>can_place_on</c> on 770+, and on 766-769 the six components whose pre-1.21.5 wire shape genuinely differs (<c>attribute_modifiers</c>, <c>dyed_color</c>, <c>potion_contents</c>, <c>trim</c>, <c>tool</c>, and on 766/767 <c>food</c>). A drop in a number here without a corresponding new codec means the table lost entries, which is the opposite of progress.</para>
    /// </remarks>
    [Theory]
    [InlineData(766, 56, 6)]
    [InlineData(767, 57, 6)]
    [InlineData(768, 67, 5)]
    [InlineData(769, 67, 5)]
    [InlineData(770, 96, 25)]
    [InlineData(771, 96, 25)]
    [InlineData(773, 96, 25)]
    [InlineData(774, 104, 32)]
    [InlineData(775, 110, 36)]
    [InlineData(776, 111, 36)]
    public void WireLayoutTable_DeclaresAndTypesTheseManyComponents(int protocol, int declared, int untyped)
    {
        ItemComponentTable table = TableFor(protocol);

        int seen = 0;
        int withoutCodec = 0;
        while (true)
        {
            try
            {
                _ = table.KeyByWireId(seen);
            }
            catch (ProtocolViolationException)
            {
                break;
            }

            if (!table.TryGetCodec(seen, out _))
                withoutCodec++;

            seen++;
        }

        Assert.Equal(declared, seen);
        Assert.Equal(untyped, withoutCodec);
    }

    private static ItemComponentTable TableFor(int protocol) => protocol switch
    {
        766 => ItemComponentTable.V1_20_5(),
        767 => ItemComponentTable.V1_21(),
        768 => ItemComponentTable.V1_21_2(),
        769 => ItemComponentTable.V1_21_4(),
        770 => ItemComponentTable.V1_21_5(),
        771 => ItemComponentTable.V1_21_6(),
        773 => ItemComponentTable.V1_21_9(),
        774 => ItemComponentTable.V1_21_11(),
        775 => ItemComponentTable.V26_1(),
        _ => ItemComponentTable.V26_2(),
    };

    // Helpers.

    private const int SetSlotWireId = 0x14; // minecraft:container_set_slot on 770

    private static EnchantmentInstance Sharpness5 { get; } = new(
        ItemTestRegistries.Enchantment(ItemTestRegistries.Sharpness), 5);

    private static DataComponentMap OneEnchantment { get; } =
        DataComponentMap.Empty.With(DataComponents.Enchantments, new EnchantmentsComponent([Sharpness5]));

    /// <summary>A firework STAR sample: a different shape and a different optional set from the rocket, so a codec that hashed one through the other's shape would not match.</summary>
    private static DataComponentMap OneFireworkStar { get; } = DataComponentMap.Empty.With(
        DataComponents.FireworkExplosion,
        new FireworkExplosionComponent(new FireworkExplosion("creeper", [0x123456], [], HasTwinkle: true)));

    private static DataComponentMap OneFirework { get; } = DataComponentMap.Empty.With(
        DataComponents.Fireworks,
        new FireworksComponent(2, [new FireworkExplosion("star", [0xFF0000, 0x00FF00], [0x0000FF], HasTrail: true)]));

    private static byte[] EncodeSlot(int protocol, DataComponentMap components) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot).Encode(
            new ClientboundContainerSetSlotPacket(
                ContainerId: 0,
                StateId: 1,
                Slot: 0,
                Item: new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1, components)));

    private delegate void PayloadWriter(ref PacketWriter writer);

    private static void WriteSampleFireworks(ref PacketWriter w)
    {
        w.WriteVarInt(2);   // flight duration
        w.WriteVarInt(1);   // one explosion
        WriteSampleExplosion(ref w);
    }

    private static void WriteSampleExplosion(ref PacketWriter w)
    {
        w.WriteVarInt(2);          // shape: star
        w.WriteVarInt(2);          // two colors
        w.WriteInt(0xFF0000);
        w.WriteInt(0x00FF00);
        w.WriteVarInt(1);          // one fade color
        w.WriteInt(0x0000FF);
        w.WriteBool(true);         // has trail
        w.WriteBool(false);        // has twinkle
    }

    private static void WriteSampleEnchantments(ref PacketWriter w, bool withTooltipFlag)
    {
        w.WriteVarInt(1);                            // one entry
        w.WriteVarInt(ItemTestRegistries.Sharpness); // enchantment holder id
        w.WriteVarInt(5);                            // level
        if (withTooltipFlag)
            w.WriteBool(true);

    }

    /// <summary>Composes a container_set_slot body at the FIELD level, so the component bytes are exactly what a server would put on the wire rather than whatever the codec under test produces. 766/767 write a signed byte container id; 768+ write a VarInt. Both are one byte for container 0.</summary>
    private static byte[] SetSlotFrame(int protocol, int slot, int itemId, int? componentWireId, PayloadWriter? write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        if (protocol < 768)
            w.WriteByte(0);

        else
            w.WriteVarInt(0);

        w.WriteVarInt(1);           // state id
        w.WriteShort((short)slot);
        w.WriteVarInt(1);           // count
        w.WriteVarInt(itemId);
        w.WriteVarInt(componentWireId is null ? 0 : 1); // added
        w.WriteVarInt(0);                               // removed
        if (componentWireId is int id && write is not null)
        {
            w.WriteVarInt(id);
            write(ref w);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] WithWireId(byte[] body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(SetSlotWireId);
        w.WriteBytes(body);
        return buffer.WrittenSpan.ToArray();
    }
}
