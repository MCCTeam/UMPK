using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Era coverage for <c>minecraft:set_creative_mode_slot</c> and <c>minecraft:merchant_offers</c>. Protocols 477-763 use pre-component item stacks and must not select the 1.21.5 structured-component codecs.</summary>
/// <remarks>
/// Two facts drive the shape of every test here, both proven twice in this codebase:
/// <list type="number">
/// <item>An EMPTY or plain item stack decodes IDENTICALLY under the right and the wrong framing (an
/// empty present-id stack and an empty component stack are both a single 0x00 byte; a null NBT tag is a bare TAG_End byte under both root flavors). Every stack below therefore carries real NBT, and every merchant offer below carries a SECOND input cost, which is the only field that separates the two pre-component trade-list forms.</item>
/// <item>A pure round trip (encode then decode through the SAME codec) is self-consistent under either
/// framing and cannot detect an era misbinding at all. Every era below is pinned by a FRAME DECODE of hand-authored expected bytes, plus a differential assertion against the adjacent era.</item>
/// </list>
/// </remarks>
public class TradeAndCreativeInventoryCodecTests
{
    // NBT payload, authored from the named-root wire form used through 1.20.1.

    /// <summary>A custom-named item: <c>{display:{Name:"Bob"}}</c>.</summary>
    private static NbtCompound CustomName()
    {
        var display = new NbtCompound();
        display.PutString("Name", "Bob");
        var root = new NbtCompound();
        root.Put("display", display);
        return root;
    }

    //   0A                 TAG_Compound, the root type
    //   00 00              the empty ROOT NAME string (absent from the 1.20.2+ unnamed root)
    //   0A 00 07 display   TAG_Compound member "display"
    //   08 00 04 Name      TAG_String member "Name"
    //   00 03 Bob          its value
    //   00 00              TAG_End of "display", then TAG_End of the root
    private static readonly byte[] CustomNameNamedRoot =
    [
        0x0A,
        0x00, 0x00,
        0x0A, 0x00, 0x07, .. "display"u8,
        0x08, 0x00, 0x04, .. "Name"u8,
        0x00, 0x03, .. "Bob"u8,
        0x00,
        0x00,
    ];

    private static readonly byte[] CustomNameUnnamedRoot =
    [
        0x0A,
        0x0A, 0x00, 0x07, .. "display"u8,
        0x08, 0x00, 0x04, .. "Name"u8,
        0x00, 0x03, .. "Bob"u8,
        0x00,
        0x00,
    ];

    // A present-id stack (1.13.2-1.20.1: present bool, VarInt id, byte count, NAMED-root optional NBT;
    // Stone (id 1), count 1, carrying CustomName().
    private static readonly byte[] NamedStoneStack = [0x01, 0x01, 0x01, .. CustomNameNamedRoot];

    // The same stack under the 1.20.2+ unnamed network root: identical but two bytes shorter.
    private static readonly byte[] UnnamedStoneStack = [0x01, 0x01, 0x01, .. CustomNameUnnamedRoot];

    // A plain present-id stack with NO tag: present, id, count, bare TAG_End.
    private static byte[] PlainStack(byte id, byte count) => [0x01, id, count, 0x00];

    // Helpers.

    private static T DecodeExact<T>(PacketCodec<T> codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        T decoded = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    /// <summary>Asserts a codec cannot consume a frame it was never meant to see: it either throws or stops short of the end. This is the shape a live session experiences as a fault.</summary>
    private static void AssertCannotFrame<T>(PacketCodec<T> codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        try
        {
            codec.Decode(ref reader, ItemTestRegistries.Context);
        }
        catch (Exception)
        {
            return;
        }

        Assert.True(reader.Remaining != 0, "the wrong-era codec consumed the frame exactly, which the test relies on being impossible");
    }

    private static ItemStack WithNbt(int itemId, int count, NbtCompound tag) =>
        new(ItemTestRegistries.Item(itemId), count,
            DataComponentMap.Empty.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));

    private static ItemStack Plain(int itemId, int count) => new(ItemTestRegistries.Item(itemId), count);

    private static void AssertIsCustomName(ItemStack stack)
    {
        Assert.True(stack.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy));
        Assert.True(legacy!.Nbt.TryGet("display", out NbtCompound? display));
        Assert.Equal("Bob", display!.GetString("Name"));
    }

    // set_creative_mode_slot set_creative_mode_slot reads a short slot followed by the item. The fields never change on the pre-component range; only the stack form underneath does.

    [Fact]
    public void CreativeSlot_1_16_5_NbtCarryingStack_DecodesExactly_AndReEncodes()
    {
        byte[] wire = [0x00, 0x24, .. NamedStoneStack];   // slot 36, then the named-root stack
        var decoded = DecodeExact(ContainerCodecs.CreativeSlotV1_14, wire);

        Assert.Equal(36, decoded.Slot);
        Assert.Equal(ItemTestRegistries.Stone, decoded.Item.Item.NetworkId);
        Assert.Equal(1, decoded.Item.Count);
        AssertIsCustomName(decoded.Item);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlotV1_14, decoded));
    }

    // The direct regression assertion: the 1.21.5 codec must not be bound to cannot frame a real 1.16.5 creative give.
    [Fact]
    public void CreativeSlot_1_16_5_Frame_UnderTheOld770Codec_CannotBeFramed()
    {
        byte[] wire = [0x00, 0x24, .. NamedStoneStack];
        AssertCannotFrame(ContainerCodecs.CreativeSlot770, wire);
    }

    // The other direction is worse than a fault, and is worth pinning: a 1.21.5 give of a plain item decodes CLEANLY under the pre-component reader, just wrong. Component form: VarInt count 1, holder id 2, empty patch (0 added, 0 removed). Present-id form reads the same bytes as present=true, id 2, count 0, empty tag - same item, silently zero count, no error anywhere. Frame-exactness alone is not enough to detect an era misbinding; that is why every other case here is byte-pinned.
    [Fact]
    public void CreativeSlot_ComponentFrame_UnderTheV1_14Codec_DecodesSilentlyWrong()
    {
        byte[] componentWire = [0x00, 0x24, 0x01, 0x02, 0x00, 0x00];

        var asComponent = DecodeExact(ContainerCodecs.CreativeSlot770, componentWire);
        Assert.Equal(ItemTestRegistries.DiamondSword, asComponent.Item.Item.NetworkId);
        Assert.Equal(1, asComponent.Item.Count);

        var asPresentId = DecodeExact(ContainerCodecs.CreativeSlotV1_14, componentWire);
        Assert.Equal(ItemTestRegistries.DiamondSword, asPresentId.Item.Item.NetworkId);
        Assert.Equal(0, asPresentId.Item.Count);
    }

    // Why nothing caught this for so long: with an EMPTY stack the two eras are byte-identical (the present-id form writes present=false, the component form writes count=0; both are a single 0x00).
    [Fact]
    public void CreativeSlot_EmptyStack_IsByteIdenticalAcrossTheWireLayouts()
    {
        var empty = new ServerboundSetCreativeModeSlotPacket(Slot: 36, Item: ItemStack.Empty);
        byte[] pre = ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlotV1_14, empty);
        byte[] component = ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlot770, empty);

        Assert.Equal<byte[]>([0x00, 0x24, 0x00], pre);
        Assert.Equal(pre, component);
    }

    // 1.20.2 dropped the NBT root name, so the SAME logical give is two bytes shorter there. This guards the 763/764 split from being collapsed onto one flavor.
    [Fact]
    public void CreativeSlot_1_20_2_UsesUnnamedRoot_AndDiffersFromTheNamedBand()
    {
        byte[] wire = [0x00, 0x24, .. UnnamedStoneStack];
        var decoded = DecodeExact(ContainerCodecs.CreativeSlotV1_20_2, wire);

        AssertIsCustomName(decoded.Item);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlotV1_20_2, decoded));
        Assert.Equal(wire.Length + 2, 2 + NamedStoneStack.Length);
        Assert.NotEqual(
            ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlotV1_14, decoded),
            ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlotV1_20_2, decoded));
    }

    [Fact]
    public void CreativeSlot_1_16_5_NbtCarryingStack_RoundTripsByteStable()
    {
        var give = new ServerboundSetCreativeModeSlotPacket(
            Slot: 36, Item: WithNbt(ItemTestRegistries.DiamondSword, 1, CustomName()));

        AssertIsCustomName(ItemCodecRoundTrip.Cycle(ContainerCodecs.CreativeSlotV1_14, give).Item);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.CreativeSlotV1_14, give);

        // The encoded stack must carry the empty root name; without it the server sees a truncated tag.
        byte[] encoded = ItemCodecRoundTrip.Encode(ContainerCodecs.CreativeSlotV1_14, give);
        // slot short(2) + present(1) + id(1) + count(1) = 5, then the tag.
        Assert.Equal(0x0A, encoded[5]);
        Assert.Equal(0x00, encoded[6]);
        Assert.Equal(0x00, encoded[7]);
    }

    // merchant_offers merchant_offers is constant from 1.14.4 through 1.20.4:
    //   VarInt containerId; offers; VarInt villagerLevel; VarInt villagerXp; boolean showProgress;
    //   boolean canRestock. The payload has two pre-component forms.

    // One trade, authored field by field. Shared tail (everything after the second input cost):
    //   00                     isOutOfStock boolean -> uses(0) < maxUses(12)
    //   00 00 00 00            uses int      = 0
    //   00 00 00 0C            maxUses int   = 12
    //   00 00 00 02            xp int        = 2
    //   00 00 00 00            specialPriceDiff int = 0
    //   3D 4C CC CD            priceMultiplier float = 0.05f
    // The 1.14-1.14.3 offer ENDS here. 1.14.4 (protocol 498) appends:
    //   00 00 00 00            demand int    = 0
    private static readonly byte[] OfferTailNoDemand =
    [
        0x00,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x0C,
        0x00, 0x00, 0x00, 0x02,
        0x00, 0x00, 0x00, 0x00,
        0x3D, 0x4C, 0xCC, 0xCD,
    ];

    private static readonly byte[] OfferTail = [.. OfferTailNoDemand, 0x00, 0x00, 0x00, 0x00];

    // The merchant-level tail: VarInt villagerLevel 1, VarInt villagerXp 0, showProgress, canRestock.
    private static readonly byte[] MerchantTail = [0x01, 0x00, 0x01, 0x01];

    // 477-485 (1.14, 1.14.1, 1.14.2) STOP one bool earlier: the packet has no canRestock field at all. Protocols 477-485 write one trailing boolean; 490 and later write two.
    private static readonly byte[] MerchantTailNoRestock = [0x01, 0x00, 0x01];

    // 498-758 (FORM A): size masked to one byte, and the second cost is a present bool + OPTIONAL stack.
    private static byte[] FormAWire() =>
    [
        0x03,                                    // VarInt containerId
        0x01,                                    // BYTE offer count
        .. PlainStack(0x01, 0x02),               // writeItem(baseCostA): 2 stone
        .. NamedStoneStack,                      // writeItem(result): a custom-named stone
        0x01,                                    // writeBoolean(!costB.isEmpty())
        .. PlainStack(0x01, 0x01),               // writeItem(costB): 1 stone
        .. OfferTail,
        .. MerchantTail,
    ];

    // 490 (1.14.3): FORM A minus the trailing demand int, which 1.14.4 added.
    private static byte[] FormA114Wire() =>
    [
        0x03,
        0x01,
        .. PlainStack(0x01, 0x02),
        .. NamedStoneStack,
        0x01,
        .. PlainStack(0x01, 0x01),
        .. OfferTailNoDemand,
        .. MerchantTail,
    ];

    // 477-485 (1.14-1.14.2): the 1.14.3 wire minus the packet's trailing canRestock bool.
    private static byte[] FormA114NoRestockWire() =>
    [
        0x03,
        0x01,
        .. PlainStack(0x01, 0x02),
        .. NamedStoneStack,
        0x01,
        .. PlainStack(0x01, 0x01),
        .. OfferTailNoDemand,
        .. MerchantTailNoRestock,
    ];

    // 759-763 (FORM B): VarInt collection count, and the second cost is an unconditional stack. Byte-for-byte FORM A minus the single present bool.
    private static byte[] FormBWire() =>
    [
        0x03,
        0x01,                                    // VarInt offer count (same single byte at count 1)
        .. PlainStack(0x01, 0x02),
        .. NamedStoneStack,
        .. PlainStack(0x01, 0x01),
        .. OfferTail,
        .. MerchantTail,
    ];

    private static void AssertDecodedOffer(ClientboundMerchantOffersPacket decoded) =>
        AssertDecodedOffer(decoded, expectCanRestock: true);

    private static void AssertDecodedOffer(ClientboundMerchantOffersPacket decoded, bool expectCanRestock)
    {
        Assert.Equal(3, decoded.ContainerId);
        MerchantOffer offer = Assert.Single(decoded.Offers.Offers);

        Assert.Equal(ItemTestRegistries.Stone, offer.BaseFirstCost.Item.NetworkId);
        Assert.Equal(2, offer.BaseFirstCost.Count);
        AssertIsCustomName(offer.Result);
        Assert.NotNull(offer.SecondCost);
        Assert.Equal(1, offer.SecondCost!.Count);

        Assert.False(offer.IsSoldOut);
        Assert.Equal(0, offer.Uses);
        Assert.Equal(12, offer.MaxUses);
        Assert.Equal(2, offer.Xp);
        Assert.Equal(0, offer.SpecialPrice);
        Assert.Equal(0.05f, offer.PriceMultiplier);
        Assert.Equal(0, offer.Demand);

        Assert.Equal(1, decoded.Offers.VillagerLevel);
        Assert.Equal(0, decoded.Offers.Experience);
        Assert.True(decoded.Offers.IsRegularVillager);
        Assert.Equal(expectCanRestock, decoded.Offers.CanRestock);
    }

    [Fact]
    public void MerchantOffers_1_16_5_DecodesExactly_AndReEncodes()
    {
        byte[] wire = FormAWire();
        AssertDecodedOffer(DecodeExact(MerchantCodecs.MerchantOffersV1_14_4, wire));
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(
            MerchantCodecs.MerchantOffersV1_14_4, DecodeExact(MerchantCodecs.MerchantOffersV1_14_4, wire)));
    }

    [Fact]
    public void MerchantOffers_1_14_3_NoDemandField_DecodesExactly_AndReEncodes()
    {
        byte[] wire = FormA114Wire();
        AssertDecodedOffer(DecodeExact(MerchantCodecs.MerchantOffersV1_14_3, wire));
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(
            MerchantCodecs.MerchantOffersV1_14_3, DecodeExact(MerchantCodecs.MerchantOffersV1_14_3, wire)));

        // 1.14.4 appended the demand int, so the same trade is four bytes longer there and neither member can frame the other's bytes.
        Assert.Equal(FormAWire().Length - 4, wire.Length);
        AssertCannotFrame(MerchantCodecs.MerchantOffersV1_14_4, wire);
        AssertCannotFrame(MerchantCodecs.MerchantOffersV1_14_3, FormAWire());
    }

    // Protocols 477-485 have neither the demand int nor the packet's canRestock bool. A 1.14.3 reader asks for one byte past the end of this frame, and the decode fault closes the connection.
    [Fact]
    public void MerchantOffers_1_14_NoCanRestockBool_DecodesExactly_AndReEncodes()
    {
        byte[] wire = FormA114NoRestockWire();
        AssertDecodedOffer(DecodeExact(MerchantCodecs.MerchantOffersV1_14, wire), expectCanRestock: false);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(
            MerchantCodecs.MerchantOffersV1_14, DecodeExact(MerchantCodecs.MerchantOffersV1_14, wire)));

        // Exactly one byte apart from the 1.14.3 frame, and the 1.14.3 reader cannot frame it: that over-read is the framing fault.
        Assert.Equal(FormA114Wire().Length - 1, wire.Length);
        AssertCannotFrame(MerchantCodecs.MerchantOffersV1_14_3, wire);
        AssertCannotFrame(MerchantCodecs.MerchantOffersV1_14_4, wire);
    }

    // This one-emerald-to-three-diamonds trade has no second cost. At protocol 485, item ids 759 and 529 are both two-byte VarInts, making the payload exactly 37 bytes.
    [Fact]
    public void MerchantOffers_1_14_2_LiveVillagerOpen_IsThirtySevenBytes_AndTheRestockReaderOverruns()
    {
        byte[] wire =
        [
            0x03,                             // VarInt containerId
            0x01,                             // BYTE offer count
            0x01, 0xF7, 0x05, 0x01, 0x00,     // baseCostA: present, VarInt 759 (emerald), count 1, TAG_End
            0x01, 0x91, 0x04, 0x03, 0x00,     // result:    present, VarInt 529 (diamond), count 3, TAG_End
            0x00,                             // writeBoolean(!costB.isEmpty()) -> no second cost
            .. OfferTailNoDemand,
            .. MerchantTailNoRestock,
        ];
        Assert.Equal(37, wire.Length);

        var decoded = DecodeExact(MerchantCodecs.MerchantOffersV1_14, wire);
        MerchantOffer offer = Assert.Single(decoded.Offers.Offers);
        Assert.Equal(ItemTestRegistries.Emerald1142, offer.BaseFirstCost.Item.NetworkId);
        Assert.Equal(ItemTestRegistries.Diamond1142, offer.Result.Item.NetworkId);
        Assert.Equal(3, offer.Result.Count);
        Assert.Null(offer.SecondCost);
        Assert.False(decoded.Offers.CanRestock);

        // The neighbouring binding reads one byte past the end of these bytes. Assert through the bound codec because it is the live entry point.
        AssertCannotFrame(MerchantCodecs.MerchantOffersV1_14_3, wire);
        object bound = BoundCodec.At(485, PacketFlow.Clientbound, MerchantOffersId).DecodeFrame(wire);
        Assert.False(Assert.IsType<ClientboundMerchantOffersPacket>(bound).Offers.CanRestock);
    }

    [Fact]
    public void MerchantOffers_1_19_DecodesExactly_AndReEncodes()
    {
        byte[] wire = FormBWire();
        AssertDecodedOffer(DecodeExact(MerchantCodecs.MerchantOffersV1_19, wire));
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(
            MerchantCodecs.MerchantOffersV1_19, DecodeExact(MerchantCodecs.MerchantOffersV1_19, wire)));
    }

    // The direct regression assertion for the misbinding: the 1.21.5 ItemCost codec the 477-763 band used to be bound to cannot frame a real villager open from any pre-component era.
    [Fact]
    public void MerchantOffers_PreComponentFrames_UnderTheOld770Codec_CannotBeFramed()
    {
        AssertCannotFrame(MerchantCodecs.MerchantOffers770, FormA114Wire());
        AssertCannotFrame(MerchantCodecs.MerchantOffers770, FormAWire());
        AssertCannotFrame(MerchantCodecs.MerchantOffers770, FormBWire());
    }

    // The two pre-component list forms are one byte apart, and each one mis-frames the other's bytes.
    [Fact]
    public void MerchantOffers_FormAAndFormB_DifferByTheSecondCostPresentBool()
    {
        byte[] formA = FormAWire();
        byte[] formB = FormBWire();
        Assert.Equal(formA.Length - 1, formB.Length);

        AssertCannotFrame(MerchantCodecs.MerchantOffersV1_19, formA);
        AssertCannotFrame(MerchantCodecs.MerchantOffersV1_14_4, formB);
    }

    // ...and the trap that hid it: with NO second cost the two forms are byte-identical, because form A's false present bool and form B's empty-item sentinel are the same single 0x00.
    [Fact]
    public void MerchantOffers_WithoutASecondCost_TheTwoFormsAreByteIdentical()
    {
        var packet = new ClientboundMerchantOffersPacket(
            ContainerId: 3,
            Offers: new MerchantOffers(
                [new MerchantOffer(Plain(ItemTestRegistries.Stone, 2), Plain(ItemTestRegistries.Stone, 2),
                    SecondCost: null, Result: Plain(ItemTestRegistries.DiamondSword, 1),
                    Uses: 0, MaxUses: 12, Xp: 2, PriceMultiplier: 0.05f, SpecialPrice: 0, Demand: 0)],
                VillagerLevel: 1, Experience: 0, IsRegularVillager: true, CanRestock: true));

        Assert.Equal(
            ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_14_4, packet),
            ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_19, packet));

        // With a second cost they diverge by exactly one byte. This is the ONLY discriminator, which is why every era case above carries one.
        MerchantOffer withSecond = packet.Offers.Offers[0] with { SecondCost = Plain(ItemTestRegistries.Stone, 1) };
        var packetWithSecond = packet with { Offers = new MerchantOffers([withSecond], 1, 0, true, true) };
        Assert.Equal(
            ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_14_4, packetWithSecond).Length - 1,
            ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_19, packetWithSecond).Length);
    }

    // 764/765: the 1.19 field framing over unnamed-root stacks.
    private static byte[] FormBUnnamedWire() =>
    [
        0x03,
        0x01,
        .. PlainStack(0x01, 0x02),
        .. UnnamedStoneStack,
        .. PlainStack(0x01, 0x01),
        .. OfferTail,
        .. MerchantTail,
    ];

    // 1.20.2 dropped the NBT root name; the field framing is otherwise the 1.19 form.
    [Fact]
    public void MerchantOffers_1_20_2_UsesUnnamedRoot_AndDiffersFromTheNamedBand()
    {
        byte[] wire = FormBUnnamedWire();
        var decoded = DecodeExact(MerchantCodecs.MerchantOffersV1_20_2, wire);
        AssertDecodedOffer(decoded);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_20_2, decoded));
        Assert.Equal(FormBWire().Length - 2, wire.Length);
        Assert.NotEqual(
            ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_19, decoded),
            ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_20_2, decoded));
    }

    // The byte count really is a byte on 477-757: 300 offers wrap to 44 exactly as vanilla's masking the size to one byte does, where the VarInt form would write two bytes.
    [Fact]
    public void MerchantOffers_FormA_OfferCount_IsAByte()
    {
        MerchantOffer[] many = [.. Enumerable.Range(0, 300).Select(_ => new MerchantOffer(
            Plain(ItemTestRegistries.Stone, 1), Plain(ItemTestRegistries.Stone, 1), null,
            Plain(ItemTestRegistries.Stone, 1), 0, 1, 0, 0f, 0, 0))];
        var packet = new ClientboundMerchantOffersPacket(3, new MerchantOffers(many, 1, 0, true, true));

        byte[] formA = ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_14_4, packet);
        Assert.Equal(300 & 0xFF, formA[1]);

        byte[] formB = ItemCodecRoundTrip.Encode(MerchantCodecs.MerchantOffersV1_19, packet);
        Assert.Equal(0xAC, formB[1]);   // VarInt 300 = AC 02
        Assert.Equal(0x02, formB[2]);
    }

    // Binding-level pins. Everything above proves the CODECS are right; these prove the TIMELINE picks them, which is the half that was actually broken. Each case runs the frame through the codec the registrar resolves for that protocol number, using BoundPacketCodec.Decode - the same entry point the live dispatcher uses, so a trailing byte raises exactly the live fault.

    private const string MerchantOffersId = "minecraft:merchant_offers";
    private const string CreativeSlotId = "minecraft:set_creative_mode_slot";

    // 477-485 have neither the demand int nor the packet's canRestock bool. Binding them to the 1.14.3 form over-read one byte on every villager open and CLOSED THE SESSION, which is why a live probe sweep lost entity.villager_interact on all three, and entity.metadata_custom_name with it (that row runs after the villager row in run_entity_probe.sh, against a client that was already gone).
    [Theory]
    [InlineData(477)]   // 1.14
    [InlineData(480)]   // 1.14.1
    [InlineData(485)]   // 1.14.2
    public void MerchantOffers_Binding_477To485_ResolvesTheNoRestockForm(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, MerchantOffersId);
        object decoded = bound.DecodeFrame(FormA114NoRestockWire());
        AssertDecodedOffer(Assert.IsType<ClientboundMerchantOffersPacket>(decoded), expectCanRestock: false);

        // ...and it must NOT be able to frame the 1.14.3 bytes, or the split is decorative.
        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(FormA114Wire()));
    }

    [Theory]
    [InlineData(490)]   // 1.14.3, where the packet gains canRestock but the offer still has no demand int
    public void MerchantOffers_Binding_490_ResolvesTheNoDemandForm(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, MerchantOffersId);
        object decoded = bound.DecodeFrame(FormA114Wire());
        AssertDecodedOffer(Assert.IsType<ClientboundMerchantOffersPacket>(decoded));

        Assert.ThrowsAny<Exception>(() => bound.DecodeFrame(FormA114NoRestockWire()));
    }

    [Theory]
    [InlineData(498)]   // 1.14.4, where the demand int arrives
    [InlineData(578)]   // 1.15.2
    [InlineData(754)]   // 1.16.5
    [InlineData(756)]   // 1.17.1
    [InlineData(758)]   // 1.18.2, the last byte-count release
    public void MerchantOffers_Binding_498To758_ResolvesTheByteCountForm(int protocol)
    {
        object decoded = BoundCodec.At(protocol, PacketFlow.Clientbound, MerchantOffersId)
            .DecodeFrame(FormAWire());
        AssertDecodedOffer(Assert.IsType<ClientboundMerchantOffersPacket>(decoded));
    }

    [Theory]
    [InlineData(759)]   // 1.19, where the list becomes a collection
    [InlineData(762)]   // 1.19.4
    [InlineData(763)]   // 1.20.1
    public void MerchantOffers_Binding_759To763_ResolvesTheCollectionForm(int protocol)
    {
        object decoded = BoundCodec.At(protocol, PacketFlow.Clientbound, MerchantOffersId)
            .DecodeFrame(FormBWire());
        AssertDecodedOffer(Assert.IsType<ClientboundMerchantOffersPacket>(decoded));
    }

    [Theory]
    [InlineData(764)]
    [InlineData(765)]
    public void MerchantOffers_Binding_764To765_ResolvesTheUnnamedRootForm(int protocol)
    {
        object decoded = BoundCodec.At(protocol, PacketFlow.Clientbound, MerchantOffersId)
            .DecodeFrame(FormBUnnamedWire());
        AssertDecodedOffer(Assert.IsType<ClientboundMerchantOffersPacket>(decoded));
    }

    [Theory]
    [InlineData(477)]
    [InlineData(498)]
    [InlineData(735)]   // 1.16
    [InlineData(754)]   // 1.16.5, the live-repro version for the sibling NBT-root defect
    [InlineData(755)]   // 1.17
    [InlineData(758)]
    [InlineData(763)]   // 1.20.1, the last named-root release
    public void CreativeSlot_Binding_477To763_ResolvesThePresentIdForm(int protocol)
    {
        byte[] wire = [0x00, 0x24, .. NamedStoneStack];
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, CreativeSlotId);

        var decoded = Assert.IsType<ServerboundSetCreativeModeSlotPacket>(bound.DecodeFrame(wire));
        Assert.Equal(36, decoded.Slot);
        AssertIsCustomName(decoded.Item);

        // set_creative_mode_slot is a SEND, so pin the encode direction through the same bound codec.
        Assert.Equal(wire, bound.Encode(decoded));
    }

    [Theory]
    [InlineData(764)]
    [InlineData(765)]
    public void CreativeSlot_Binding_764To765_ResolvesTheUnnamedRootForm(int protocol)
    {
        byte[] wire = [0x00, 0x24, .. UnnamedStoneStack];
        var decoded = Assert.IsType<ServerboundSetCreativeModeSlotPacket>(
            BoundCodec.At(protocol, PacketFlow.Serverbound, CreativeSlotId).DecodeFrame(wire));

        Assert.Equal(36, decoded.Slot);
        AssertIsCustomName(decoded.Item);
    }
}
