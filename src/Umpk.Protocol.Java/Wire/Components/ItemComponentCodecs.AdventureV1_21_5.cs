using System.Globalization;
using System.Text;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class ItemComponentCodecs
{
    /// <summary>Creates one 1.21.5 Adventure codec. Its exact matcher values recurse through the same table, bound once after construction.</summary>
    public static ModernAdventurePredicateCodec MakeAdventureV1_21_5(DataComponentType type) => new(type);

    internal sealed class ModernAdventurePredicateCodec(DataComponentType type) : ItemComponentCodec(type)
    {
        // Original 1.21.5 DataComponentPredicates static registration order. Only Damage's measured integer/default DATA form is owned below.
        private static readonly Identifier[] PartialTypes =
        [
            Identifier.Minecraft("damage"),
            Identifier.Minecraft("enchantments"),
            Identifier.Minecraft("stored_enchantments"),
            Identifier.Minecraft("potion_contents"),
            Identifier.Minecraft("custom_data"),
            Identifier.Minecraft("container"),
            Identifier.Minecraft("bundle_contents"),
            Identifier.Minecraft("firework_explosion"),
            Identifier.Minecraft("fireworks"),
            Identifier.Minecraft("writable_book_content"),
            Identifier.Minecraft("written_book_content"),
            Identifier.Minecraft("attribute_modifiers"),
            Identifier.Minecraft("trim"),
            Identifier.Minecraft("jukebox_playable"),
        ];

        private ItemComponentTable? _table;
        private ItemComponentTable Table => _table ?? throw new InvalidOperationException("Adventure codec has no owning item table.");

        public void Bind(ItemComponentTable table)
        {
            ArgumentNullException.ThrowIfNull(table);
            if (_table is not null)
                throw new InvalidOperationException("Adventure codec already has an owning item table.");
            _table = table;
        }

        public override object Decode(ref PacketReader reader, PacketCodecContext context)
            => new AdventureModePredicateComponent(reader.ReadList((ref PacketReader input) => ReadPredicate(ref input, context)));

        private BlockPredicateEntry ReadPredicate(ref PacketReader reader, PacketCodecContext context)
        {
            HolderSetRef? blocks = reader.ReadBool() ? ReadHolderSetRef(ref reader) : null;
            StatePropertyMatcher[]? properties = reader.ReadBool() ? reader.ReadList(ReadStateMatcher) : null;
            NbtCompound? nbt = reader.ReadBool() ? reader.ReadNbt(ItemCodecPrimitives.NetworkNbt) as NbtCompound
                ?? throw new ProtocolViolationException("A block-entity predicate requires a compound NBT tag.") : null;
            DataComponentEntry[] exact = reader.ReadList((ref PacketReader input) =>
            {
                ItemComponentCodec codec = Table.CompactCodec(input.ReadVarInt());
                return DataComponentEntry.Set(codec.Type, codec.Decode(ref input, context));
            });
            int count = reader.ReadVarInt();
            if (count < 0 || count > 64 || count > reader.Remaining)
                throw new ProtocolViolationException($"Invalid partial component predicate count {count}.");
            var partial = new BlockPredicatePartialMatcher[count];
            var seen = new HashSet<int>();
            for (int index = 0; index < count; index++)
            {
                int id = reader.ReadVarInt();
                if (id < 0 || id >= PartialTypes.Length)
                    throw new ProtocolViolationException($"Partial component predicate type {id} is out of range.");
                if (!seen.Add(id))
                    throw new ProtocolViolationException("A partial component predicate type occurs more than once.");
                NbtTag value = reader.ReadNbt(ItemCodecPrimitives.NetworkNbt);
                if (value is NbtEnd)
                    throw new ProtocolViolationException("A partial predicate requires a non-null NBT value.");
                if (id == 0 && value is not NbtCompound)
                    throw new ProtocolViolationException("The Damage predicate CODEC requires an NBT map.");
                if (id == 0 && value is NbtCompound damage)
                {
                    RequireDamageBoundsShape(damage, "durability");
                    RequireDamageBoundsShape(damage, "damage");
                }
                if (id != 0 || !IsOwnedDamage(value))
                    // Native-valid coercions and unowned selected-CODEC semantics are modeling gaps, not invented framing faults.
                    throw Table.UnmodeledValue(Type, "The selected partial predicate CODEC or numeric canonicalization is not owned.");
                partial[index] = new BlockPredicatePartialMatcher(PartialTypes[id], false, value);
            }
            return new BlockPredicateEntry(blocks, properties, nbt) { ComponentMatchers = new(exact, partial) };
        }

        public override void Encode(ref PacketWriter writer, object value, PacketCodecContext context)
        {
            var component = (AdventureModePredicateComponent)value;
            writer.WriteVarInt(component.Predicates.Count);
            foreach (BlockPredicateEntry predicate in component.Predicates)
            {
                writer.WriteBool(predicate.Blocks is not null);
                if (predicate.Blocks is { } blocks)
                    WriteHolderSetRef(ref writer, blocks);
                writer.WriteBool(predicate.Properties is not null);
                if (predicate.Properties is { } properties)
                    writer.WriteList(properties, WriteStateMatcher);
                writer.WriteBool(predicate.Nbt is not null);
                if (predicate.Nbt is { } nbt)
                    writer.WriteNbt(nbt, ItemCodecPrimitives.NetworkNbt);
                BlockPredicateComponentMatchers matchers = predicate.ComponentMatchers
                    ?? throw new InvalidOperationException("Modern block predicate matcher context is unavailable.");
                writer.WriteVarInt(matchers.Exact.Count);
                foreach (DataComponentEntry entry in matchers.Exact)
                {
                    if (entry.IsRemoval || entry.Value is null)
                        throw new ProtocolViolationException("An exact component predicate cannot remove a component.");
                    int id = Table.WireId(entry.Type);
                    ItemComponentCodec codec = Table.CompactCodec(id);
                    writer.WriteVarInt(id);
                    codec.Encode(ref writer, entry.Value, context);
                }
                if (matchers.Partial.Count > 64)
                    throw new ProtocolViolationException("A partial component predicate list exceeds 64 entries.");
                writer.WriteVarInt(matchers.Partial.Count);
                var seen = new HashSet<Identifier>();
                foreach (BlockPredicatePartialMatcher partial in matchers.Partial)
                {
                    if (!seen.Add(partial.Type))
                        throw new ProtocolViolationException("A partial component predicate type occurs more than once.");
                    if (partial.IsAnyValue || partial.Type != PartialTypes[0] || !IsOwnedDamage(partial.Value))
                        throw Table.UnmodeledValue(Type, "The selected partial predicate CODEC or numeric canonicalization is not owned.");
                    writer.WriteVarInt(0);
                    writer.WriteNbt(partial.Value, ItemCodecPrimitives.NetworkNbt);
                }
            }
        }

        public override int Hash(in HashOps ops, object value, PacketCodecContext context)
        {
            var component = (AdventureModePredicateComponent)value;
            if (component.Predicates.Count == 0)
                throw new ComponentHashUnavailableException("Native Adventure DATA encoding rejects an empty predicate list.");
            var hashes = new int[component.Predicates.Count];
            for (int index = 0; index < hashes.Length; index++)
                hashes[index] = HashPredicate(ops, component.Predicates[index], context);
            return hashes.Length == 1 ? hashes[0] : ops.List(hashes);
        }

        private int HashPredicate(in HashOps ops, BlockPredicateEntry predicate, PacketCodecContext context)
        {
            var fields = new List<(int Key, int Value)>();
            if (predicate.Blocks is { } blocks)
            {
                int hash;
                if (blocks.Tag is { } tag)
                    hash = ops.String("#" + tag);
                else
                {
                    var holders = new int[blocks.Ids.Count];
                    for (int index = 0; index < holders.Length; index++)
                    {
                        if (!context.Registries.Blocks.TryGet(blocks.Ids[index], out var block))
                            throw new ComponentHashUnavailableException("A predicate block holder is unavailable in the current registry.");
                        holders[index] = ops.String(block.Id.ToString());
                    }
                    hash = holders.Length == 1 ? holders[0] : ops.List(holders);
                }
                fields.Add((ops.String("blocks"), hash));
            }
            if (predicate.Properties is { } properties)
            {
                var values = new List<(int Key, int Value)>(properties.Count);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (StatePropertyMatcher matcher in properties)
                {
                    if (!names.Add(matcher.Name))
                        throw new ComponentHashUnavailableException("Duplicate state matcher names cannot form the native DATA map.");
                    int hash;
                    if (matcher.Exact)
                        hash = ops.String(matcher.Value ?? throw new ComponentHashUnavailableException("An exact state matcher has no value."));
                    else
                    {
                        var bounds = new List<(int Key, int Value)>(2);
                        if (matcher.MinValue is { } min)
                            bounds.Add((ops.String("min"), ops.String(min)));
                        if (matcher.MaxValue is { } max)
                            bounds.Add((ops.String("max"), ops.String(max)));
                        hash = ops.Map(bounds);
                    }
                    values.Add((ops.String(matcher.Name), hash));
                }
                fields.Add((ops.String("state"), ops.Map(values)));
            }
            if (predicate.Nbt is { } nbt)
                fields.Add((ops.String("nbt"), ops.String(AdventureSnbt(nbt))));
            BlockPredicateComponentMatchers matchers = predicate.ComponentMatchers
                ?? throw new ComponentHashUnavailableException("Modern block predicate matcher context is unavailable.");
            if (matchers.Exact.Count != 0)
            {
                var exact = new List<(int Key, int Value)>(matchers.Exact.Count);
                var types = new HashSet<Identifier>();
                foreach (DataComponentEntry entry in matchers.Exact)
                {
                    if (entry.IsRemoval || entry.Value is null || !types.Add(entry.Type.Id))
                        throw new ComponentHashUnavailableException("Exact matcher values cannot form the native DATA map.");
                    int hash = entry.Type.Equals(DataComponents.Damage) && entry.Value is DamageComponent { Value: >= 0 } damage
                        ? ops.Int(damage.Value)
                        : (entry.Type.Equals(DataComponents.CanBreak) || entry.Type.Equals(DataComponents.CanPlaceOn)) && entry.Value is AdventureModePredicateComponent
                            ? Table.ByKey(entry.Type).Hash(ops, entry.Value, context)
                            : throw new ComponentHashUnavailableException("The exact matcher's selected DATA hash is not owned.");
                    exact.Add((ops.String(entry.Type.Id.ToString()), hash));
                }
                fields.Add((ops.String("components"), ops.Map(exact)));
            }
            if (matchers.Partial.Count != 0)
            {
                var partials = new List<(int Key, int Value)>(matchers.Partial.Count);
                var types = new HashSet<Identifier>();
                foreach (BlockPredicatePartialMatcher partial in matchers.Partial)
                {
                    if (!types.Add(partial.Type) || partial.IsAnyValue || partial.Type != PartialTypes[0] || !IsOwnedDamage(partial.Value))
                        throw new ComponentHashUnavailableException("The partial matcher's selected DATA hash is not owned.");
                    partials.Add((ops.String(partial.Type.ToString()), HashDamage(ops, (NbtCompound)partial.Value)));
                }
                fields.Add((ops.String("predicates"), ops.Map(partials)));
            }
            return ops.Map(fields);
        }

        private static void RequireDamageBoundsShape(NbtCompound compound, string name)
        {
            if (!compound.TryGet(name, out NbtTag? value) || value is NbtNumeric)
                return;
            if (value is not NbtCompound bounds)
                throw new ProtocolViolationException($"Damage predicate field '{name}' requires a number or bounds map.");
            if ((bounds.TryGet("min", out NbtTag? min) && min is not NbtNumeric)
                || (bounds.TryGet("max", out NbtTag? max) && max is not NbtNumeric))
                throw new ProtocolViolationException($"Damage predicate field '{name}' requires numeric min/max bounds.");
        }

        private static bool IsOwnedDamage(NbtTag value)
            => value is NbtCompound compound && OwnedBounds(compound, "durability") && OwnedBounds(compound, "damage");

        private static bool OwnedBounds(NbtCompound compound, string name)
        {
            if (!compound.TryGet(name, out NbtTag? value))
                return true;
            if (value is NbtInt)
                return true;
            return value is NbtCompound bounds
                && (!bounds.TryGet("min", out NbtTag? min) || min is NbtInt)
                && (!bounds.TryGet("max", out NbtTag? max) || max is NbtInt);
        }

        private static int HashDamage(in HashOps ops, NbtCompound compound)
        {
            var fields = new List<(int Key, int Value)>(2);
            foreach (string name in new[] { "durability", "damage" })
            {
                if (!compound.TryGet(name, out NbtTag? value))
                    continue;
                if (value is NbtInt point)
                {
                    fields.Add((ops.String(name), ops.Int(point.Value)));
                    continue;
                }
                var bounds = (NbtCompound)value;
                int? min = bounds.TryGet("min", out NbtInt? lower) ? lower.Value : null;
                int? max = bounds.TryGet("max", out NbtInt? upper) ? upper.Value : null;
                if (min is null && max is null)
                    continue;
                int hash;
                if (min is { } exact && min == max)
                    hash = ops.Int(exact);
                else
                {
                    var range = new List<(int Key, int Value)>(2);
                    if (min is { } minimum)
                        range.Add((ops.String("min"), ops.Int(minimum)));
                    if (max is { } maximum)
                        range.Add((ops.String("max"), ops.Int(maximum)));
                    hash = ops.Map(range);
                }
                fields.Add((ops.String(name), hash));
            }
            return ops.Map(fields);
        }

        private static StatePropertyMatcher ReadStateMatcher(ref PacketReader reader)
        {
            string name = reader.ReadString();
            if (reader.ReadBool())
                return new StatePropertyMatcher(name, true, reader.ReadString());
            string? min = reader.ReadOptional(static (ref PacketReader input) => input.ReadString());
            string? max = reader.ReadOptional(static (ref PacketReader input) => input.ReadString());
            return new StatePropertyMatcher(name, false, null, min, max);
        }

        private static void WriteStateMatcher(ref PacketWriter writer, StatePropertyMatcher value)
        {
            writer.WriteString(value.Name);
            writer.WriteBool(value.Exact);
            if (value.Exact)
                writer.WriteString(value.Value ?? throw new ProtocolViolationException("An exact state matcher requires a value."));
            else
            {
                writer.WriteOptional(value.MinValue, static (ref PacketWriter output, string bound) => output.WriteString(bound));
                writer.WriteOptional(value.MaxValue, static (ref PacketWriter output, string bound) => output.WriteString(bound));
            }
        }

        private static string AdventureSnbt(NbtCompound tag)
        {
            var output = new StringBuilder();
            AppendSnbt(output, tag, 0);
            return output.ToString();
        }

        private static void AppendSnbt(StringBuilder output, NbtTag tag, int depth)
        {
            if (depth > 512)
                throw new ComponentHashUnavailableException("The predicate SNBT nesting exceeds the owned rendering depth.");
            switch (tag)
            {
                case NbtCompound compound:
                    output.Append('{');
                    bool first = true;
                    foreach (string key in compound.Keys.Order(StringComparer.Ordinal))
                    {
                        if (!first)
                            output.Append(',');
                        first = false;
                        if (SimpleSnbtKey(key))
                            output.Append(key);
                        else
                            AppendQuoted(output, key);
                        output.Append(':');
                        AppendSnbt(output, compound[key], depth + 1);
                    }
                    output.Append('}');
                    return;
                case NbtList list:
                    output.Append('[');
                    for (int index = 0; index < list.Count; index++)
                    {
                        if (index != 0)
                            output.Append(',');
                        AppendSnbt(output, list[index], depth + 1);
                    }
                    output.Append(']');
                    return;
                case NbtString value:
                    AppendQuoted(output, value.Value);
                    return;
                case NbtByte value:
                    output.Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append('b');
                    return;
                case NbtShort value:
                    output.Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append('s');
                    return;
                case NbtInt value:
                    output.Append(value.Value.ToString(CultureInfo.InvariantCulture));
                    return;
                case NbtLong value:
                    output.Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append('L');
                    return;
                default:
                    // Array and Java floating-point spelling still need their own native string/hash witnesses; do not use the generic NBT hash or insertion-order printer.
                    throw new ComponentHashUnavailableException("The predicate tag's native SNBT spelling is not owned.");
            }
        }

        private static bool SimpleSnbtKey(string key)
        {
            if (key.Length == 0 || key.Equals("true", StringComparison.OrdinalIgnoreCase) || key.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            if (key[0] is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or '.' or '_'))
                return false;
            return key.All(static character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '+' or '-');
        }

        private static void AppendQuoted(StringBuilder output, string value)
        {
            int start = output.Length;
            output.Append(' ');
            char quote = '\0';
            foreach (char character in value)
            {
                if (character == '\\')
                    output.Append("\\\\");
                else if (character is '\'' or '"')
                {
                    if (quote == '\0')
                        quote = character == '"' ? '\'' : '"';
                    if (quote == character)
                        output.Append('\\');
                    output.Append(character);
                }
                else if (character < ' ')
                    output.Append('\\').Append(character switch
                    {
                        '\b' => "b",
                        '\t' => "t",
                        '\n' => "n",
                        '\f' => "f",
                        '\r' => "r",
                        _ => "x" + ((byte)character).ToString("X2", CultureInfo.InvariantCulture),
                    });
                else
                    output.Append(character);
            }
            if (quote == '\0')
                quote = '"';
            output[start] = quote;
            output.Append(quote);
        }
    }
}
