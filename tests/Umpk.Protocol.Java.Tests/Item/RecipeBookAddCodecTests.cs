using System.Buffers;
using Umpk.Game.Items;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Byte-anchored round-trip tests for <c>minecraft:recipe_book_add</c> (1.21.2+), the recipe-display tree.</summary>
/// <remarks>
/// <para>Every frame here is hand-built field by field from the specified wire contract, never by running the encoder, so an encoder that merely agrees with itself cannot satisfy them. The assertion is <c>encode(decode(bytes)) == bytes</c> plus a check on the decoded tree, because either one alone is passable by a wrong codec: a symmetric misreading round-trips, and a correct decode can still be written back short.</para>
/// <para>The codec under test is resolved through <see cref="PacketRegistrar"/> at a real protocol number, so these exercise the BINDING (which slot table and which component table that protocol gets), not a codec instance the test picked.</para>
/// <para>This is the corpus-dark half of the surface. The committed captures only ever carry <c>crafting_shaped</c> with <c>tag</c>, <c>item_stack</c> and <c>item</c> slots, so every other display type, every other slot variant, the inline-ids ingredient form and a present group are proved only here.</para>
/// </remarks>
public sealed class RecipeBookAddCodecTests
{
    // Protocol numbers, one per slot_display era. 768/769 share the 1.21.2 table with a NESTED smithing-trim pattern, 770-774 share the same eight ids with a HOLDER pattern, 775/776 use the eleven-id 26.1 table.
    private const int V1_21_2 = 768;
    private const int V1_21_5 = 770;
    private const int V26_1 = 775;

    // Protocol 768 slot-display wire ids.
    private const int OldEmpty = 0;
    private const int OldAnyFuel = 1;
    private const int OldItem = 2;
    private const int OldItemStack = 3;
    private const int OldTag = 4;
    private const int OldSmithingTrim = 5;
    private const int OldWithRemainder = 6;
    private const int OldComposite = 7;

    // Protocol 775 slot-display wire ids. Three variants were inserted, so every id from 2 up differs from the table above; that shift is what these constants pin.
    private const int NewEmpty = 0;
    private const int NewAnyFuel = 1;
    private const int NewWithAnyPotion = 2;
    private const int NewOnlyWithComponent = 3;
    private const int NewItem = 4;
    private const int NewItemStack = 5;
    private const int NewTag = 6;
    private const int NewDyed = 7;
    private const int NewSmithingTrim = 8;
    private const int NewWithRemainder = 9;
    private const int NewComposite = 10;

    // Recipe-display wire ids are unchanged from protocol 768 through 776.
    private const int Shapeless = 0;
    private const int Shaped = 1;
    private const int FurnaceType = 2;
    private const int StonecutterType = 3;
    private const int SmithingType = 4;

    // Test harness.

    private static BoundPacketCodec Bound(int protocol)
    {
        // Binding resolution is by protocol number alone (PacketRegistrar's own remark), so the codec key is free; building at the protocol is what selects the era codec.
        var version = new GameVersion(GameEdition.Java, "test", protocol);
        var builder = new ProtocolDescriptorBuilder(version, new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, 0, "minecraft:recipe_book_add");
        ProtocolDescriptor descriptor = builder.Build();

        Assert.True(
            descriptor.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound).TryGetInbound(0, out BoundPacketCodec entry),
            $"recipe_book_add is not registered at protocol {protocol}.");
        Assert.True(entry.IsImplemented, $"recipe_book_add is a marker at protocol {protocol}.");
        return entry;
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        write(w);
        return buffer.WrittenSpan.ToArray();
    }

    private static ClientboundRecipeBookAddPacket Decode(int protocol, byte[] frame)
    {
        var packet = (ClientboundRecipeBookAddPacket)Bound(protocol).Decode(frame, ItemTestRegistries.Context);

        // The decoder CONTAINS a parse failure instead of throwing, so a broken frame would otherwise arrive here as a short entry list and every round-trip assertion below would be vacuous.
        Assert.Equal(0, packet.UndecodedEntries);
        return packet;
    }

    private static byte[] Encode(int protocol, ClientboundRecipeBookAddPacket packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        Bound(protocol).Encode(ref w, packet, ItemTestRegistries.Context);
        return buffer.WrittenSpan.ToArray();
    }

    private static ClientboundRecipeBookAddPacket AssertRoundTrip(int protocol, byte[] frame)
    {
        ClientboundRecipeBookAddPacket packet = Decode(protocol, frame);
        Assert.Equal(frame, Encode(protocol, packet));
        return packet;
    }

    /// <summary>The smallest legal frame carrying one slot display under test: one entry, a stonecutter display whose INPUT is that slot and whose result and station are empty, no group, category 0, no crafting requirements, no flags, replace false.</summary>
    private static byte[] FrameWithSlot(Action<PacketWriter> writeSlot) => Build(w =>
    {
        w.WriteVarInt(1);                  // entry count
        w.WriteVarInt(7);                  // RecipeDisplayId
        w.WriteVarInt(StonecutterType);    // recipe_display type
        writeSlot(w);                      // input: the slot under test
        w.WriteVarInt(OldEmpty);           // result: empty (id 0 on BOTH era tables)
        w.WriteVarInt(OldEmpty);           // station: empty
        w.WriteVarInt(0);                  // OptionalVarInt group: absent
        w.WriteVarInt(0);                  // category
        w.WriteBool(false);                // Optional<List<Ingredient>>: absent
        w.WriteByte(0);                    // flags
        w.WriteBool(false);                // replace
    });

    private static SlotDisplay InputOf(ClientboundRecipeBookAddPacket packet) =>
        Assert.IsType<RecipeDisplay.Stonecutter>(Assert.Single(packet.Entries).Display).Input;

    // The recorded shape.

    /// <summary>The exact 166-byte <c>recipe_book_add</c> body expected on protocol 768, rebuilt field by field. The stub encoder silently reduced this shape to four bytes, so it is pinned here as a unit test.</summary>
    [Fact]
    public void CraftingShaped_RecordedShape_RoundTripsByteExact()
    {
        const int Planks = ItemTestRegistries.Stone;   // a seeded item id; the recorded id is registry-specific
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(1);                   // one entry
            w.WriteVarInt(257);                 // RecipeDisplayId (two-byte VarInt, as recorded)
            w.WriteVarInt(Shaped);              // crafting_shaped
            w.WriteVarInt(2);                   // width
            w.WriteVarInt(2);                   // height
            w.WriteVarInt(4);                   // 4 ingredient slot displays
            for (int i = 0; i < 4; i++)
            {
                w.WriteVarInt(OldTag);
                w.WriteString("minecraft:planks");
            }

            w.WriteVarInt(OldItemStack);        // result
            w.WriteVarInt(1);                   //   count
            w.WriteVarInt(Planks);              //   holder id
            w.WriteVarInt(0);                   //   components added
            w.WriteVarInt(0);                   //   components removed
            w.WriteVarInt(OldItem);             // crafting station
            w.WriteVarInt(Planks);
            w.WriteVarInt(0);                   // group absent
            w.WriteVarInt(3);                   // category
            w.WriteBool(true);                  // craftingRequirements present
            w.WriteVarInt(4);                   //   4 ingredients
            for (int i = 0; i < 4; i++)
            {
                w.WriteVarInt(0);               //   HolderSet size 0 => named-set (tag) form
                w.WriteString("minecraft:planks");
            }

            w.WriteByte(2);                     // flags: highlight
            w.WriteBool(true);                  // replace
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);

        RecipeBookEntry entry = Assert.Single(packet.Entries);
        Assert.Equal(257, entry.DisplayId);
        Assert.Equal(RecipeDisplayKind.CraftingShaped, entry.Kind);
        Assert.Null(entry.Group);
        Assert.Equal(3, entry.Category);
        Assert.False(entry.Notification);
        Assert.True(entry.Highlight);
        Assert.True(packet.Replace);

        var shaped = Assert.IsType<RecipeDisplay.CraftingShaped>(entry.Display);
        Assert.Equal(2, shaped.Width);
        Assert.Equal(2, shaped.Height);
        Assert.Equal(4, shaped.Ingredients.Count);
        Assert.All(shaped.Ingredients, s => Assert.Equal("minecraft:planks", Assert.IsType<SlotDisplay.Tag>(s).Name));
        Assert.Equal(1, Assert.IsType<SlotDisplay.Stack>(shaped.Result).Value.Count);
        Assert.Equal(Planks, Assert.IsType<SlotDisplay.Item>(shaped.CraftingStation).ItemId);

        Assert.Equal(4, entry.CraftingRequirements!.Count);
        Assert.All(
            entry.CraftingRequirements,
            i => Assert.Equal("minecraft:planks", Assert.IsType<RecipeIngredient.Tag>(i).Name));

        // The listing summary: the result names the recipe.
        Assert.Equal(Planks, entry.ResultItemId);
        Assert.Equal(1, entry.ResultCount);
    }

    // Every recipe_display type.

    [Fact]
    public void RecipeDisplay_CraftingShapeless_RoundTripsByteExact()
    {
        // Shapeless crafting writes ingredients, result, then station.
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(1);
            w.WriteVarInt(Shapeless);
            w.WriteVarInt(2);                                    // 2 ingredients
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.Stone);
            w.WriteVarInt(OldAnyFuel);
            w.WriteVarInt(OldItem);                              // result
            w.WriteVarInt(ItemTestRegistries.DiamondSword);
            w.WriteVarInt(OldEmpty);                             // station
            w.WriteVarInt(0);
            w.WriteVarInt(0);
            w.WriteBool(false);
            w.WriteByte(0);
            w.WriteBool(false);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        var shapeless = Assert.IsType<RecipeDisplay.CraftingShapeless>(Assert.Single(packet.Entries).Display);
        Assert.Equal(2, shapeless.Ingredients.Count);
        Assert.IsType<SlotDisplay.AnyFuel>(shapeless.Ingredients[1]);
        Assert.Equal(ItemTestRegistries.DiamondSword, Assert.IsType<SlotDisplay.Item>(shapeless.Result).ItemId);
    }

    [Fact]
    public void RecipeDisplay_Furnace_RoundTripsByteExact_IncludingDurationAndExperience()
    {
        // Furnace crafting writes ingredient, fuel, result, station, then VarInt duration, FLOAT experience. The float is the only one of its kind in the tree, and dropping it costs four bytes that no length prefix would reveal.
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(4);
            w.WriteVarInt(FurnaceType);
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.Stone);             // ingredient
            w.WriteVarInt(OldAnyFuel);                           // fuel
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);      // result
            w.WriteVarInt(OldEmpty);                             // station
            w.WriteVarInt(200);                                  // duration
            w.WriteFloat(0.35f);                                 // experience
            w.WriteVarInt(0);
            w.WriteVarInt(1);
            w.WriteBool(false);
            w.WriteByte(0);
            w.WriteBool(false);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        var furnace = Assert.IsType<RecipeDisplay.Furnace>(Assert.Single(packet.Entries).Display);
        Assert.Equal(200, furnace.Duration);
        Assert.Equal(0.35f, furnace.Experience);
        Assert.IsType<SlotDisplay.AnyFuel>(furnace.Fuel);
    }

    [Fact]
    public void RecipeDisplay_Stonecutter_RoundTripsByteExact()
    {
        byte[] frame = FrameWithSlot(w =>
        {
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.Stone);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        Assert.Equal(ItemTestRegistries.Stone, Assert.IsType<SlotDisplay.Item>(InputOf(packet)).ItemId);
    }

    [Fact]
    public void RecipeDisplay_Smithing_RoundTripsByteExact_AllFiveSlots()
    {
        // Smithing writes template, base, addition, result, then station.
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(9);
            w.WriteVarInt(SmithingType);
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.Stone);             // template
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);      // base
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.FilledMap);         // addition
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);      // result
            w.WriteVarInt(OldEmpty);                             // station
            w.WriteVarInt(0);
            w.WriteVarInt(0);
            w.WriteBool(false);
            w.WriteByte(0);
            w.WriteBool(false);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        var smithing = Assert.IsType<RecipeDisplay.Smithing>(Assert.Single(packet.Entries).Display);
        Assert.Equal(ItemTestRegistries.Stone, Assert.IsType<SlotDisplay.Item>(smithing.Template).ItemId);
        Assert.Equal(ItemTestRegistries.FilledMap, Assert.IsType<SlotDisplay.Item>(smithing.Addition).ItemId);
        Assert.IsType<SlotDisplay.Empty>(smithing.CraftingStation);
    }

    // Every slot_display variant in the 1.21.2 table.

    [Fact]
    public void SlotDisplay_Empty_RoundTripsByteExact()
    {
        // The empty variant carries only the type id and no payload.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w => w.WriteVarInt(OldEmpty)));
        Assert.IsType<SlotDisplay.Empty>(InputOf(packet));
    }

    [Fact]
    public void SlotDisplay_AnyFuel_RoundTripsByteExact()
    {
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w => w.WriteVarInt(OldAnyFuel)));
        Assert.IsType<SlotDisplay.AnyFuel>(InputOf(packet));
    }

    [Fact]
    public void SlotDisplay_Item_RoundTripsByteExact()
    {
        // holderRegistry(ITEM): a bare VarInt network id.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.FilledMap);
        }));

        Assert.Equal(ItemTestRegistries.FilledMap, Assert.IsType<SlotDisplay.Item>(InputOf(packet)).ItemId);
    }

    [Fact]
    public void SlotDisplay_ItemStack_RoundTripsByteExact()
    {
        // The 1.21.2 count-first stack: VarInt count, holder id, added-count, removed-count.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldItemStack);
            w.WriteVarInt(7);
            w.WriteVarInt(ItemTestRegistries.Stone);
            w.WriteVarInt(0);
            w.WriteVarInt(0);
        }));

        var stack = Assert.IsType<SlotDisplay.Stack>(InputOf(packet));
        Assert.Equal(7, stack.Value.Count);
        Assert.Equal(ItemTestRegistries.Stone, stack.Value.Item.NetworkId);
    }

    [Fact]
    public void SlotDisplay_Tag_RoundTripsByteExact()
    {
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldTag);
            w.WriteString("minecraft:logs");
        }));

        Assert.Equal("minecraft:logs", Assert.IsType<SlotDisplay.Tag>(InputOf(packet)).Name);
    }

    [Fact]
    public void SlotDisplay_WithRemainder_RoundTripsByteExact()
    {
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldWithRemainder);
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.Stone);             // input
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);      // remainder
        }));

        var remainder = Assert.IsType<SlotDisplay.WithRemainder>(InputOf(packet));
        Assert.Equal(ItemTestRegistries.Stone, Assert.IsType<SlotDisplay.Item>(remainder.Input).ItemId);
        Assert.Equal(ItemTestRegistries.DiamondSword, Assert.IsType<SlotDisplay.Item>(remainder.Remainder).ItemId);
    }

    [Fact]
    public void SlotDisplay_Composite_RoundTripsByteExact_IncludingEmptyAndNested()
    {
        // A composite of three, one of which is itself a composite: the list prefix and the recursion are both on the write path, and a flattened or truncated write changes the length.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldComposite);
            w.WriteVarInt(3);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);
            w.WriteVarInt(OldComposite);
            w.WriteVarInt(0);                                    // an EMPTY nested composite
        }));

        var composite = Assert.IsType<SlotDisplay.Composite>(InputOf(packet));
        Assert.Equal(3, composite.Contents.Count);
        Assert.IsType<SlotDisplay.Empty>(composite.Contents[0]);
        Assert.Empty(Assert.IsType<SlotDisplay.Composite>(composite.Contents[2]).Contents);
    }

    // The smithing-trim era split.

    [Fact]
    public void SmithingTrim_On1_21_2_ReadsANestedSlotDisplayPattern()
    {
        // Protocol 768 declares the third field as an inline pattern display. Reading a holder here would take the nested type id as a holder id and then misread the whole rest of the packet.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldSmithingTrim);
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.Stone);             // base
            w.WriteVarInt(OldAnyFuel);                           // material
            w.WriteVarInt(OldItem);                              // pattern: a nested SlotDisplay
            w.WriteVarInt(ItemTestRegistries.DiamondSword);
        }));

        var trim = Assert.IsType<SlotDisplay.SmithingTrim>(InputOf(packet));
        var nested = Assert.IsType<TrimPatternDisplay.Nested>(trim.Pattern);
        Assert.Equal(ItemTestRegistries.DiamondSword, Assert.IsType<SlotDisplay.Item>(nested.Pattern).ItemId);
    }

    [Fact]
    public void SmithingTrim_On1_21_5_ReadsAHolderPattern()
    {
        // Protocol 770 changes the field to a trim-pattern holder, encoded as VarInt(id + 1).
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_5, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldSmithingTrim);
            w.WriteVarInt(OldItem);
            w.WriteVarInt(ItemTestRegistries.Stone);             // base
            w.WriteVarInt(OldAnyFuel);                           // material
            w.WriteVarInt(13);                                   // pattern holder: id 12, written as 12 + 1
        }));

        var trim = Assert.IsType<SlotDisplay.SmithingTrim>(InputOf(packet));
        Assert.Equal(12, Assert.IsType<TrimPatternDisplay.Registry>(trim.Pattern).PatternId);
    }

    [Fact]
    public void SmithingTrim_TheTwoWireLayoutsDisagreeOnTheSameBytes()
    {
        // The discriminator has to BITE: one byte sequence must mean different things on the two eras. `05 01 01 02` is (smithing_trim, any_fuel base, any_fuel material) then a single byte 0x02, which 1.21.2 reads as a nested `item` display needing one more byte and 1.21.5 reads as the complete holder id 1. So the same bytes are a short frame on one era and a whole one on the other.
        byte[] holderEraFrame = FrameWithSlot(w =>
        {
            w.WriteVarInt(OldSmithingTrim);
            w.WriteVarInt(OldAnyFuel);
            w.WriteVarInt(OldAnyFuel);
            w.WriteVarInt(2);
        });

        ClientboundRecipeBookAddPacket onHolderEra = AssertRoundTrip(V1_21_5, holderEraFrame);
        Assert.IsType<TrimPatternDisplay.Registry>(
            Assert.IsType<SlotDisplay.SmithingTrim>(InputOf(onHolderEra)).Pattern);

        // On 1.21.2 the same bytes are consumed differently: the trailing 0x02 is taken as a NESTED `item` display, which shifts every later field and runs off the frame. The whole packet must be rejected. Returning a partially decoded replacement would let the client mutate recipe state from bytes whose list boundary it could no longer prove.
        Assert.Throws<ProtocolViolationException>(() =>
            Bound(V1_21_2).Decode(holderEraFrame, ItemTestRegistries.Context));
    }

    // The 26.1 slot table.

    [Fact]
    public void SlotDisplay_26_1_TypeIdsAreShifted_NotAppended()
    {
        // The whole reason 26.1 needs its own table: `tag` is id 4 before it and id 6 from it. Decoding this frame with the older table would read id 6 as with_remainder and consume the tag string as two nested displays.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V26_1, FrameWithSlot(w =>
        {
            w.WriteVarInt(NewTag);
            w.WriteString("minecraft:planks");
        }));

        Assert.Equal("minecraft:planks", Assert.IsType<SlotDisplay.Tag>(InputOf(packet)).Name);

        // And the same bytes are NOT a tag on the older table.
        byte[] frame = FrameWithSlot(w =>
        {
            w.WriteVarInt(NewTag);
            w.WriteString("minecraft:planks");
        });
        Assert.Throws<ProtocolViolationException>(() => Bound(V1_21_2).Decode(frame, ItemTestRegistries.Context));
    }

    [Fact]
    public void SlotDisplay_26_1_WithAnyPotion_RoundTripsByteExact()
    {
        // WithAnyPotion carries one wrapped SlotDisplay and nothing else.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V26_1, FrameWithSlot(w =>
        {
            w.WriteVarInt(NewWithAnyPotion);
            w.WriteVarInt(NewItem);
            w.WriteVarInt(ItemTestRegistries.Stone);
        }));

        var potion = Assert.IsType<SlotDisplay.WithAnyPotion>(InputOf(packet));
        Assert.Equal(ItemTestRegistries.Stone, Assert.IsType<SlotDisplay.Item>(potion.Display).ItemId);
    }

    [Fact]
    public void SlotDisplay_26_1_OnlyWithComponent_RoundTripsByteExact()
    {
        // OnlyWithComponent carries a slot display then a component-type id as a bare VarInt.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V26_1, FrameWithSlot(w =>
        {
            w.WriteVarInt(NewOnlyWithComponent);
            w.WriteVarInt(NewAnyFuel);
            w.WriteVarInt(41);
        }));

        var gated = Assert.IsType<SlotDisplay.OnlyWithComponent>(InputOf(packet));
        Assert.Equal(41, gated.ComponentId);
        Assert.IsType<SlotDisplay.AnyFuel>(gated.Source);
    }

    [Fact]
    public void SlotDisplay_26_1_Dyed_RoundTripsByteExact_DyeThenTarget()
    {
        // DyedSlotDemo carries dye first, then target. Swapping them round-trips byte-identically, so the decoded field assertion below is what actually pins the order.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V26_1, FrameWithSlot(w =>
        {
            w.WriteVarInt(NewDyed);
            w.WriteVarInt(NewItem);
            w.WriteVarInt(ItemTestRegistries.Stone);             // dye
            w.WriteVarInt(NewItem);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);      // target
        }));

        var dyed = Assert.IsType<SlotDisplay.Dyed>(InputOf(packet));
        Assert.Equal(ItemTestRegistries.Stone, Assert.IsType<SlotDisplay.Item>(dyed.Dye).ItemId);
        Assert.Equal(ItemTestRegistries.DiamondSword, Assert.IsType<SlotDisplay.Item>(dyed.Target).ItemId);
    }

    [Fact]
    public void SlotDisplay_26_1_ItemStack_IsATemplate_IdBeforeCount()
    {
        // Protocol 775 changes the field from a count-first stack to a template: item holder id, VarInt count, then the patch. That is the REVERSE of the earlier stack's first two fields. 1.21.11 and earlier still carry a plain ItemStack Protocol 774 still carries count then item.
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V26_1, FrameWithSlot(w =>
        {
            w.WriteVarInt(NewItemStack);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);      // holder id FIRST
            w.WriteVarInt(9);                                    // then the count
            w.WriteVarInt(0);
            w.WriteVarInt(0);
        }));

        var stack = Assert.IsType<SlotDisplay.Stack>(InputOf(packet));
        Assert.Equal(ItemTestRegistries.DiamondSword, stack.Value.Item.NetworkId);
        Assert.Equal(9, stack.Value.Count);
    }

    [Fact]
    public void SlotDisplay_ItemStack_TheTwoFormsMirrorEachOther_SoOnlyAFieldAssertionSeparatesThem()
    {
        // The same payload bytes under the two forms: 1.21.11 reads (count 2, item 1) and 26.1 reads (item 2, count 1). BOTH re-encode byte-identically, which is why the corpus round-trip could never have caught the wrong binding and why this test asserts values, not bytes.
        static void Payload(PacketWriter w)
        {
            w.WriteVarInt(2);
            w.WriteVarInt(1);
            w.WriteVarInt(0);
            w.WriteVarInt(0);
        }

        ClientboundRecipeBookAddPacket old = AssertRoundTrip(774, FrameWithSlot(w =>
        {
            w.WriteVarInt(OldItemStack);
            Payload(w);
        }));

        ClientboundRecipeBookAddPacket now = AssertRoundTrip(V26_1, FrameWithSlot(w =>
        {
            w.WriteVarInt(NewItemStack);
            Payload(w);
        }));

        ItemStack before = Assert.IsType<SlotDisplay.Stack>(InputOf(old)).Value;
        ItemStack after = Assert.IsType<SlotDisplay.Stack>(InputOf(now)).Value;

        Assert.Equal(2, before.Count);
        Assert.Equal(1, before.Item.NetworkId);
        Assert.Equal(2, after.Item.NetworkId);
        Assert.Equal(1, after.Count);
    }

    [Fact]
    public void SlotDisplay_26_1_SmithingTrimAndCompositeUseTheShiftedIds()
    {
        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V26_1, FrameWithSlot(w =>
        {
            w.WriteVarInt(NewComposite);
            w.WriteVarInt(2);
            w.WriteVarInt(NewSmithingTrim);
            w.WriteVarInt(NewEmpty);                             // base
            w.WriteVarInt(NewAnyFuel);                           // material
            w.WriteVarInt(4);                                    // pattern holder id 3
            w.WriteVarInt(NewWithRemainder);
            w.WriteVarInt(NewEmpty);
            w.WriteVarInt(NewEmpty);
        }));

        var composite = Assert.IsType<SlotDisplay.Composite>(InputOf(packet));
        Assert.Equal(2, composite.Contents.Count);
        var trim = Assert.IsType<SlotDisplay.SmithingTrim>(composite.Contents[0]);
        Assert.Equal(3, Assert.IsType<TrimPatternDisplay.Registry>(trim.Pattern).PatternId);
        Assert.IsType<SlotDisplay.WithRemainder>(composite.Contents[1]);
    }

    // Entry-level fields.

    [Fact]
    public void Entry_GroupPresent_RoundTripsByteExact_AndDecodesAsNMinusOne()
    {
        // OPTIONAL_VAR_INT: 0 means absent, n means n - 1. Group 0 is therefore written as 1, and a codec that confused "absent" with "zero" would write 0 and lose a byte of meaning.
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(2);
            w.WriteVarInt(StonecutterType);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(1);                  // group present, value 0
            w.WriteVarInt(5);                  // category
            w.WriteBool(false);
            w.WriteByte(0);
            w.WriteBool(false);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        RecipeBookEntry entry = Assert.Single(packet.Entries);
        Assert.Equal(0, entry.Group);
        Assert.Equal(5, entry.Category);
    }

    [Fact]
    public void Entry_EmptyRequirementList_IsNotTheSameAsAbsent()
    {
        // Optional<List<...>>: present-and-empty is `01 00`, absent is `00`. Collapsing the two changes the frame length, so this is a byte assertion as much as a semantic one.
        byte[] present = Build(w =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(2);
            w.WriteVarInt(StonecutterType);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(0);
            w.WriteVarInt(0);
            w.WriteBool(true);                 // present
            w.WriteVarInt(0);                  //   with zero ingredients
            w.WriteByte(0);
            w.WriteBool(false);
        });

        ClientboundRecipeBookAddPacket decoded = AssertRoundTrip(V1_21_2, present);
        Assert.Empty(Assert.Single(decoded.Entries).CraftingRequirements!);

        // The absent form is the same frame with `00` where the present form writes `01 00`, so present-and-empty costs exactly one more byte. Collapsing null into an empty list would make the encoder write the shorter of the two and change the frame.
        byte[] absent = FrameWithSlot(w => w.WriteVarInt(OldEmpty));
        Assert.Null(Assert.Single(Decode(V1_21_2, absent).Entries).CraftingRequirements);
        Assert.Equal(absent.Length + 1, present.Length);
        Assert.Equal(absent, Encode(V1_21_2, Decode(V1_21_2, absent)));
    }

    [Fact]
    public void Entry_InlineIngredientIds_RoundTripByteExact()
    {
        // The HolderSet inline form: VarInt(count + 1) then that many ids. The corpus only ever carries the tag form (a leading 0), so this branch is proved nowhere else.
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(2);
            w.WriteVarInt(StonecutterType);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(0);
            w.WriteVarInt(0);
            w.WriteBool(true);
            w.WriteVarInt(2);                  // 2 ingredients
            w.WriteVarInt(3);                  //   inline set of 2 ids
            w.WriteVarInt(ItemTestRegistries.Stone);
            w.WriteVarInt(ItemTestRegistries.DiamondSword);
            w.WriteVarInt(0);                  //   a tag set
            w.WriteString("minecraft:planks");
            w.WriteByte(0);
            w.WriteBool(false);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        IReadOnlyList<RecipeIngredient> requirements = Assert.Single(packet.Entries).CraftingRequirements!;
        Assert.Equal(2, requirements.Count);
        var inline = Assert.IsType<RecipeIngredient.Items>(requirements[0]);
        Assert.Equal([ItemTestRegistries.Stone, ItemTestRegistries.DiamondSword], inline.ItemIds);
        Assert.Equal("minecraft:planks", Assert.IsType<RecipeIngredient.Tag>(requirements[1]).Name);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, true)]
    [InlineData(3, true, true)]
    public void Entry_FlagsByte_RoundTripsByteExact(int flags, bool notification, bool highlight)
    {
        // A recipe-book entry uses bit 0 for notification, bit 1 highlight.
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(1);
            w.WriteVarInt(2);
            w.WriteVarInt(StonecutterType);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(OldEmpty);
            w.WriteVarInt(0);
            w.WriteVarInt(0);
            w.WriteBool(false);
            w.WriteByte((byte)flags);
            w.WriteBool(true);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        RecipeBookEntry entry = Assert.Single(packet.Entries);
        Assert.Equal(notification, entry.Notification);
        Assert.Equal(highlight, entry.Highlight);
        Assert.True(packet.Replace);
    }

    [Fact]
    public void Packet_ManyEntriesAndReplaceFalse_RoundTripByteExact()
    {
        byte[] frame = Build(w =>
        {
            w.WriteVarInt(3);
            for (int i = 0; i < 3; i++)
            {
                w.WriteVarInt(100 + i);
                w.WriteVarInt(StonecutterType);
                w.WriteVarInt(OldEmpty);
                w.WriteVarInt(OldEmpty);
                w.WriteVarInt(OldEmpty);
                w.WriteVarInt(0);
                w.WriteVarInt(i);
                w.WriteBool(false);
                w.WriteByte(0);
            }

            w.WriteBool(false);
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(V1_21_2, frame);
        Assert.Equal(3, packet.Entries.Count);
        Assert.Equal([100, 101, 102], packet.Entries.Select(e => e.DisplayId));
        Assert.False(packet.Replace);
    }

    // Encoder refusal cases.

    [Fact]
    public void Write_RefusesA26_1OnlyVariantOnTheOlderTable()
    {
        // 26.1 INSERTED dyed at id 7, which 1.21.2 gives to composite. Writing "some" id would put a plausible but wrong type on the wire and corrupt everything after it, so the write faults.
        ClientboundRecipeBookAddPacket packet = OneEntry(
            new RecipeDisplay.Stonecutter(
                new SlotDisplay.Dyed(SlotDisplay.Empty.Instance, SlotDisplay.Empty.Instance),
                SlotDisplay.Empty.Instance,
                SlotDisplay.Empty.Instance));

        Assert.Throws<ProtocolViolationException>(() => Encode(V1_21_2, packet));

        // The identical packet is writable on 26.1, so the refusal is about the era and nothing else.
        Assert.NotEmpty(Encode(V26_1, packet));
    }

    [Fact]
    public void Write_RefusesAHolderTrimPatternOnTheNestedWireLayout()
    {
        ClientboundRecipeBookAddPacket packet = OneEntry(
            new RecipeDisplay.Stonecutter(
                new SlotDisplay.SmithingTrim(
                    SlotDisplay.Empty.Instance,
                    SlotDisplay.Empty.Instance,
                    new TrimPatternDisplay.Registry(3)),
                SlotDisplay.Empty.Instance,
                SlotDisplay.Empty.Instance));

        Assert.Throws<ProtocolViolationException>(() => Encode(V1_21_2, packet));
        Assert.NotEmpty(Encode(V1_21_5, packet));
    }

    [Fact]
    public void Write_RefusesANestedTrimPatternOnTheHolderWireLayout()
    {
        ClientboundRecipeBookAddPacket packet = OneEntry(
            new RecipeDisplay.Stonecutter(
                new SlotDisplay.SmithingTrim(
                    SlotDisplay.Empty.Instance,
                    SlotDisplay.Empty.Instance,
                    new TrimPatternDisplay.Nested(SlotDisplay.Empty.Instance)),
                SlotDisplay.Empty.Instance,
                SlotDisplay.Empty.Instance));

        Assert.Throws<ProtocolViolationException>(() => Encode(V1_21_5, packet));
        Assert.NotEmpty(Encode(V1_21_2, packet));
    }

    [Fact]
    public void Write_RefusesAPacketWhoseDecodeWasContained()
    {
        // UndecodedEntries != 0 means bytes were consumed that no tree stands for. Writing the entries that DID decode would emit a shorter packet claiming to be the same one, which is exactly the silent truncation this encoder exists to end.
        var contained = new ClientboundRecipeBookAddPacket([], Replace: false, UndecodedEntries: 1);
        Assert.Throws<ProtocolViolationException>(() => Encode(V1_21_2, contained));
    }

    [Fact]
    public void Read_ContainsAnUnknownSlotDisplayTypeRatherThanThrowing()
    {
        // An unknown type id has an unknown length, so decode cannot resynchronise. The containment path reports it instead of killing the session; the trailing bytes then fail frame-exactness.
        byte[] frame = FrameWithSlot(w => w.WriteVarInt(60));
        Assert.Throws<ProtocolViolationException>(() => Bound(V1_21_2).Decode(frame, ItemTestRegistries.Context));
    }

    /// <summary>Every protocol this packet exists on round-trips an era-appropriate frame through its own binding. The slot-table and component-table choices are per-protocol, so a binding that reached for a neighbouring era's table would show up here and nowhere else in this file.</summary>
    [Theory]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(771)]
    [InlineData(772)]
    [InlineData(773)]
    [InlineData(774)]
    [InlineData(775)]
    [InlineData(776)]
    public void EveryBoundProtocol_RoundTripsAFrameOfItsOwnWireLayout(int protocol)
    {
        // `tag` is id 4 before 26.1 and id 6 from it, so this frame is only decodable on the right table.
        int tagId = protocol >= 775 ? NewTag : OldTag;
        byte[] frame = FrameWithSlot(w =>
        {
            w.WriteVarInt(tagId);
            w.WriteString("minecraft:planks");
        });

        ClientboundRecipeBookAddPacket packet = AssertRoundTrip(protocol, frame);
        Assert.Equal("minecraft:planks", Assert.IsType<SlotDisplay.Tag>(InputOf(packet)).Name);
    }

    private static ClientboundRecipeBookAddPacket OneEntry(RecipeDisplay display) =>
        new(
        [
            new RecipeBookEntry(
                DisplayId: 1,
                Kind: RecipeDisplayKind.Stonecutter,
                ResultItemId: -1,
                ResultId: default,
                ResultCount: 0,
                Group: null,
                Category: 0,
                Notification: false,
                Highlight: false,
                Display: display,
                CraftingRequirements: null),
        ],
            Replace: false);
}
