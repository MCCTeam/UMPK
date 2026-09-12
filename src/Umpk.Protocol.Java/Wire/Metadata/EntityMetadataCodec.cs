using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>One decoded entity-metadata entry: the field index and its typed value.</summary>
/// <param name="Index">The field index the value is stored at.</param>
/// <param name="Value">The typed metadata value (a <see cref="Umpk.Game.Entities.MetadataValue"/>).</param>
/// <param name="WireSerializerId">The exact modern-format serializer id read off the wire, preserved so re-encode emits the same id. Several distinct serializers decode to the same <see cref="Umpk.Game.Entities.MetadataValue"/> kind (for example every Holder-based variant serializer decodes as a plain VarInt), so deriving the id back from the value kind alone would pick the first serializer of that kind and change the bytes. Negative means "derive from the value kind" (used for the 1.8 packed format and for entries built in code rather than decoded).</param>
public readonly record struct EntityDataEntry(int Index, MetadataValue Value, int WireSerializerId = -1);

/// <summary>The wire shape of a whole entity-metadata list: the structured entries, plus an optional raw tail captured verbatim when a modern item-slot value is reached (the inventory item-codec seam is not yet available, so decode stops there and preserves the remainder byte-exactly). Legacy (1.8) lists never set <see cref="RawTail"/> because the legacy item format is self-delimiting.</summary>
/// <param name="Entries">The structured metadata entries decoded so far.</param>
/// <param name="RawTail">The un-decoded remainder (from a modern item-slot value onward, including its index/serializer header), or empty when the whole list decoded structurally. Re-emitted verbatim so round-trip is byte-exact. TODO(inventory-item-codec): remove once the modern item codec lands.</param>
public sealed record EntityMetadataList(IReadOnlyList<EntityDataEntry> Entries, byte[] RawTail);

/// <summary>The per-era inputs a STRUCTURAL particle decode needs: the era's particle option-shape table and the era's item-component table (the <c>item</c> particle option carries a full item stack).</summary>
/// <remarks>Passed in rather than looked up because <see cref="PacketCodecContext"/> deliberately exposes no protocol version: version-dependent behaviour is a different codec INSTANCE for that version range, which is the rule that keeps inline version branching out of the codecs. Null means "this era has no particle shape table", and particle values are then captured verbatim instead.</remarks>
/// <param name="Shapes">The era's particle option-shape table.</param>
/// <param name="Icons">The era's item-component table, for the <c>item</c> particle option.</param>
internal sealed record MetadataParticleEra(
    IReadOnlyDictionary<int, ParticleOptionShape> Shapes,
    ItemComponentTable Icons);

/// <summary>The item-stack reader/writer selected for one entity-metadata protocol era.</summary>
/// <param name="Read">The era's stack reader.</param>
/// <param name="Write">The era's stack writer.</param>
/// <param name="Form">What the pair reads, named here rather than derived from two delegate names.</param>
internal sealed record MetadataItemEra(
    ItemPacketCodecShared.StackReader Read,
    ItemPacketCodecShared.StackWriter Write,
    string Form);

/// <summary>The entity-metadata serializer table and value codecs. Two eras: the 1.8 packed <c>type&lt;&lt;5 | index</c> format terminated by <c>0x7f</c>, and the modern <c>index byte, VarInt serializer id, value</c> format terminated by <c>0xff</c>.</summary>
/// <remarks>Wire anchors: the 1.8.9 packed key and type range 0-7, and the 1.21.5 serializer registration order (indices 0-34). Version 26.1 adds sound-variant and profile serializers, shifting later ids. Holder-based variant serializers (cat/cow/painting/...) are all VarInt-shaped and decode as <see cref="MetadataValue.VarInt"/>.</remarks>
internal static class EntityMetadataCodec
{
    /// <summary>The modern metadata list terminator.</summary>
    private const int ModernTerminator = 0xFF;

    /// <summary>The 1.8 metadata list terminator.</summary>
    private const int LegacyTerminator = 0x7F;

    // 1.8 packed format (DataWatcher)

    /// <summary>Reads a 1.8 metadata list: repeated header byte (<c>type&lt;&lt;5 | index</c>) plus value, terminated by 0x7f. Types: 0 byte, 1 short, 2 int, 3 float, 4 string, 5 item, 6 blockpos (3 ints), 7 rotations.</summary>
    public static EntityMetadataList ReadLegacy(ref PacketReader r, PacketCodecContext? context = null)
    {
        var entries = new List<EntityDataEntry>();
        while (true)
        {
            int header = r.ReadByte();
            if (header == LegacyTerminator)
                return new EntityMetadataList(entries, []);

            int type = (header & 0xE0) >> 5;
            int index = header & 0x1F;
            MetadataValue value = type switch
            {
                0 => MetadataValue.Byte(r.ReadSByte()),
                1 => MetadataValue.VarInt(r.ReadShort()),
                2 => MetadataValue.VarInt(r.ReadInt()),
                3 => MetadataValue.Float(r.ReadFloat()),
                4 => MetadataValue.String(r.ReadString()),
                5 => MetadataValue.Slot(context is null
                    ? EntityItemSlotCodec.ReadLegacy(ref r)
                    : ItemStackCodecs.ReadLegacyStack(ref r, context)),
                6 => MetadataValue.Position(new BlockPos(r.ReadInt(), r.ReadInt(), r.ReadInt())),
                7 => MetadataValue.Rotations(new Rotations(r.ReadFloat(), r.ReadFloat(), r.ReadFloat())),
                _ => throw new ProtocolViolationException($"Unknown 1.8 metadata type {type} at index {index}."),
            };

            // The 1.8 DataWatcher has distinct type 1 (short) and type 2 (int); both project onto the VarInt value kind, so the wire type is retained on the entry (reusing the WireSerializerId slot) to re-encode short versus int byte-exactly.
            entries.Add(new EntityDataEntry(index, value, type));
        }
    }

    /// <summary>Writes a 1.8 metadata list mirroring <see cref="ReadLegacy"/>.</summary>
    public static void WriteLegacy(ref PacketWriter w, EntityMetadataList list, PacketCodecContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        foreach (EntityDataEntry entry in list.Entries)
        {
            // Prefer the exact wire type read off the 1.8 stream (retained in WireSerializerId) so short vs int round-trips byte-exactly; fall back to deriving it from the value kind for entries built in code (WireSerializerId < 0).
            int type = entry.WireSerializerId >= 0 ? entry.WireSerializerId : LegacyType(entry.Value.Kind);
            w.WriteByte((byte)((type << 5) | (entry.Index & 0x1F)));
            WriteLegacyValue(ref w, type, entry.Value, context);
        }

        w.WriteByte(LegacyTerminator);
    }

    private static int LegacyType(MetadataValueKind kind) => kind switch
    {
        MetadataValueKind.Byte => 0,
        MetadataValueKind.VarInt => 2,
        MetadataValueKind.Float => 3,
        MetadataValueKind.String => 4,
        MetadataValueKind.Slot => 5,
        MetadataValueKind.Position => 6,
        MetadataValueKind.Rotations => 7,
        _ => throw new ProtocolViolationException($"Metadata kind {kind} is not representable in the 1.8 format."),
    };

    private static void WriteLegacyValue(
        ref PacketWriter w,
        int type,
        MetadataValue value,
        PacketCodecContext? context)
    {
        switch (value.Kind)
        {
            case MetadataValueKind.Byte: w.WriteSByte(value.AsByte()); break;

            // 1.8 type 1 is a 16-bit short; type 2 is a 32-bit int. Both surface as the VarInt kind.
            case MetadataValueKind.VarInt when type == 1: w.WriteShort((short)value.AsVarInt()); break;
            case MetadataValueKind.VarInt: w.WriteInt(value.AsVarInt()); break;
            case MetadataValueKind.Float: w.WriteFloat(value.AsFloat()); break;
            case MetadataValueKind.String: w.WriteString(value.AsString()); break;
            case MetadataValueKind.Slot when context is not null && value.AsSlot() is Umpk.Game.Items.ItemStack stack:
                ItemStackCodecs.WriteLegacyStack(ref w, stack, context);
                break;
            case MetadataValueKind.Slot:
                EntityItemSlotCodec.WriteLegacy(ref w, (EntityItemSlot)value.AsSlot()!);
                break;
            case MetadataValueKind.Position:
                BlockPos p = value.AsPosition();
                w.WriteInt(p.X);
                w.WriteInt(p.Y);
                w.WriteInt(p.Z);
                break;
            case MetadataValueKind.Rotations:
                Rotations rot = value.AsRotations();
                w.WriteFloat(rot.X);
                w.WriteFloat(rot.Y);
                w.WriteFloat(rot.Z);
                break;
            default:
                throw new ProtocolViolationException($"Metadata kind {value.Kind} is not representable in the 1.8 format.");
        }
    }

    // modern format (1.9+; index byte, VarInt serializer id, value)

    /// <summary>Reads a modern metadata list under the given serializer table. Each entry is an index byte (0xff terminates), a VarInt serializer id, then the serializer's value. When a serializer maps to a modern item slot (no length-derivable codec yet), decode stops and the remainder is captured raw (including this entry's header). See <see cref="EntityMetadataList.RawTail"/>.</summary>
    public static EntityMetadataList ReadModern(ref PacketReader r, ModernMetadataTable table, NbtWireFormat nbt, ComponentWireEra component, bool componentJson = false, BlockPosLayout blockPos = BlockPosLayout.Packed114, MetadataParticleEra? particles = null, MetadataItemEra? items = null, PacketCodecContext? context = null)
    {
        var entries = new List<EntityDataEntry>();
        while (true)
        {
            int index = r.ReadByte();
            if (index == ModernTerminator)
                return new EntityMetadataList(entries, []);

            int serializerId = r.ReadVarInt();
            ModernMetadataSerializer serializer = table.Resolve(serializerId);
            if (serializer == ModernMetadataSerializer.ItemStack && items is not null && context is not null)
            {
                PacketReader probe = r;
                try
                {
                    MetadataValue slot = MetadataValue.Slot(items.Read(ref probe, context));
                    r = probe;
                    entries.Add(new EntityDataEntry(index, slot, serializerId));
                    continue;
                }
                catch (ProtocolViolationException)
                {
                    // A malformed or registry-unknown slot must not desynchronise every metadata field after it. Preserve the undecodable entry and tail byte-exactly, matching the unknown-serializer policy, while valid item entities still expose a real stack.
                    return new EntityMetadataList(entries, CaptureItemTail(ref r, index, serializerId));
                }
            }

            if ((serializer == ModernMetadataSerializer.ItemStack && (items is null || context is null))
                || ModernMetadataTable.NeedsRawTail(serializer, particles is not null && context is not null))
            {
                // No usable protocol-side codec for this binding (item/particle/profile): capture its header plus the rest verbatim so the frame round-trips byte-exactly. The header (index byte + serializer VarInt) re-encodes deterministically, so it is reconstructed rather than un-read. TODO(inventory-item-codec / particle-codec): decode structurally.
                byte[] tail = CaptureItemTail(ref r, index, serializerId);
                return new EntityMetadataList(entries, tail);
            }

            MetadataValue value = serializer switch
            {
                ModernMetadataSerializer.Particle =>
                    MetadataValue.Particle(ParticleCodec.ReadModern(ref r, particles!.Shapes, particles.Icons, context!)),
                ModernMetadataSerializer.Particles =>
                    MetadataExtraCodecs.ReadParticles(ref r, particles!.Shapes, particles.Icons, context!),
                _ => ReadModernValue(ref r, serializer, nbt, component, componentJson, blockPos),
            };

            entries.Add(new EntityDataEntry(index, value, serializerId));
        }
    }

    /// <summary>Writes a modern metadata list mirroring <see cref="ReadModern"/>, including any raw tail.</summary>
    public static void WriteModern(ref PacketWriter w, EntityMetadataList list, ModernMetadataTable table, NbtWireFormat nbt, ComponentWireEra component, bool componentJson = false, BlockPosLayout blockPos = BlockPosLayout.Packed114, MetadataParticleEra? particles = null, MetadataItemEra? items = null, PacketCodecContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        foreach (EntityDataEntry entry in list.Entries)
        {
            w.WriteByte((byte)entry.Index);

            // Prefer the exact serializer id read off the wire so re-encode is byte-identical; only fall back to deriving it from the value kind for entries built in code (WireSerializerId < 0).
            int serializerId = entry.WireSerializerId >= 0
                ? entry.WireSerializerId
                : table.SerializerId(SerializerFor(entry.Value.Kind));
            w.WriteVarInt(serializerId);
            switch (entry.Value.Kind)
            {
                case MetadataValueKind.Particle:
                    ParticleCodec.WriteModern(ref w, (ParticleData)entry.Value.AsParticle());
                    break;
                case MetadataValueKind.Particles:
                    MetadataExtraCodecs.WriteParticles(ref w, entry.Value);
                    break;
                case MetadataValueKind.Slot when items is not null && context is not null:
                    items.Write(ref w, (Umpk.Game.Items.ItemStack?)entry.Value.AsSlot() ?? Umpk.Game.Items.ItemStack.Empty, context);
                    break;
                default:
                    WriteModernValue(ref w, entry.Value, nbt, component, componentJson, blockPos);
                    break;
            }
        }

        if (list.RawTail.Length > 0)
        {
            // The raw tail carries its own index/serializer header and everything after it verbatim.
            w.WriteBytes(list.RawTail);
            return;
        }

        w.WriteByte(ModernTerminator);
    }

    private static byte[] CaptureItemTail(ref PacketReader r, int index, int serializerId)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var header = new PacketWriter(buffer);
        header.WriteByte((byte)index);
        header.WriteVarInt(serializerId);
        ReadOnlySpan<byte> rest = r.ReadRemaining();
        var full = new byte[buffer.WrittenCount + rest.Length];
        buffer.WrittenSpan.CopyTo(full);
        rest.CopyTo(full.AsSpan(buffer.WrittenCount));
        return full;
    }

    private const int MaxMetadataComponentBytes = 262144;

    private static MetadataValue ReadModernValue(ref PacketReader r, ModernMetadataSerializer serializer, NbtWireFormat nbt, ComponentWireEra component, bool componentJson = false, BlockPosLayout blockPos = BlockPosLayout.Packed114) => serializer switch
    {
        ModernMetadataSerializer.Byte => MetadataValue.Byte(r.ReadSByte()),
        ModernMetadataSerializer.Int => MetadataValue.VarInt(r.ReadVarInt()),
        ModernMetadataSerializer.Long => MetadataValue.VarLong(r.ReadVarLong()),
        ModernMetadataSerializer.Float => MetadataValue.Float(r.ReadFloat()),
        ModernMetadataSerializer.String => MetadataValue.String(r.ReadString()),
        ModernMetadataSerializer.Component => MetadataValue.Component(ReadMetaComponent(ref r, component, nbt, componentJson)),
        ModernMetadataSerializer.OptionalComponent => MetadataValue.OptionalComponent(r.ReadBool() ? ReadMetaComponent(ref r, component, nbt, componentJson) : null),
        ModernMetadataSerializer.Boolean => MetadataValue.Boolean(r.ReadBool()),
        ModernMetadataSerializer.Rotations => MetadataValue.Rotations(new Rotations(r.ReadFloat(), r.ReadFloat(), r.ReadFloat())),
        ModernMetadataSerializer.BlockPos => MetadataValue.Position(r.ReadBlockPos(blockPos)),
        ModernMetadataSerializer.OptionalBlockPos => MetadataValue.OptionalPosition(r.ReadBool() ? r.ReadBlockPos(blockPos) : null),
        ModernMetadataSerializer.Direction => MetadataValue.Direction((Direction)r.ReadVarInt()),
        ModernMetadataSerializer.OptionalUuid => MetadataValue.OptionalUuid(r.ReadBool() ? r.ReadUuid() : null),
        ModernMetadataSerializer.BlockState => MetadataValue.BlockState(r.ReadVarInt()),
        ModernMetadataSerializer.OptionalBlockState => MetadataValue.OptionalBlockState(ReadOptionalBlockState(ref r)),
        ModernMetadataSerializer.CompoundTag => MetadataValue.Nbt(r.ReadNbt(nbt)),
        ModernMetadataSerializer.VillagerData => MetadataValue.VillagerData(new VillagerData(r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt())),
        ModernMetadataSerializer.OptionalUnsignedInt => MetadataValue.OptionalVarInt(ReadOptionalUnsignedInt(ref r)),
        ModernMetadataSerializer.Pose => MetadataValue.Pose((EntityPose)r.ReadVarInt()),
        ModernMetadataSerializer.VarIntHolder => MetadataValue.VarInt(r.ReadVarInt()),
        ModernMetadataSerializer.OptionalGlobalPos => MetadataValue.OptionalGlobalPosition(ReadOptionalGlobalPos(ref r, blockPos)),
        ModernMetadataSerializer.Vector3 => MetadataValue.Vector3(new Vec3d(r.ReadFloat(), r.ReadFloat(), r.ReadFloat())),
        ModernMetadataSerializer.Quaternion => MetadataValue.Quaternion(new Quaternion(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat())),
        _ => throw new ProtocolViolationException($"Unsupported modern metadata serializer {serializer}."),
    };

    private static Component ReadMetaComponent(ref PacketReader r, ComponentWireEra component, NbtWireFormat nbt, bool componentJson) =>
        componentJson
            ? ComponentJson.Parse(r.ReadString(MaxMetadataComponentBytes), component)
            : r.ReadComponent(component, nbt);

    private static void WriteMetaComponent(ref PacketWriter w, Component value, ComponentWireEra component, NbtWireFormat nbt, bool componentJson)
    {
        if (componentJson)
        {
            // componentJson is the pre-1.20.3 path; the newest binding that sets it is protocol 764. That band requires the object form rather than a bare string, preserving metadata bytes.
            w.WriteString(
                ComponentJson.ToJsonString(value, component, ComponentJsonLiteralForm.Object), MaxMetadataComponentBytes);
        }
        else
            w.WriteComponent(value, component, nbt);

    }

    private static void WriteModernValue(ref PacketWriter w, MetadataValue value, NbtWireFormat nbt, ComponentWireEra component, bool componentJson = false, BlockPosLayout blockPos = BlockPosLayout.Packed114)
    {
        switch (value.Kind)
        {
            case MetadataValueKind.Byte: w.WriteSByte(value.AsByte()); break;
            case MetadataValueKind.VarInt: w.WriteVarInt(value.AsVarInt()); break;
            case MetadataValueKind.VarLong: w.WriteVarLong(value.AsVarLong()); break;
            case MetadataValueKind.Float: w.WriteFloat(value.AsFloat()); break;
            case MetadataValueKind.String: w.WriteString(value.AsString()); break;
            case MetadataValueKind.Component: WriteMetaComponent(ref w, value.AsComponent(), component, nbt, componentJson); break;
            case MetadataValueKind.OptionalComponent:
                Component? oc = value.AsOptionalComponent();
                w.WriteBool(oc is not null);
                if (oc is not null) WriteMetaComponent(ref w, oc, component, nbt, componentJson);
                break;
            case MetadataValueKind.Boolean: w.WriteBool(value.AsBoolean()); break;
            case MetadataValueKind.Rotations:
                Rotations rot = value.AsRotations();
                w.WriteFloat(rot.X); w.WriteFloat(rot.Y); w.WriteFloat(rot.Z);
                break;
            case MetadataValueKind.Position: w.WriteBlockPos(value.AsPosition(), blockPos); break;
            case MetadataValueKind.OptionalPosition:
                BlockPos? op = value.AsOptionalPosition();
                w.WriteBool(op.HasValue);
                if (op.HasValue) w.WriteBlockPos(op.Value, blockPos);
                break;
            case MetadataValueKind.Direction: w.WriteVarInt((int)value.AsDirection()); break;
            case MetadataValueKind.OptionalUuid:
                Guid? ou = value.AsOptionalUuid();
                w.WriteBool(ou.HasValue);
                if (ou.HasValue) w.WriteUuid(ou.Value);
                break;
            case MetadataValueKind.BlockState: w.WriteVarInt(value.AsBlockState()); break;
            case MetadataValueKind.OptionalBlockState: w.WriteVarInt((value.AsOptionalBlockState() ?? -1) + 1); break;
            case MetadataValueKind.Nbt: w.WriteNbt(value.AsNbt(), nbt); break;
            case MetadataValueKind.VillagerData:
                VillagerData vd = value.AsVillagerData();
                w.WriteVarInt(vd.Type); w.WriteVarInt(vd.Profession); w.WriteVarInt(vd.Level);
                break;
            case MetadataValueKind.OptionalVarInt: w.WriteVarInt((value.AsOptionalVarInt() ?? -1) + 1); break;
            case MetadataValueKind.Pose: w.WriteVarInt((int)value.AsPose()); break;
            case MetadataValueKind.OptionalGlobalPosition:
                GlobalPosition? gp = value.AsOptionalGlobalPosition();
                w.WriteBool(gp.HasValue);
                if (gp is { } present)
                {
                    w.WriteString(present.Dimension.ToString());
                    w.WriteBlockPos(present.Position, blockPos);
                }

                break;
            case MetadataValueKind.Vector3:
                Vec3d v3 = value.AsVector3();
                w.WriteFloat((float)v3.X); w.WriteFloat((float)v3.Y); w.WriteFloat((float)v3.Z);
                break;
            case MetadataValueKind.Quaternion:
                Quaternion q = value.AsQuaternion();
                w.WriteFloat(q.X); w.WriteFloat(q.Y); w.WriteFloat(q.Z); w.WriteFloat(q.W);
                break;
            default:
                throw new ProtocolViolationException($"Metadata kind {value.Kind} is not representable in the modern format.");
        }
    }

    private static int? ReadOptionalBlockState(ref PacketReader r)
    {
        int id = r.ReadVarInt();
        return id == 0 ? null : id;
    }

    private static int? ReadOptionalUnsignedInt(ref PacketReader r)
    {
        int raw = r.ReadVarInt();
        return raw == 0 ? null : raw - 1;
    }

    private static GlobalPosition? ReadOptionalGlobalPos(ref PacketReader r, BlockPosLayout blockPos)
    {
        if (!r.ReadBool())
            return null;

        Identifier dim = Identifier.Parse(r.ReadString());
        BlockPos pos = r.ReadBlockPos(blockPos);
        return new GlobalPosition(dim, pos);
    }

    private static ModernMetadataSerializer SerializerFor(MetadataValueKind kind) => kind switch
    {
        MetadataValueKind.Byte => ModernMetadataSerializer.Byte,
        MetadataValueKind.VarInt => ModernMetadataSerializer.Int,
        MetadataValueKind.VarLong => ModernMetadataSerializer.Long,
        MetadataValueKind.Float => ModernMetadataSerializer.Float,
        MetadataValueKind.String => ModernMetadataSerializer.String,
        MetadataValueKind.Component => ModernMetadataSerializer.Component,
        MetadataValueKind.OptionalComponent => ModernMetadataSerializer.OptionalComponent,
        MetadataValueKind.Slot => ModernMetadataSerializer.ItemStack,
        MetadataValueKind.Boolean => ModernMetadataSerializer.Boolean,
        MetadataValueKind.Rotations => ModernMetadataSerializer.Rotations,
        MetadataValueKind.Position => ModernMetadataSerializer.BlockPos,
        MetadataValueKind.OptionalPosition => ModernMetadataSerializer.OptionalBlockPos,
        MetadataValueKind.Direction => ModernMetadataSerializer.Direction,
        MetadataValueKind.OptionalUuid => ModernMetadataSerializer.OptionalUuid,
        MetadataValueKind.BlockState => ModernMetadataSerializer.BlockState,
        MetadataValueKind.OptionalBlockState => ModernMetadataSerializer.OptionalBlockState,
        MetadataValueKind.Nbt => ModernMetadataSerializer.CompoundTag,
        MetadataValueKind.VillagerData => ModernMetadataSerializer.VillagerData,
        MetadataValueKind.OptionalVarInt => ModernMetadataSerializer.OptionalUnsignedInt,
        MetadataValueKind.Pose => ModernMetadataSerializer.Pose,
        MetadataValueKind.OptionalGlobalPosition => ModernMetadataSerializer.OptionalGlobalPos,
        MetadataValueKind.Vector3 => ModernMetadataSerializer.Vector3,
        MetadataValueKind.Quaternion => ModernMetadataSerializer.Quaternion,
        MetadataValueKind.Particle => ModernMetadataSerializer.Particle,
        MetadataValueKind.Particles => ModernMetadataSerializer.Particles,
        _ => throw new ProtocolViolationException($"Metadata kind {kind} has no modern serializer mapping."),
    };
}
