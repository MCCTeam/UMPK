using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Shared helpers for the item/container packet codecs: the per-era component tables, the stack reader/writer plumbing (legacy / varintId / component stacks), item-cost read/write, and the per-packet era factory helpers used across the container, merchant, recipe and use-item families.</summary>
internal static class ItemPacketCodecShared
{
    internal static ItemComponentTable Table770 => ItemStackCodecs.ComponentsV1_21_5;

    internal static ItemComponentTable Table776 => ItemStackCodecs.ComponentsV26_2;

    /// <summary>The protocol 768 (1.21.2/1.21.3) component era table.</summary>
    internal static ItemComponentTable Table768 { get; } = ItemComponentTable.V1_21_2();

    /// <summary>The protocol 769 (1.21.4) component era table.</summary>
    internal static ItemComponentTable Table769 { get; } = ItemComponentTable.V1_21_4();

    /// <summary>The protocol 771/772 (1.21.6-1.21.8) component era table.</summary>
    internal static ItemComponentTable Table771 { get; } = ItemComponentTable.V1_21_6();

    /// <summary>The protocol 773 (1.21.9/1.21.10) component era table.</summary>
    internal static ItemComponentTable Table773 { get; } = ItemComponentTable.V1_21_9();

    /// <summary>The protocol 774 (1.21.11) component era table.</summary>
    internal static ItemComponentTable Table774 { get; } = ItemComponentTable.V1_21_11();

    /// <summary>The protocol 775 (26.1) component era table.</summary>
    internal static ItemComponentTable Table775 { get; } = ItemComponentTable.V26_1();

    /// <summary>The protocol 777 (26.3) component era table.</summary>
    internal static ItemComponentTable Table777 { get; } = ItemComponentTable.V26_3();

    /// <summary>The component eras whose <c>container_click</c> still carries the FULL item stack: 768 and 769. The hashed stack arrives at 1.21.5, so these two are the tail of the full-stack timeline rather than the head of the hashed one, and the split is declared here instead of being re-derived as a protocol comparison at the bind site.</summary>
    private static (int Protocol, string Era, ItemComponentTable Table)[] PreHashedStackComponentEras { get; } =
    [
        (JavaProtocols.V1_21_2, nameof(Table768), Table768),
        (JavaProtocols.V1_21_4, nameof(Table769), Table769),
    ];

    /// <summary>The component eras whose <c>container_click</c> carries the 1.21.5 HASHED stack: 770 and up.</summary>
    internal static (int Protocol, string Era, ItemComponentTable Table)[] HashedStackComponentEras { get; } =
    [
        (JavaProtocols.V1_21_5, nameof(Table770), Table770),
        (JavaProtocols.V1_21_6, nameof(Table771), Table771),
        (JavaProtocols.V1_21_9, nameof(Table773), Table773),
        (JavaProtocols.V1_21_11, nameof(Table774), Table774),
        (JavaProtocols.V26_1, nameof(Table775), Table775),
        (JavaProtocols.V26_2, nameof(Table776), Table776),
        (JavaProtocols.V26_3, nameof(Table777), Table777),
    ];

    /// <summary>
    /// Every component era from 1.21.2 up, as (first protocol of the era, that era's table), in protocol order. An entry exists wherever either the component id ORDERING or a component PAYLOAD changes, because both make a neighbouring era's table wrong:
    /// <list type="bullet">
    /// <item>768: its own 67-id ordering (diverges from 770 at wire id 15).</item>
    /// <item>769: the 768 ordering, but <c>custom_model_data</c> became four lists.</item>
    /// <item>770: the 96-id ordering, zero-byte unbreakable, and the modern text dialect.</item>
    /// <item>771: the 770 ordering, plus the <c>attribute_modifiers</c> display field 1.21.6 added.</item>
    /// <item>773: as 771, minus <c>profile</c> / <c>entity_data</c> / <c>block_entity_data</c>, all
    /// re-shaped by 1.21.9.</item>
    /// <item>774: its own 104-id ordering (diverges from 770 at wire id 5).</item>
    /// <item>775: its own 110-id ordering (776 inserts <c>sulfur_cube_content</c> at 78).</item>
    /// <item>776: the 111-id ordering.</item>
    /// <item>777: the 122-id ordering (thirteen components added, <c>swing_animation</c> and <c>map_color</c> removed).</item>
    /// </list>
    /// Every item-family packet that carries a component stack walks this list, so a new era is added in exactly one place instead of once per packet.
    /// <para><c>Era</c> is the table member's own name, carried as data because the binding loops need it. The codec-identity pin defaults to the call site's source expression, and inside a loop that expression reads <c>Make...(table)</c> identically on every era, so without this name the eight binds would pin as one indistinguishable token. The explicit name keeps neighbouring era tables distinct even when their call-site expressions are identical.</para>
    /// </summary>
    internal static (int Protocol, string Era, ItemComponentTable Table)[] ComponentEras { get; } =
        [.. PreHashedStackComponentEras, .. HashedStackComponentEras];

    internal static PacketCodec<ClientboundContainerSetSlotPacket> MakeContainerSetSlot(ItemComponentTable table)
    {
        StackWire stacks = StackWire.Components(table);
        return PacketCodec<ClientboundContainerSetSlotPacket>.Of(
            (ref PacketWriter w, ClientboundContainerSetSlotPacket p, PacketCodecContext c) =>
                WriteSetSlot(ref w, p, c, stacks, containerIdIsVarInt: true, withStateId: true),
            (ref PacketReader r, PacketCodecContext c) =>
                ReadSetSlot(ref r, c, stacks, containerIdIsVarInt: true, withStateId: true),
            WireShape.Of("varint,varint,short,stack", table.ShapeToken));
    }

    internal static PacketCodec<ClientboundContainerSetContentPacket> MakeContainerSetContent(ItemComponentTable table) =>
        PacketCodec<ClientboundContainerSetContentPacket>.Of(
            (ref PacketWriter w, ClientboundContainerSetContentPacket p, PacketCodecContext c) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.StateId);
                w.WriteVarInt(p.Items.Count);
                foreach (ItemStack stack in p.Items)
                    ItemStackCodecs.WriteModernStack(ref w, stack, c, table);

                ItemStackCodecs.WriteModernStack(ref w, p.CarriedItem, c, table);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadVarInt();
                int state = r.ReadVarInt();
                int count = r.ReadVarInt();
                var items = new ItemStack[count];
                for (int i = 0; i < count; i++)
                    items[i] = ItemStackCodecs.ReadModernStack(ref r, c, table);

                ItemStack carried = ItemStackCodecs.ReadModernStack(ref r, c, table);
                return new ClientboundContainerSetContentPacket(id, state, items, carried);
            },
            WireShape.Of("varint,varint,varint*stack,stack", table.ShapeToken));

    internal static PacketCodec<ClientboundSetCursorItemPacket> MakeSetCursorItem(ItemComponentTable table) =>
        PacketCodec<ClientboundSetCursorItemPacket>.Of(
            (ref PacketWriter w, ClientboundSetCursorItemPacket p, PacketCodecContext c) =>
                ItemStackCodecs.WriteModernStack(ref w, p.Item, c, table),
            (ref PacketReader r, PacketCodecContext c) =>
                new ClientboundSetCursorItemPacket(ItemStackCodecs.ReadModernStack(ref r, c, table)),
            WireShape.Of("stack", table.ShapeToken));

    internal static PacketCodec<ClientboundSetPlayerInventoryPacket> MakeSetPlayerInventory(ItemComponentTable table) =>
        PacketCodec<ClientboundSetPlayerInventoryPacket>.Of(
            (ref PacketWriter w, ClientboundSetPlayerInventoryPacket p, PacketCodecContext c) =>
            {
                w.WriteVarInt(p.Slot);
                ItemStackCodecs.WriteModernStack(ref w, p.Item, c, table);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int slot = r.ReadVarInt();
                ItemStack item = ItemStackCodecs.ReadModernStack(ref r, c, table);
                return new ClientboundSetPlayerInventoryPacket(slot, item);
            },
            WireShape.Of("varint,stack", table.ShapeToken));

    internal static PacketCodec<ServerboundContainerClickPacket> MakeContainerClick(ItemComponentTable table) =>
        PacketCodec<ServerboundContainerClickPacket>.Of(
            (ref PacketWriter w, ServerboundContainerClickPacket p, PacketCodecContext c) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.StateId);
                w.WriteShort(p.Slot);
                w.WriteByte(p.Button);
                w.WriteVarInt(p.Mode);
                w.WriteVarInt(p.ChangedSlots.Count);
                foreach (PredictedSlot slot in p.ChangedSlots)
                {
                    w.WriteShort(slot.Slot);
                    ItemStackCodecs.WriteHashedStack(ref w, ItemStackCodecs.ToHashedStack(slot.Stack, table, c));
                }

                ItemStackCodecs.WriteHashedStack(ref w, ItemStackCodecs.ToHashedStack(p.CarriedItem ?? ItemStack.Empty, table, c));
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadVarInt();
                int state = r.ReadVarInt();
                short slot = r.ReadShort();
                byte button = r.ReadByte();
                int mode = r.ReadVarInt();
                int changedCount = r.ReadVarInt();
                for (int i = 0; i < changedCount; i++)
                {
                    _ = r.ReadShort();
                    _ = ItemStackCodecs.ReadHashedStack(ref r, c);
                }

                _ = ItemStackCodecs.ReadHashedStack(ref r, c);

                // A modern container click is client-to-server and its stacks are one-way hashed on the wire, so the raw predicted stacks cannot be reconstructed on decode. The frame is fully consumed (frame-exact); the decoded record carries no predicted stacks.
                return new ServerboundContainerClickPacket(id, state, slot, button, mode, 0, null, [], null);
            },
            WireShape.Of("varint,varint,short,byte,varint,varint*(short,hashed_stack),hashed_stack", table.ShapeToken));

    // The 1.21.2-1.21.4 container click: the modern VarInt header with FULL component stacks in the changed-slots map and for the carried item. 1.21.5 replaced those with hashed stacks (MakeContainerClick above), so this form covers exactly 768 and 769 and is parameterised on the era table for the same reason every other member here is.
    internal static PacketCodec<ServerboundContainerClickPacket> MakeContainerClickFullStack(ItemComponentTable table) =>
        PacketCodec<ServerboundContainerClickPacket>.Of(
            (ref PacketWriter w, ServerboundContainerClickPacket p, PacketCodecContext c) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.StateId);
                w.WriteShort(p.Slot);
                w.WriteByte(p.Button);
                w.WriteVarInt(p.Mode);
                w.WriteVarInt(p.ChangedSlots.Count);
                foreach (PredictedSlot slot in p.ChangedSlots)
                {
                    w.WriteShort(slot.Slot);
                    ItemStackCodecs.WriteModernStack(ref w, slot.Stack, c, table);
                }

                ItemStackCodecs.WriteModernStack(ref w, p.CarriedItem ?? ItemStack.Empty, c, table);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadVarInt();
                int state = r.ReadVarInt();
                short slot = r.ReadShort();
                byte button = r.ReadByte();
                int mode = r.ReadVarInt();
                int changedCount = r.ReadVarInt();
                var changed = new PredictedSlot[changedCount];
                for (int i = 0; i < changedCount; i++)
                {
                    short changedSlot = r.ReadShort();
                    changed[i] = new PredictedSlot(changedSlot, ItemStackCodecs.ReadModernStack(ref r, c, table));
                }

                ItemStack carried = ItemStackCodecs.ReadModernStack(ref r, c, table);
                return new ServerboundContainerClickPacket(id, state, slot, button, mode, 0, null, changed, carried);
            },
            WireShape.Of("varint,varint,short,byte,varint,varint*(short,stack),stack", table.ShapeToken));

    internal static PacketCodec<ServerboundSetCreativeModeSlotPacket> MakeCreativeSlot(ItemComponentTable table) =>
        PacketCodec<ServerboundSetCreativeModeSlotPacket>.Of(
            (ref PacketWriter w, ServerboundSetCreativeModeSlotPacket p, PacketCodecContext c) =>
            {
                w.WriteShort(p.Slot);
                ItemStackCodecs.WriteDelimitedStack(ref w, p.Item, c, table);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                short slot = r.ReadShort();
                ItemStack item = ItemStackCodecs.ReadDelimitedStack(ref r, c, table);
                return new ServerboundSetCreativeModeSlotPacket(slot, item);
            },
            WireShape.Of("short,delimited_stack", table.ShapeToken));

    /// <summary>The sixteenths scale the 1.9-1.10 in-block cursor is carried in.</summary>
    /// <remarks>
    /// <para>This protocol band writes <c>(int)(facing * 16.0F)</c> as a byte and reads the unsigned byte divided by 16.0. <see cref="ServerboundUseItemOnPacket"/> carries the 0..1 fraction on every era, so the codec owns the conversion.</para>
    /// <para>Without it the cast truncated every in-domain value: <c>(byte)0.5f</c> is 0, so the server was told the click landed on the block's corner on the whole 1.9-1.10 band no matter where it actually landed, and slab halves, stair orientation and trapdoor halves all came out wrong. The decode side was the same error mirrored, turning a legal wire byte 8 into a 0..1 field holding 8. The 1.11+ float band never had this and is untouched.</para>
    /// <para>Deliberately NOT clamped: vanilla does not clamp either, and an exact 1.0 hit on the far face legitimately writes 16, which has to survive a decode/re-encode round trip.</para>
    /// </remarks>
    private const float LegacyCursorScale = 16f;

    internal static PacketCodec<ServerboundUseItemOnPacket> MakeUseItemOnPre114(bool floatCursor) =>
        PacketCodec<ServerboundUseItemOnPacket>.Of(
            (ref PacketWriter w, ServerboundUseItemOnPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Position, BlockPosLayout.PrePacked114);
                w.WriteVarInt(p.Face);
                w.WriteVarInt(p.Hand);
                if (floatCursor)
                {
                    w.WriteFloat(p.CursorX);
                    w.WriteFloat(p.CursorY);
                    w.WriteFloat(p.CursorZ);
                }
                else
                {
                    w.WriteByte((byte)(int)(p.CursorX * LegacyCursorScale));
                    w.WriteByte((byte)(int)(p.CursorY * LegacyCursorScale));
                    w.WriteByte((byte)(int)(p.CursorZ * LegacyCursorScale));
                }
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                BlockPos pos = r.ReadBlockPos(BlockPosLayout.PrePacked114);
                int face = r.ReadVarInt();
                int hand = r.ReadVarInt();
                float cx = floatCursor ? r.ReadFloat() : r.ReadByte() / LegacyCursorScale;
                float cy = floatCursor ? r.ReadFloat() : r.ReadByte() / LegacyCursorScale;
                float cz = floatCursor ? r.ReadFloat() : r.ReadByte() / LegacyCursorScale;
                return new ServerboundUseItemOnPacket(hand, pos, face, cx, cy, cz, Inside: false, WorldBorderHit: false, Sequence: 0);
            });

    internal static PacketCodec<ServerboundContainerClickPacket> MakeContainerClickPre114(bool presentIdStack) =>
        PacketCodec<ServerboundContainerClickPacket>.Of(
            (ref PacketWriter w, ServerboundContainerClickPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteShort(p.Slot);
                w.WriteByte(p.Button);
                w.WriteShort(p.ActionNumber);
                w.WriteByte((byte)p.Mode);
                ItemStack item = p.LegacyClickedItem ?? ItemStack.Empty;
                if (presentIdStack) ItemStackCodecs.WritePresentIdStack(ref w, item, c);
                else ItemStackCodecs.WriteShortIdStack(ref w, item, c);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                short slot = r.ReadShort();
                byte button = r.ReadByte();
                short action = r.ReadShort();
                byte mode = r.ReadByte();
                ItemStack item = presentIdStack ? ItemStackCodecs.ReadPresentIdStack(ref r, c) : ItemStackCodecs.ReadShortIdStack(ref r, c);
                return new ServerboundContainerClickPacket(id, 0, slot, button, mode, action, item, [], null);
            });

    internal static PacketCodec<ServerboundSetCreativeModeSlotPacket> MakeCreativeSlotPre114(bool present) =>
        PacketCodec<ServerboundSetCreativeModeSlotPacket>.Of(
            (ref PacketWriter w, ServerboundSetCreativeModeSlotPacket p, PacketCodecContext c) =>
            {
                w.WriteShort(p.Slot);
                if (present) ItemStackCodecs.WritePresentIdStack(ref w, p.Item, c);
                else ItemStackCodecs.WriteShortIdStack(ref w, p.Item, c);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                short slot = r.ReadShort();
                ItemStack item = present ? ItemStackCodecs.ReadPresentIdStack(ref r, c) : ItemStackCodecs.ReadShortIdStack(ref r, c);
                return new ServerboundSetCreativeModeSlotPacket(slot, item);
            });

    internal static PacketCodec<ClientboundMerchantOffersPacket> MakeMerchantOffers(ItemComponentTable table) =>
        PacketCodec<ClientboundMerchantOffersPacket>.Of(
            (ref PacketWriter w, ClientboundMerchantOffersPacket p, PacketCodecContext c) =>
            {
                w.WriteVarInt(p.ContainerId);
                MerchantOffers offers = p.Offers;
                w.WriteVarInt(offers.Offers.Count);
                foreach (MerchantOffer offer in offers.Offers)
                {
                    WriteItemCost(ref w, offer.BaseFirstCost, c, table);
                    ItemStackCodecs.WriteModernStack(ref w, offer.Result, c, table);
                    if (offer.SecondCost is { IsEmpty: false } second)
                    {
                        w.WriteBool(true);
                        WriteItemCost(ref w, second, c, table);
                    }
                    else
                        w.WriteBool(false);

                    w.WriteBool(offer.IsSoldOut);
                    w.WriteInt(offer.Uses);
                    w.WriteInt(offer.MaxUses);
                    w.WriteInt(offer.Xp);
                    w.WriteInt(offer.SpecialPrice);
                    w.WriteFloat(offer.PriceMultiplier);
                    w.WriteInt(offer.Demand);
                }

                w.WriteVarInt(offers.VillagerLevel);
                w.WriteVarInt(offers.Experience);
                w.WriteBool(offers.IsRegularVillager);
                w.WriteBool(offers.CanRestock);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadVarInt();
                int count = r.ReadVarInt();
                var list = new List<MerchantOffer>(count);
                for (int i = 0; i < count; i++)
                {
                    ItemStack baseCost = ReadItemCost(ref r, c, table);
                    ItemStack result = ItemStackCodecs.ReadModernStack(ref r, c, table);
                    ItemStack? second = r.ReadBool() ? ReadItemCost(ref r, c, table) : null;
                    bool soldOut = r.ReadBool();
                    int uses = r.ReadInt();
                    int maxUses = r.ReadInt();
                    int xp = r.ReadInt();
                    int specialPrice = r.ReadInt();
                    float multiplier = r.ReadFloat();
                    int demand = r.ReadInt();

                    // The recorded out-of-stock boolean is intentionally not retained. When set, the decoded value forces uses to maxUses, so IsSoldOut re-emits the same boolean.
                    _ = soldOut;
                    list.Add(new MerchantOffer(baseCost, baseCost, second, result, uses, maxUses, xp, multiplier, specialPrice, demand));
                }

                int villagerLevel = r.ReadVarInt();
                int experience = r.ReadVarInt();
                bool regular = r.ReadBool();
                bool restock = r.ReadBool();
                return new ClientboundMerchantOffersPacket(id, new MerchantOffers(list, villagerLevel, experience, regular, restock));
            },
            WireShape.Of("varint,varint*offer,varint,varint,bool,bool", table.ShapeToken));

    // ItemCost = item holder id, VarInt count, DataComponentExactPredicate (a list of typed components). The base cost is modeled as an ItemStack with those predicate components; the count is the stack count. <remarks> Every predicate component is routed through the full component codec table, matching ItemStackCodecs.ReadPatch and the WriteItemCost write path, so all table components survive decode and re-encode. </remarks> On an unknown item id the codec throws (never defaults or forwards): a trade cost with an item id outside the bound registry is a wire/registry mismatch the session cannot faithfully model. This matches ReadModernStack and is the deliberate FailConnection policy for a client build; a proxy build selects a ForwardVerbatim codec instead. An unknown live id indicates an incomplete registry dataset rather than a codec fallback case.
    internal static ItemStack ReadItemCost(ref PacketReader reader, PacketCodecContext context, ItemComponentTable table)
    {
        int itemId = ItemCodecPrimitives.ReadHolderId(ref reader);
        int count = reader.ReadVarInt();
        int predicateCount = reader.ReadVarInt();
        var patch = new List<DataComponentEntry>(predicateCount);
        for (int i = 0; i < predicateCount; i++)
        {
            int componentId = reader.ReadVarInt();

            // DataComponentExactPredicate is a COMPACT component list (no per-payload length prefix), so it shares the item patch's recovery story: an unmodeled component costs this packet and not the session, while an out-of-range id stays a fatal framing fault. Same single gate.
            ItemComponentCodec codec = table.CompactCodec(componentId);
            object value = codec.Decode(ref reader, context);
            patch.Add(DataComponentEntry.Set(codec.Type, value));
        }

        if (!context.Registries.Items.TryGet(itemId, out var item))
            throw new ProtocolViolationException($"Trade cost item id {itemId} is not in the item registry.");

        DataComponentMap components = patch.Count == 0
            ? DataComponentMap.Empty
            : DataComponentMap.Create(EmptyComponentPrototype, patch);
        return new ItemStack(item, count, components);
    }

    internal static readonly IReadOnlyDictionary<DataComponentType, object> EmptyComponentPrototype =
        new Dictionary<DataComponentType, object>();

    internal static void WriteItemCost(ref PacketWriter writer, ItemStack cost, PacketCodecContext context, ItemComponentTable table)
    {
        ItemCodecPrimitives.WriteHolderId(ref writer, cost.Item.NetworkId);
        writer.WriteVarInt(cost.Count);
        IReadOnlyList<DataComponentEntry> patch = cost.Components.Patch;
        int typedCount = 0;
        foreach (DataComponentEntry entry in patch)
            if (!entry.IsRemoval && table.Contains(entry.Type))
                typedCount++;

        writer.WriteVarInt(typedCount);
        foreach (DataComponentEntry entry in patch)
        {
            if (entry.IsRemoval || !table.Contains(entry.Type))
                continue;

            writer.WriteVarInt(table.WireId(entry.Type));
            table.ByKey(entry.Type).Encode(ref writer, entry.Value!, context);
        }
    }

    internal delegate ItemStack StackReader(ref PacketReader reader, PacketCodecContext context);

    internal delegate void StackWriter(ref PacketWriter writer, ItemStack stack, PacketCodecContext context);

    // Stack strategies

    // The 764/765 stack: present bool, VarInt id, byte count, UNNAMED-root network NBT. Valid only from 1.20.2 (protocol 764), where the network form dropped the root name. Anything below 764 must use the present-id pair below, whose NBT root is named.
    internal static readonly StackReader ReadVarIntId = ItemStackCodecs.ReadVarIntIdStack;

    internal static readonly StackWriter WriteVarIntId = ItemStackCodecs.WriteVarIntIdStack;

    // The 404-763 (1.13.2-1.20.1) stack: byte-for-byte the same fields as the pair above, but the NBT root is NAMED (type byte, empty root-name string, body). See ItemStackCodecs.ReadPresentIdStack.
    internal static readonly StackReader ReadPresentId = ItemStackCodecs.ReadPresentIdStack;

    internal static readonly StackWriter WritePresentId = ItemStackCodecs.WritePresentIdStack;

    /// <summary>The protocol 766 component era table.</summary>
    internal static ItemComponentTable Table766 { get; } = ItemComponentTable.V1_20_5();

    /// <summary>The protocol 767 component era table.</summary>
    internal static ItemComponentTable Table767 { get; } = ItemComponentTable.V1_21();

    internal static StackReader ComponentReader(ItemComponentTable table) =>
        (ref PacketReader r, PacketCodecContext c) => ItemStackCodecs.ReadModernStack(ref r, c, table);

    internal static StackWriter ComponentWriter(ItemComponentTable table) =>
        (ref PacketWriter w, ItemStack s, PacketCodecContext c) => ItemStackCodecs.WriteModernStack(ref w, s, c, table);

    /// <summary>The 26.1+ nested stack form: holder id, VarInt count, component patch. This is not the count-first <see cref="ComponentReader"/> form; the two swap their first two fields, and neither shape is self-describing, so a frame read with the wrong one round-trips byte-identically while reporting a different item and a different count.</summary>
    internal static StackReader TemplateReader(ItemComponentTable table) =>
        (ref PacketReader r, PacketCodecContext c) => ItemStackCodecs.ReadTemplateStack(ref r, c, table);

    /// <summary>The write half of <see cref="TemplateReader"/>.</summary>
    internal static StackWriter TemplateWriter(ItemComponentTable table) =>
        (ref PacketWriter w, ItemStack s, PacketCodecContext c) => ItemStackCodecs.WriteTemplateStack(ref w, s, c, table);

    /// <summary>The era's item-stack wire form, as the reader and writer that express it. They travel together because they are each other's inverse: a factory handed one era's reader and a neighbour's writer decodes a frame it cannot re-encode, and nothing in a signature taking two loose delegates says they have to agree.</summary>
    /// <param name="Read">The era's stack reader.</param>
    /// <param name="Write">The era's stack writer, which must be the reader's inverse.</param>
    /// <param name="Form">What the pair reads, named once here. The two halves are delegates, and a delegate's own method name is a C# name, so deriving the token from them would put a rename back into the shape column that the column exists to keep out.</param>
    internal readonly record struct StackWire(StackReader Read, StackWriter Write, string Form)
    {
        /// <summary>47-340: the pre-flattening (id, count, damage, NBT) stack.</summary>
        internal static StackWire Legacy { get; } =
            new(ItemStackCodecs.ReadLegacyStack, ItemStackCodecs.WriteLegacyStack, "legacy");

        /// <summary>47-401: the (id, damage) stack with a short id and a named NBT root.</summary>
        internal static StackWire ShortId { get; } =
            new(ItemStackCodecs.ReadShortIdStack, ItemStackCodecs.WriteShortIdStack, "shortid");

        /// <summary>404-763: present bool, VarInt id, byte count, NAMED-root NBT.</summary>
        internal static StackWire PresentId { get; } = new(ReadPresentId, WritePresentId, "presentid");

        /// <summary>764/765: the same fields as <see cref="PresentId"/> over an UNNAMED NBT root.</summary>
        internal static StackWire VarIntId { get; } = new(ReadVarIntId, WriteVarIntId, "varintid");

        /// <summary>766+: the count-first component stack, under one era's component table.</summary>
        /// <param name="table">The era's component table.</param>
        /// <returns>The pair.</returns>
        internal static StackWire Components(ItemComponentTable table) =>
            new(ComponentReader(table), ComponentWriter(table), "countfirst/" + table.ShapeToken);

        /// <summary>775+: the template-stack form, under one era's component table.</summary>
        /// <param name="table">The era's component table.</param>
        /// <returns>The pair.</returns>
        internal static StackWire Templates(ItemComponentTable table) =>
            new(TemplateReader(table), TemplateWriter(table), "template/" + table.ShapeToken);

        /// <inheritdoc />
        public override string ToString() => Form;
    }

    /// <summary>The set-slot body, in every shape the packet has had: container id, an optional state id, the slot short and one item stack. Keeping the shared framing here prevents stack-era changes from moving the surrounding fields.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="p">The packet.</param>
    /// <param name="c">The codec context.</param>
    /// <param name="stacks">The era's item-stack wire form.</param>
    /// <param name="containerIdIsVarInt">True from 1.21.2, where the container id is a VarInt. Below that it is a signed byte, and the sign carries meaning: -1 is the carried stack and -2 writes into the player inventory.</param>
    /// <param name="withStateId">True from 1.17.1, which inserted the state-id VarInt after the id.</param>
    internal static void WriteSetSlot(
        ref PacketWriter w,
        ClientboundContainerSetSlotPacket p,
        PacketCodecContext c,
        StackWire stacks,
        bool containerIdIsVarInt,
        bool withStateId)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (containerIdIsVarInt)
            w.WriteVarInt(p.ContainerId);

        else
            w.WriteByte((byte)(sbyte)p.ContainerId);

        if (withStateId)
            w.WriteVarInt(p.StateId);

        w.WriteShort((short)p.Slot);
        stacks.Write(ref w, p.Item, c);
    }

    /// <summary>The read half of <see cref="WriteSetSlot"/>.</summary>
    /// <param name="r">The reader.</param>
    /// <param name="c">The codec context.</param>
    /// <param name="stacks">The era's item-stack wire form.</param>
    /// <param name="containerIdIsVarInt">True from 1.21.2; below that a signed byte.</param>
    /// <param name="withStateId">True from 1.17.1.</param>
    /// <param name="isLegacy">True only for 1.8's <c>minecraft:set_slot</c>, whose record carries the flag so it resolves back to the legacy packet type on the way out.</param>
    /// <returns>The decoded packet.</returns>
    internal static ClientboundContainerSetSlotPacket ReadSetSlot(
        ref PacketReader r,
        PacketCodecContext c,
        StackWire stacks,
        bool containerIdIsVarInt,
        bool withStateId,
        bool isLegacy = false)
    {
        int id = containerIdIsVarInt ? r.ReadVarInt() : r.ReadSByte();
        int state = withStateId ? r.ReadVarInt() : 0;
        short slot = r.ReadShort();
        ItemStack item = stacks.Read(ref r, c);
        return new ClientboundContainerSetSlotPacket(id, state, slot, item, isLegacy);
    }

    internal static PacketCodec<ClientboundContainerSetSlotPacket> MakeSetSlot(StackWire stacks) =>
        PacketCodec<ClientboundContainerSetSlotPacket>.Of(
            (ref PacketWriter w, ClientboundContainerSetSlotPacket p, PacketCodecContext c) =>
                WriteSetSlot(ref w, p, c, stacks, containerIdIsVarInt: false, withStateId: true),
            (ref PacketReader r, PacketCodecContext c) =>
                ReadSetSlot(ref r, c, stacks, containerIdIsVarInt: false, withStateId: true),
            WireShape.Of("sbyte,varint,short,stack", stacks.Form));

    internal static PacketCodec<ClientboundContainerSetContentPacket> MakeSetContent(StackWire stacks) =>
        PacketCodec<ClientboundContainerSetContentPacket>.Of(
            (ref PacketWriter w, ClientboundContainerSetContentPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteVarInt(p.StateId);
                w.WriteVarInt(p.Items.Count);
                foreach (ItemStack stack in p.Items)
                    stacks.Write(ref w, stack, c);

                stacks.Write(ref w, p.CarriedItem, c);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadByte();
                int state = r.ReadVarInt();
                int count = r.ReadVarInt();
                var items = new ItemStack[count];
                for (int i = 0; i < count; i++)
                    items[i] = stacks.Read(ref r, c);

                ItemStack carried = stacks.Read(ref r, c);
                return new ClientboundContainerSetContentPacket(id, state, items, carried);
            },
            WireShape.Of("byte,varint,varint*stack,stack", stacks.Form));

    // The full-stack (non-hashed) container-click. 1.17 introduced the changed-slots sync map + carried stack but NOT the state id; 1.17.1 added the state id. Pass withStateId: false for the 1.17-only form.
    internal static PacketCodec<ServerboundContainerClickPacket> MakeClick(StackWire stacks, bool withStateId = true) =>
        PacketCodec<ServerboundContainerClickPacket>.Of(
            (ref PacketWriter w, ServerboundContainerClickPacket p, PacketCodecContext c) =>
            {
                w.WriteByte((byte)(sbyte)p.ContainerId);
                if (withStateId)
                    w.WriteVarInt(p.StateId);

                w.WriteShort(p.Slot);
                w.WriteByte(p.Button);
                w.WriteVarInt(p.Mode);
                w.WriteVarInt(p.ChangedSlots.Count);
                foreach (PredictedSlot slot in p.ChangedSlots)
                {
                    w.WriteShort(slot.Slot);
                    stacks.Write(ref w, slot.Stack, c);
                }

                stacks.Write(ref w, p.CarriedItem ?? ItemStack.Empty, c);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadSByte();
                int state = withStateId ? r.ReadVarInt() : 0;
                short slot = r.ReadShort();
                byte button = r.ReadByte();
                int mode = r.ReadVarInt();
                int changedCount = r.ReadVarInt();
                var changed = new PredictedSlot[changedCount];
                for (int i = 0; i < changedCount; i++)
                {
                    short changedSlot = r.ReadShort();
                    changed[i] = new PredictedSlot(changedSlot, stacks.Read(ref r, c));
                }

                ItemStack carried = stacks.Read(ref r, c);

                // Unlike the hashed 1.21.5+ form, the full predicted stacks reconstruct losslessly.
                return new ServerboundContainerClickPacket(id, state, slot, button, mode, ActionNumber: 0, null, changed, carried);
            },
            WireShape.Of(
                withStateId
                    ? "sbyte,varint,short,byte,varint,varint*(short,stack),stack"
                    : "sbyte,short,byte,varint,varint*(short,stack),stack",
                stacks.Form));

    internal static PacketCodec<ServerboundSetCreativeModeSlotPacket> MakeCreativeSlot(StackWire stacks) =>
        PacketCodec<ServerboundSetCreativeModeSlotPacket>.Of(
            (ref PacketWriter w, ServerboundSetCreativeModeSlotPacket p, PacketCodecContext c) =>
            {
                w.WriteShort(p.Slot);
                stacks.Write(ref w, p.Item, c);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                short slot = r.ReadShort();
                ItemStack item = stacks.Read(ref r, c);
                return new ServerboundSetCreativeModeSlotPacket(slot, item);
            },
            WireShape.Of("short,stack", stacks.Form));

    // The pre-component merchant-offers payload (1.14-1.20.3), parameterised on the era's item stack, on the offer-list framing, and on whether the packet frame carries the trailing canRestock bool. The frame is VarInt containerId, the offer list, VarInt villagerLevel, VarInt villagerXp, bool showProgress, and (from protocol 490) bool canRestock.
    //
    // LegacyList true uses an unsigned-byte offer count and represents the second cost as a presence boolean plus an optional stack. Protocol 758 is the final release with this form. LegacyList false uses a VarInt offer count and writes the second-cost stack unconditionally, beginning at protocol 759. WithDemand adds a trailing demand int to each offer from protocol 498. Earlier offers end after priceMultiplier; decoding them supplies Demand = 0 and encoding drops that unavailable field. WithRestock adds the packet's second trailing boolean at protocol 490, one era before demand.
    //             A codec that reads two bools on 477-485 runs one byte past the end of a real villager
    //             open and the decode fault ENDS THE SESSION; measured live on 1.14.2 as
    //             "Decoding minecraft:merchant_offers (wire 0x27): Packet decode ran past the end of the
    //             payload (needed 1 byte(s) at offset 37, 0 remaining)". An offer decoded without the
    //             field carries CanRestock = false, which is vanilla's own value for a villager on a
    //             version that has no restocking flag on the wire.
    //
    // The two list forms agree byte-for-byte whenever every trade's second cost is EMPTY: form A writes both write a false presence marker, which is the same single 0x00. They diverge by exactly one byte per trade that HAS a second input cost, so only an offer carrying a second cost can distinguish them.
    internal static PacketCodec<ClientboundMerchantOffersPacket> MakeMerchantOffersPreComponent(
        StackWire stacks, MerchantOffersWire era) =>
        PacketCodec<ClientboundMerchantOffersPacket>.Of(
            (ref PacketWriter w, ClientboundMerchantOffersPacket p, PacketCodecContext c) =>
            {
                w.WriteVarInt(p.ContainerId);
                MerchantOffers offers = p.Offers;
                if (era.LegacyList)
                {
                    // The legacy count carries only the low eight bits.
                    w.WriteByte((byte)(offers.Offers.Count & 0xFF));
                }
                else
                    w.WriteVarInt(offers.Offers.Count);

                foreach (MerchantOffer offer in offers.Offers)
                {
                    stacks.Write(ref w, offer.BaseFirstCost, c);
                    stacks.Write(ref w, offer.Result, c);
                    ItemStack secondCost = offer.SecondCost ?? ItemStack.Empty;
                    if (era.LegacyList)
                    {
                        w.WriteBool(!secondCost.IsEmpty);
                        if (!secondCost.IsEmpty)
                            stacks.Write(ref w, secondCost, c);

                    }
                    else
                        stacks.Write(ref w, secondCost, c);

                    w.WriteBool(offer.IsSoldOut);
                    w.WriteInt(offer.Uses);
                    w.WriteInt(offer.MaxUses);
                    w.WriteInt(offer.Xp);
                    w.WriteInt(offer.SpecialPrice);
                    w.WriteFloat(offer.PriceMultiplier);
                    if (era.WithDemand)
                        w.WriteInt(offer.Demand);

                }

                w.WriteVarInt(offers.VillagerLevel);
                w.WriteVarInt(offers.Experience);
                w.WriteBool(offers.IsRegularVillager);
                if (era.WithRestock)
                    w.WriteBool(offers.CanRestock);

            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadVarInt();
                int count = era.LegacyList ? r.ReadByte() : r.ReadVarInt();
                var list = new List<MerchantOffer>(count);
                for (int i = 0; i < count; i++)
                {
                    ItemStack baseCost = stacks.Read(ref r, c);
                    ItemStack result = stacks.Read(ref r, c);
                    ItemStack costB = era.LegacyList
                        ? (r.ReadBool() ? stacks.Read(ref r, c) : ItemStack.Empty)
                        : stacks.Read(ref r, c);
                    bool soldOut = r.ReadBool();
                    int uses = r.ReadInt();
                    int maxUses = r.ReadInt();
                    int xp = r.ReadInt();
                    int specialPrice = r.ReadInt();
                    float multiplier = r.ReadFloat();
                    int demand = era.WithDemand ? r.ReadInt() : 0;

                    // Same IsSoldOut re-derivation as the component-era members (vanilla forces uses = maxUses on read when the bool is set, so MerchantOffer.IsSoldOut re-emits it).
                    _ = soldOut;
                    ItemStack? second = costB.IsEmpty ? null : costB;
                    list.Add(new MerchantOffer(baseCost, baseCost, second, result, uses, maxUses, xp, multiplier, specialPrice, demand));
                }

                int level = r.ReadVarInt();
                int experience = r.ReadVarInt();
                bool regular = r.ReadBool();
                bool restock = era.WithRestock && r.ReadBool();
                return new ClientboundMerchantOffersPacket(id, new MerchantOffers(list, level, experience, regular, restock));
            },
            WireShape.Of("varint,offers,varint,varint,bool", $"{era},{stacks.Form}"));

    // The ItemCost trade form (1.20.5+). This mirrors MakeMerchantOffers byte-for-byte; it is re-stated here because the 764-767 eras bind their own component tables. Each cost is a holder id, VarInt count, and exact-component predicate.
    internal static PacketCodec<ClientboundMerchantOffersPacket> MakeMerchantOffersItemCost(ItemComponentTable table) =>
        PacketCodec<ClientboundMerchantOffersPacket>.Of(
            (ref PacketWriter w, ClientboundMerchantOffersPacket p, PacketCodecContext c) =>
            {
                w.WriteVarInt(p.ContainerId);
                MerchantOffers offers = p.Offers;
                w.WriteVarInt(offers.Offers.Count);
                foreach (MerchantOffer offer in offers.Offers)
                {
                    WriteItemCost(ref w, offer.BaseFirstCost, c, table);
                    ItemStackCodecs.WriteModernStack(ref w, offer.Result, c, table);
                    if (offer.SecondCost is { IsEmpty: false } second)
                    {
                        w.WriteBool(true);
                        WriteItemCost(ref w, second, c, table);
                    }
                    else
                        w.WriteBool(false);

                    w.WriteBool(offer.IsSoldOut);
                    w.WriteInt(offer.Uses);
                    w.WriteInt(offer.MaxUses);
                    w.WriteInt(offer.Xp);
                    w.WriteInt(offer.SpecialPrice);
                    w.WriteFloat(offer.PriceMultiplier);
                    w.WriteInt(offer.Demand);
                }

                w.WriteVarInt(offers.VillagerLevel);
                w.WriteVarInt(offers.Experience);
                w.WriteBool(offers.IsRegularVillager);
                w.WriteBool(offers.CanRestock);
            },
            (ref PacketReader r, PacketCodecContext c) =>
            {
                int id = r.ReadVarInt();
                int count = r.ReadVarInt();
                var list = new List<MerchantOffer>(count);
                for (int i = 0; i < count; i++)
                {
                    ItemStack baseCost = ReadItemCost(ref r, c, table);
                    ItemStack result = ItemStackCodecs.ReadModernStack(ref r, c, table);
                    ItemStack? second = r.ReadBool() ? ReadItemCost(ref r, c, table) : null;
                    bool soldOut = r.ReadBool();
                    int uses = r.ReadInt();
                    int maxUses = r.ReadInt();
                    int xp = r.ReadInt();
                    int specialPrice = r.ReadInt();
                    float multiplier = r.ReadFloat();
                    int demand = r.ReadInt();
                    _ = soldOut;
                    list.Add(new MerchantOffer(baseCost, baseCost, second, result, uses, maxUses, xp, multiplier, specialPrice, demand));
                }

                int level = r.ReadVarInt();
                int experience = r.ReadVarInt();
                bool regular = r.ReadBool();
                bool restock = r.ReadBool();
                return new ClientboundMerchantOffersPacket(id, new MerchantOffers(list, level, experience, regular, restock));
            },
            WireShape.Of("varint,varint*item_cost_offer,varint,varint,bool,bool", table.ShapeToken));
}
