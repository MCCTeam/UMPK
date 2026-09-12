using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Nbt;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The item-stack payload codecs, the centerpiece of the item family. Two wire eras: the 1.8 legacy slot (short id or -1, byte count, short damage, NBT-or-0, with the <c>(id&lt;&lt;16)|damage</c> composite item identity) and the 1.20.5+ component stack (VarInt count-first empty semantics, item holder id, add/remove component patch). Both resolve the item against the session item registry; the component patch dispatches per-component payloads through the per-era <see cref="ItemComponentTable"/>. The 1.21.5+ hashed-stack form (container clicks) hashes each component through <see cref="HashOps"/> rather than resending the payload.</summary>
/// <remarks>Frame-exact: a decode overrun, or an unknown item id on a flattened reader, raises <see cref="ProtocolViolationException"/>. The one exception is the legacy composite reader, where an unmapped id degrades to a named placeholder that still re-encodes byte-for-byte (see <c>ResolveLegacyItem</c>).</remarks>
internal static partial class ItemStackCodecs
{
    // Legacy 1.8 item stack

    /// <summary>Reads a 1.8 legacy item slot (short id or -1, byte count, short damage, NBT-or-0).</summary>
    public static ItemStack ReadLegacyStack(ref PacketReader reader, PacketCodecContext context)
    {
        short id = reader.ReadShort();
        if (id < 0)
            return ItemStack.Empty;

        byte count = reader.ReadByte();
        short damage = reader.ReadShort();
        NbtTag nbt = reader.ReadNbt(NbtWireFormat.JavaNamedRoot);

        RegistryEntry<ItemDefinition> item = ResolveLegacyItem(context, id, damage, out int carriedDamage);
        DataComponentMap components = DataComponentMap.Empty;
        if (carriedDamage != 0)
            components = components.With(DataComponents.Damage, new DamageComponent(carriedDamage));

        if (nbt is NbtCompound compound && compound.Count > 0)
            components = components.With(DataComponents.LegacyNbt, new LegacyNbtComponent(compound));

        return new ItemStack(item, count, components);
    }

    /// <summary>Writes a 1.8 legacy item slot.</summary>
    public static void WriteLegacyStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context)
    {
        if (stack.IsEmpty)
        {
            writer.WriteShort(-1);
            return;
        }

        int composite = stack.Item.NetworkId;
        int itemId = composite >> 16;
        int baseDamage = composite & 0xFFFF;
        int wireDamage = baseDamage;
        if (baseDamage == 0 && stack.Components.TryGet(DataComponents.Damage, out DamageComponent? damage))
            wireDamage = damage.Value;

        writer.WriteShort((short)itemId);
        writer.WriteByte((byte)stack.Count);
        writer.WriteShort((short)wireDamage);

        if (stack.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy))
            writer.WriteNbt(legacy.Nbt, NbtWireFormat.JavaNamedRoot);

        else
            writer.WriteNbt(NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);

    }

    /// <summary>The identifier an unresolvable legacy item degrades to, matching unresolved entity types.</summary>
    private static readonly Identifier UnknownItem = Identifier.Minecraft("unknown");

    /// <summary>The definition the placeholder carries; the real stack size of an unknown item is unknowable.</summary>
    private static readonly ItemDefinition UnknownItemDefinition = new();

    /// <summary>Resolves a legacy <c>(id, damage)</c> pair: the exact composite subtype first, then the base item with the damage carried as a component, then a named placeholder. The placeholder is the deliberate policy for this era: the pre-flattening id space has a long tail (server-side modifications, curated-dataset gaps) and one unmapped slot in a join inventory must never drop the whole session. It keeps the original composite as its network id, so <see cref="WriteLegacyStack"/> re-emits the exact id/damage pair and a decode/re-encode round trip stays byte-exact. The flattened readers keep their strict policy: their registries are gapless by construction, so an unresolvable id there is a real desync.</summary>
    private static RegistryEntry<ItemDefinition> ResolveLegacyItem(PacketCodecContext context, int itemId, int damage, out int carriedDamage)
    {
        Registry<ItemDefinition> items = context.Registries.Items;
        int composite = ((itemId & 0xFFFF) << 16) | (damage & 0xFFFF);
        if (items.TryGet(composite, out RegistryEntry<ItemDefinition> subtype))
        {
            carriedDamage = 0;
            return subtype;
        }

        int baseComposite = (itemId & 0xFFFF) << 16;
        if (items.TryGet(baseComposite, out RegistryEntry<ItemDefinition> baseItem))
        {
            carriedDamage = damage;
            return baseItem;
        }

        // The placeholder's own identity carries the damage (its network id is the full composite), so there is nothing left to carry as a component.
        ConnectionDiagnostics.UnknownItems.Add(1);
        carriedDamage = 0;
        return Registry.Direct(composite, UnknownItem, UnknownItemDefinition);
    }

    // 1.13 and 1.13.1 short-id item stack

    /// <summary>Reads a 1.13/1.13.1 item slot: short id (-1 = empty), byte count, named-root optional NBT. The flattening dropped the separate 1.8 damage short, so this differs from the legacy stack.</summary>
    public static ItemStack ReadShortIdStack(ref PacketReader reader, PacketCodecContext context)
    {
        short id = reader.ReadShort();
        if (id < 0)
            return ItemStack.Empty;

        byte count = reader.ReadByte();
        NbtTag nbt = reader.ReadNbt(NbtWireFormat.JavaNamedRoot);
        if (!context.Registries.Items.TryGet(id, out RegistryEntry<ItemDefinition> item))
            throw new ProtocolViolationException($"Item id {id} is not in the item registry.");

        DataComponentMap components = DataComponentMap.Empty;
        if (nbt is NbtCompound compound && compound.Count > 0)
            components = components.With(DataComponents.LegacyNbt, new LegacyNbtComponent(compound));

        return new ItemStack(item, count, components);
    }

    /// <summary>Writes a 1.13/1.13.1 item slot (short id, byte count, named-root optional NBT).</summary>
    public static void WriteShortIdStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context)
    {
        if (stack.IsEmpty)
        {
            writer.WriteShort(-1);
            return;
        }

        writer.WriteShort((short)stack.Item.NetworkId);
        writer.WriteByte((byte)stack.Count);
        WriteNamedItemNbt(ref writer, stack);
    }

    // 1.13.2 present-flag and VarInt-id item stack

    /// <summary>Reads a 1.13.2-1.20.1 item slot (protocols 404-763): bool present, then VarInt id, byte count, NAMED-root optional NBT. This is the 1.13.2 Slot format flip away from the 1.13/1.13.1 short-id form, and the fields do not change again until 1.20.5 brings structured components.</summary>
    /// <remarks>The NBT root framing is what separates this from <see cref="ReadVarIntIdStack"/>, which is otherwise identical: through protocol 763 the tag has a named root (type byte, empty root-name string, body); protocol 764 switched to an unnamed root. A null tag is a bare TAG_End byte in BOTH forms, which is exactly why a stack with no NBT decodes identically under either reading and why empty-stack round-trips cannot catch a mismatch here.</remarks>
    public static ItemStack ReadPresentIdStack(ref PacketReader reader, PacketCodecContext context)
    {
        if (!reader.ReadBool())
            return ItemStack.Empty;

        int itemId = reader.ReadVarInt();
        byte count = reader.ReadByte();
        NbtTag nbt = reader.ReadNbt(NbtWireFormat.JavaNamedRoot);
        if (!context.Registries.Items.TryGet(itemId, out RegistryEntry<ItemDefinition> item))
            throw new ProtocolViolationException($"Item id {itemId} is not in the item registry.");

        DataComponentMap components = DataComponentMap.Empty;
        if (nbt is NbtCompound compound && compound.Count > 0)
            components = components.With(DataComponents.LegacyNbt, new LegacyNbtComponent(compound));

        return new ItemStack(item, count, components);
    }

    /// <summary>Writes a 1.13.2-1.20.1 item slot (present bool, VarInt id, byte count, NAMED-root optional NBT).</summary>
    public static void WritePresentIdStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context)
    {
        if (stack.IsEmpty)
        {
            writer.WriteBool(false);
            return;
        }

        writer.WriteBool(true);
        writer.WriteVarInt(stack.Item.NetworkId);
        writer.WriteByte((byte)stack.Count);
        WriteNamedItemNbt(ref writer, stack);
    }

    private static void WriteNamedItemNbt(ref PacketWriter writer, ItemStack stack)
    {
        if (stack.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy))
            writer.WriteNbt(legacy.Nbt, NbtWireFormat.JavaNamedRoot);

        else
            writer.WriteNbt(NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);

    }

    // Modern 1.20.5+ item stack

    /// <summary>The 1.21.5 component era (protocol 770 component id table).</summary>
    public static ItemComponentTable ComponentsV1_21_5 { get; } = ItemComponentTable.V1_21_5();

    /// <summary>The 26.2 component era (protocol 776 component id table, attribute-modifier display field).</summary>
    public static ItemComponentTable ComponentsV26_2 { get; } = ItemComponentTable.V26_2();

    /// <summary>Reads a modern component-based item stack under the given component era table.</summary>
    public static ItemStack ReadModernStack(ref PacketReader reader, PacketCodecContext context, ItemComponentTable table) =>
        ReadModernStackCore(ref reader, context, table, StackTrust.Trusted);

    /// <summary>Writes a modern component-based item stack under the given component era table.</summary>
    public static void WriteModernStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context, ItemComponentTable table) =>
        WriteModernStackCore(ref writer, stack, context, table, StackTrust.Trusted);

    /// <summary>Reads a modern stack whose component patch is length-delimited (the untrusted form).</summary>
    public static ItemStack ReadDelimitedStack(ref PacketReader reader, PacketCodecContext context, ItemComponentTable table) =>
        ReadModernStackCore(ref reader, context, table, StackTrust.Untrusted);

    /// <summary>Writes a modern stack whose component patch is length-delimited (the untrusted form).</summary>
    public static void WriteDelimitedStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context, ItemComponentTable table) =>
        WriteModernStackCore(ref writer, stack, context, table, StackTrust.Untrusted);

    private static ItemStack ReadModernStackCore(ref PacketReader reader, PacketCodecContext context, ItemComponentTable table, StackTrust trust)
    {
        int count = reader.ReadVarInt();
        if (count <= 0)
            return ItemStack.Empty;

        int itemId = ItemCodecPrimitives.ReadHolderId(ref reader);
        if (!context.Registries.Items.TryGet(itemId, out RegistryEntry<ItemDefinition> item))
            throw new ProtocolViolationException($"Item id {itemId} is not in the item registry.");

        DataComponentMap components = ReadPatch(ref reader, context, table, trust);
        return new ItemStack(item, count, components);
    }

    private static void WriteModernStackCore(ref PacketWriter writer, ItemStack stack, PacketCodecContext context, ItemComponentTable table, StackTrust trust)
    {
        if (stack.IsEmpty)
        {
            writer.WriteVarInt(0);
            return;
        }

        writer.WriteVarInt(stack.Count);
        ItemCodecPrimitives.WriteHolderId(ref writer, stack.Item.NetworkId);
        WritePatch(ref writer, stack.Components, context, table, trust);
    }

    private static DataComponentMap ReadPatch(ref PacketReader reader, PacketCodecContext context, ItemComponentTable table, StackTrust trust)
    {
        int added = reader.ReadVarInt();
        int removed = reader.ReadVarInt();
        if (added == 0 && removed == 0)
            return DataComponentMap.Empty;

        var patch = new List<DataComponentEntry>(added + removed);

        // A removal patch entry only survives DataComponentMap.Patch when the map's prototype carries the component (a removal against nothing is meaningless). To keep decode->encode frame-exact we seed the reconstructed map's prototype with each removed key so the re-encode re-emits the removal.
        Dictionary<DataComponentType, object>? removalPrototype = null;
        for (int i = 0; i < added; i++)
        {
            int componentId = reader.ReadVarInt();
            if (trust == StackTrust.Untrusted)
            {
                // The untrusted patch length-prefixes each payload, so an unmodeled component is fully recoverable here: capture its bytes verbatim and carry on. Nothing is lost and the re-encode is byte-exact.
                int length = reader.ReadVarInt();
                ReadOnlySpan<byte> payload = reader.ReadBytes(length);
                if (!table.TryGetCodec(componentId, out ItemComponentCodec? delimitedCodec))
                {
                    DataComponentType unmodeledType = table.UnmodeledKeyByWireId(componentId);
                    patch.Add(DataComponentEntry.Set(
                        unmodeledType, new UnmodeledComponent(unmodeledType.Id, payload)));
                    continue;
                }

                var inner = new PacketReader(payload);
                object delimitedValue = delimitedCodec.Decode(ref inner, context);
                if (inner.Remaining != 0)
                    throw new ProtocolViolationException(
                        $"Delimited component '{delimitedCodec.Type.Id}' left {inner.Remaining} unread byte(s).");

                patch.Add(DataComponentEntry.Set(delimitedCodec.Type, delimitedValue));
                continue;
            }

            // Compact form: no length prefix, so an unmodeled component makes the rest of this item stream unreadable. CompactCodec is the one gate into the packet-scoped recovery; an out-of-range id still raises ProtocolViolationException and still kills the connection.
            ItemComponentCodec codec = table.CompactCodec(componentId);
            patch.Add(DataComponentEntry.Set(codec.Type, codec.Decode(ref reader, context)));
        }

        for (int i = 0; i < removed; i++)
        {
            int componentId = reader.ReadVarInt();

            // A removal carries only the wire id, no payload, so there is nothing to skip and nothing can desync here. An unmodeled id resolves to the era's stable placeholder key, which re-encodes to the same wire id, so the removal round-trips exactly instead of faulting. Out-of-range still throws from UnmodeledKeyByWireId (a framing fault).
            DataComponentType removedType = table.UnmodeledKeyByWireId(componentId);
            patch.Add(DataComponentEntry.Remove(removedType));
            (removalPrototype ??= [])[removedType] = RemovalPrototypeMarker;
        }

        return DataComponentMap.Create(removalPrototype ?? NoPrototype, patch);
    }

    // A stable placeholder value seeded into a decoded stack's prototype for each removed component, so DataComponentMap.Patch re-emits the removal on re-encode. Never itself serialized.
    private static readonly object RemovalPrototypeMarker = new();

    private static void WritePatch(ref PacketWriter writer, DataComponentMap components, PacketCodecContext context, ItemComponentTable table, StackTrust trust)
    {
        IReadOnlyList<DataComponentEntry> patch = components.Patch;
        int added = 0;
        int removed = 0;
        foreach (DataComponentEntry entry in patch)
            if (IsWireComponent(entry.Type, table))
                if (entry.IsRemoval)
                    removed++;

                else
                    added++;

        writer.WriteVarInt(added);
        writer.WriteVarInt(removed);

        foreach (DataComponentEntry entry in patch)
        {
            if (entry.IsRemoval || !IsWireComponent(entry.Type, table))
                continue;

            writer.WriteVarInt(table.WireId(entry.Type));

            // An unmodeled component carries its payload verbatim (it can only have been captured from a length-delimited patch). Its bytes ARE the component payload, so they go out inline on the trusted form and length-prefixed on the untrusted one, exactly as they came in.
            if (entry.Value is UnmodeledComponent unmodeled)
            {
                if (trust == StackTrust.Untrusted)
                    writer.WriteByteArray(unmodeled.Payload);

                else
                    writer.WriteBytes(unmodeled.Payload);

                continue;
            }

            ItemComponentCodec codec = table.ByKey(entry.Type);
            if (trust == StackTrust.Untrusted)
            {
                var scratch = new System.Buffers.ArrayBufferWriter<byte>();
                var inner = new PacketWriter(scratch);
                codec.Encode(ref inner, entry.Value!, context);
                writer.WriteByteArray(scratch.WrittenSpan);
            }
            else
                codec.Encode(ref writer, entry.Value!, context);

        }

        foreach (DataComponentEntry entry in patch)
            if (entry.IsRemoval && IsWireComponent(entry.Type, table))
                writer.WriteVarInt(table.WireId(entry.Type));

    }

    // The LegacyNbt escape hatch is a model-only carrier (umpk:legacy_nbt); it never goes on the modern wire and is not part of the component table.
    private static bool IsWireComponent(DataComponentType type, ItemComponentTable table) => table.Contains(type);

    private static readonly System.Collections.Generic.IReadOnlyDictionary<DataComponentType, object> NoPrototype =
        new System.Collections.Generic.Dictionary<DataComponentType, object>();

    // Nested item-stack template for 26.1+

    /// <summary>Reads a 26.1+ nested item-stack template: the item HOLDER id first, then a VarInt count, then the component patch. There is no empty sentinel, so this form can never represent an absent stack; where the protocol needs one it adds a leading presence boolean (<see cref="ReadOptionalTemplateStack"/>).</summary>
    /// <remarks>
    /// A zero count or AIR holder is invalid because the template form cannot represent an empty stack.
    /// <para>This is NOT the count-first <see cref="ReadModernStack"/> form. The two swap their first two fields, so a template frame read count-first takes the item id as the count and the count as the item id: for <c>stone</c> (holder 1) with count 2 the wrong reader sees one stone-shaped stack of ONE and then reads the patch header from the wrong offset. Neither shape is self-describing, so only a frame-length or field-value assertion separates them, never a round trip.</para>
    /// </remarks>
    public static ItemStack ReadTemplateStack(ref PacketReader reader, PacketCodecContext context, ItemComponentTable table)
    {
        int itemId = ItemCodecPrimitives.ReadHolderId(ref reader);
        int count = reader.ReadVarInt();
        if (!context.Registries.Items.TryGet(itemId, out RegistryEntry<ItemDefinition> item))
            throw new ProtocolViolationException($"Item id {itemId} is not in the item registry.");

        DataComponentMap components = ReadPatch(ref reader, context, table, StackTrust.Trusted);
        return new ItemStack(item, count, components);
    }

    /// <summary>Writes a 26.1+ nested item-stack template (holder id, VarInt count, component patch).</summary>
    /// <param name="writer">The packet writer.</param>
    /// <param name="stack">The stack; it must not be empty because this form has no empty sentinel.</param>
    /// <param name="context">The codec context.</param>
    /// <param name="table">The component era table.</param>
    /// <exception cref="ProtocolViolationException">The stack is empty.</exception>
    public static void WriteTemplateStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context, ItemComponentTable table)
    {
        if (stack.IsEmpty)
        {
            // This form has no empty sentinel. Faulting here avoids emitting a zero count the peer cannot parse as a template.
            throw new ProtocolViolationException("An item-stack template cannot be empty on this protocol.");
        }

        ItemCodecPrimitives.WriteHolderId(ref writer, stack.Item.NetworkId);
        writer.WriteVarInt(stack.Count);
        WritePatch(ref writer, stack.Components, context, table, StackTrust.Trusted);
    }

    /// <summary>Reads an OPTIONAL 26.1+ template (a leading present bool, then the template), which is what <c>minecraft:container</c> carries per slot from 26.1.</summary>
    /// <param name="reader">The packet reader.</param>
    /// <param name="context">The codec context.</param>
    /// <param name="table">The component era table.</param>
    /// <returns>The stack, or <see cref="ItemStack.Empty"/> when the present bool is false.</returns>
    public static ItemStack ReadOptionalTemplateStack(ref PacketReader reader, PacketCodecContext context, ItemComponentTable table) =>
        reader.ReadBool() ? ReadTemplateStack(ref reader, context, table) : ItemStack.Empty;

    /// <summary>Writes an optional 26.1+ template (present bool, then the template when non-empty).</summary>
    /// <param name="writer">The packet writer.</param>
    /// <param name="stack">The stack; empty writes a single false bool.</param>
    /// <param name="context">The codec context.</param>
    /// <param name="table">The component era table.</param>
    public static void WriteOptionalTemplateStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context, ItemComponentTable table)
    {
        if (stack.IsEmpty)
        {
            writer.WriteBool(false);
            return;
        }

        writer.WriteBool(true);
        WriteTemplateStack(ref writer, stack, context, table);
    }

    /// <summary>Hashes a nested item-stack template in its data form: <c>id</c> is always present, <c>count</c> is omitted when it is 1, and <c>components</c> is omitted when empty. The optional count is the one structural difference from <see cref="HashStack"/>, which always writes it.</summary>
    /// <remarks>Same residual as <see cref="HashStack"/>: a hash disagreement costs a server-driven slot resync, never the session, so this follows the structural data map.</remarks>
    /// <param name="ops">The structural hash ops.</param>
    /// <param name="stack">The nested stack.</param>
    /// <param name="table">The component era table.</param>
    /// <param name="context">The codec context.</param>
    /// <returns>The structural hash.</returns>
    public static int HashTemplateStack(in HashOps ops, ItemStack stack, ItemComponentTable table, PacketCodecContext context)
    {
        if (stack.IsEmpty)
            return ops.Empty;

        var map = new List<(int Key, int Value)>(3)
        {
            (ops.String("id"), ops.String(stack.Item.Id.ToString())),
        };

        if (stack.Count != 1)
            map.Add((ops.String("count"), ops.Int(stack.Count)));

        int patchHash = HashComponentPatch(ops, stack, table, context, out bool anyPatch);
        if (anyPatch)
            map.Add((ops.String("components"), patchHash));

        return ops.Map(map);
    }

    // Hashed stack for 1.21.5+ container clicks

    /// <summary>Reads a hashed stack (VarInt count-or-0-empty via optional bool? No: optional wrapper).</summary>
    public static HashedStackValue ReadHashedStack(ref PacketReader reader, PacketCodecContext context)
    {
        // HashedStack = optional(ActualItem). The optional is a leading bool.
        if (!reader.ReadBool())
            return HashedStackValue.Empty;

        int itemId = ItemCodecPrimitives.ReadHolderId(ref reader);
        int count = reader.ReadVarInt();
        var added = new List<(int ComponentId, int Hash)>();
        int addedCount = reader.ReadVarInt();
        for (int i = 0; i < addedCount; i++)
        {
            int componentId = reader.ReadVarInt();
            int hash = reader.ReadInt();
            added.Add((componentId, hash));
        }

        var removed = new List<int>();
        int removedCount = reader.ReadVarInt();
        for (int i = 0; i < removedCount; i++)
            removed.Add(reader.ReadVarInt());

        return new HashedStackValue(itemId, count, added, removed);
    }

    /// <summary>Writes a hashed stack.</summary>
    public static void WriteHashedStack(ref PacketWriter writer, HashedStackValue stack)
    {
        if (stack.IsEmpty)
        {
            writer.WriteBool(false);
            return;
        }

        writer.WriteBool(true);
        ItemCodecPrimitives.WriteHolderId(ref writer, stack.ItemId);
        writer.WriteVarInt(stack.Count);
        writer.WriteVarInt(stack.AddedComponentHashes.Count);
        foreach ((int componentId, int hash) in stack.AddedComponentHashes)
        {
            writer.WriteVarInt(componentId);
            writer.WriteInt(hash);
        }

        writer.WriteVarInt(stack.RemovedComponentIds.Count);
        foreach (int componentId in stack.RemovedComponentIds)
            writer.WriteVarInt(componentId);

    }

    /// <summary>Converts an item stack to its hashed form under a component era table (for send).</summary>
    public static HashedStackValue ToHashedStack(ItemStack stack, ItemComponentTable table, PacketCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (stack.IsEmpty)
            return HashedStackValue.Empty;

        var ops = new HashOps();
        var added = new List<(int, int)>();
        var removed = new List<int>();
        foreach (DataComponentEntry entry in stack.Components.Patch)
        {
            if (!table.Contains(entry.Type))
                continue;

            if (entry.IsRemoval)
                removed.Add(table.WireId(entry.Type));

            else if (entry.Value is UnmodeledComponent)
            {
                // Vanilla hashes the DATA form, and an unmodeled component was only ever captured as wire bytes, so its structural hash is not derivable. Omitting it makes the server see a hash mismatch and push a full slot update, which is a self-correcting resync. Faulting here would cost the session over a container click.
                continue;
            }
            else
            {
                int hash = table.ByKey(entry.Type).Hash(ops, entry.Value!, context);
                added.Add((table.WireId(entry.Type), hash));
            }
        }

        return new HashedStackValue(stack.Item.NetworkId, stack.Count, added, removed);
    }

    /// <summary>Hashes a nested item stack inside a bundle or container in its full data form: a map <c>{id: &lt;resource string&gt;, count: &lt;int&gt;}</c> plus a <c>components</c> entry present only when the nested stack carries a non-empty component patch because an empty patch is omitted. The nested patch itself is a map keyed by the component type id, using a "!"-prefixed key for a removal, whose values are each component hashed through its own bound codec.</summary>
    /// <remarks>Nested removed components are modeled for completeness even though they effectively never occur in click echoes.</remarks>
    public static int HashStack(in HashOps ops, ItemStack stack, ItemComponentTable table, PacketCodecContext context)
    {
        if (stack.IsEmpty)
            return ops.Empty;

        var map = new List<(int Key, int Value)>(3)
        {
            (ops.String("id"), ops.String(stack.Item.Id.ToString())),
            (ops.String("count"), ops.Int(stack.Count)),
        };

        int patchHash = HashComponentPatch(ops, stack, table, context, out bool anyPatch);
        if (anyPatch)
            map.Add((ops.String("components"), patchHash));

        return ops.Map(map);
    }

    /// <summary>Hashes a nested stack's component patch as a map keyed by the component type id, with a "!"-prefixed key for a removal. Shared by the count-first and template DATA forms, which differ only in the fields AROUND the patch.</summary>
    private static int HashComponentPatch(
        in HashOps ops, ItemStack stack, ItemComponentTable table, PacketCodecContext context, out bool hasEntries)
    {
        var patch = new List<(int Key, int Value)>();
        foreach (DataComponentEntry entry in stack.Components.Patch)
        {
            if (!table.Contains(entry.Type))
                continue;

            string keyId = entry.Type.Id.ToString();
            if (entry.IsRemoval)
                patch.Add((ops.String("!" + keyId), ops.Empty));

            else if (entry.Value is UnmodeledComponent)
            {
                // Same reason as ToHashedStack: no DATA form to hash, so omit and let the server resync.
                continue;
            }
            else
            {
                int valueHash = table.ByKey(entry.Type).Hash(ops, entry.Value!, context);
                patch.Add((ops.String(keyId), valueHash));
            }
        }

        hasEntries = patch.Count > 0;
        return hasEntries ? ops.Map(patch) : ops.Empty;
    }

    /// <summary>Reads a varintId-era item stack.</summary>
    public static ItemStack ReadVarIntIdStack(ref PacketReader reader, PacketCodecContext context)
    {
        if (!reader.ReadBool())
            return ItemStack.Empty;

        int itemId = ItemCodecPrimitives.ReadHolderId(ref reader);
        byte count = reader.ReadByte();
        NbtTag nbt = reader.ReadNbt(ItemCodecPrimitives.NetworkNbt);

        if (!context.Registries.Items.TryGet(itemId, out RegistryEntry<ItemDefinition> item))
            throw new ProtocolViolationException($"Item id {itemId} is not in the item registry.");

        DataComponentMap components = DataComponentMap.Empty;
        if (nbt is NbtCompound compound && compound.Count > 0)
            components = components.With(DataComponents.LegacyNbt, new LegacyNbtComponent(compound));

        return new ItemStack(item, count, components);
    }

    /// <summary>Writes a varintId-era item stack.</summary>
    public static void WriteVarIntIdStack(ref PacketWriter writer, ItemStack stack, PacketCodecContext context)
    {
        if (stack.IsEmpty)
        {
            writer.WriteBool(false);
            return;
        }

        writer.WriteBool(true);
        ItemCodecPrimitives.WriteHolderId(ref writer, stack.Item.NetworkId);
        writer.WriteByte((byte)stack.Count);
        if (stack.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy))
            writer.WriteNbt(legacy.Nbt, ItemCodecPrimitives.NetworkNbt);

        else
            writer.WriteNbt(NbtEnd.Instance, ItemCodecPrimitives.NetworkNbt);

    }
}

/// <summary>The wire form of a 1.21.5+ hashed stack (container clicks): item holder id, count, the per-component int hashes for the added patch, and the removed component ids. The empty stack is a single false bool.</summary>
/// <param name="ItemId">The item holder network id.</param>
/// <param name="Count">The stack count.</param>
/// <param name="AddedComponentHashes">The (component wire id, structural hash) pairs of the patch.</param>
/// <param name="RemovedComponentIds">The removed component wire ids.</param>
public sealed record HashedStackValue(
    int ItemId,
    int Count,
    IReadOnlyList<(int ComponentId, int Hash)> AddedComponentHashes,
    IReadOnlyList<int> RemovedComponentIds)
{
    /// <summary>The empty hashed stack.</summary>
    public static HashedStackValue Empty { get; } = new(-1, 0, [], []) { IsEmpty = true };

    /// <summary>True when this represents the absent (empty) stack.</summary>
    public bool IsEmpty { get; init; }
}
