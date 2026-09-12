using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The two era tables a 1.20.5+ level-particles codec reads a particle through, plus the one field the frame itself gained. The tables travel together because they must be the same era: the option-shape table says how to read a particle's payload and the component table reads the item particle's stack, and crossing them decodes an item under a neighbouring era's component ids.</summary>
/// <param name="Shapes">The era's particle option-shape table.</param>
/// <param name="Icons">The era's item component table, for the item particle.</param>
/// <param name="HasAlwaysShow">True from 1.21.4 (769), where the frame gained a second leading bool. 1.21.2 reads one bool before the coordinates; 1.21.4 reads <c>overrideLimiter</c> then <c>alwaysShow</c>. A frame written with the extra bool is one byte longer and shifts every field after it.</param>
internal readonly record struct ParticlesWire(
    IReadOnlyDictionary<int, ParticleOptionShape> Shapes,
    ItemComponentTable Icons,
    bool HasAlwaysShow = true)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"shapes={WireShapeDigest.Of(Shapes.OrderBy(static e => e.Key).Select(static e => $"{e.Key}:{e.Value}"))}," +
        $"icons={Icons.ShapeToken},alwaysshow={(HasAlwaysShow ? 1 : 0)}";
}

/// <summary>How one era frames a <c>block_entity_data</c> frame. The three facts move at three unrelated releases: the block-position packing at 1.14, the type field from a byte to a registry VarInt at 1.18, and the NBT root name at 1.20.2. Reading the wrong NBT flavour under-consumes silently (a named root's name length parses as an End entry), which the strict decode policy escalates to a session-fatal trailing-bytes violation.</summary>
/// <param name="Pos">The era's BLOCK_POS packing.</param>
/// <param name="ActionIsByte">Pre-1.18: the block-entity type is an unsigned byte, not a VarInt.</param>
/// <param name="Nbt">The era's NBT root framing.</param>
internal readonly record struct BlockEntityDataWire(BlockPosLayout Pos, bool ActionIsByte, NbtWireFormat Nbt)
{
    /// <summary>47-404: pre-1.14 position packing, byte type, named NBT root.</summary>
    internal static BlockEntityDataWire V1_8 { get; } =
        new(BlockPosLayout.PrePacked114, ActionIsByte: true, NbtWireFormat.JavaNamedRoot);

    /// <summary>477-756: the 1.14 position packing, still a byte type.</summary>
    internal static BlockEntityDataWire V1_14 { get; } = V1_8 with { Pos = BlockPosLayout.Packed114 };

    /// <summary>757-763: the type becomes a registry VarInt.</summary>
    internal static BlockEntityDataWire V1_18 { get; } = V1_14 with { ActionIsByte = false };

    /// <summary>764+: the network NBT root loses its name.</summary>
    internal static BlockEntityDataWire V1_20_2 { get; } = V1_18 with { Nbt = NbtWireFormat.JavaUnnamedRoot };

    /// <inheritdoc />
    public override string ToString() => $"{Pos},bytetype={(ActionIsByte ? 1 : 0)},{Nbt}";
}

/// <summary>Shared low-level wire helpers for the world-family codecs (vec3 / chunk-pos / light-data / long and nibble arrays / legacy particle args / map patches) and the per-packet era factory helpers.</summary>
internal static class WorldCodecShared
{
    // Nibble arrays in a light-update block are always this length (16*16*16 / 2).
    internal const int LightArrayLength = 2048;

    internal static PacketCodec<ClientboundSetDefaultSpawnPositionPacket> MakeSetDefaultSpawn(BlockPosLayout layout, bool hasAngle) =>
        PacketCodec<ClientboundSetDefaultSpawnPositionPacket>.Of(
            (ref PacketWriter w, ClientboundSetDefaultSpawnPositionPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Position, layout);
                if (hasAngle)
                    w.WriteFloat(p.Angle);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                BlockPos pos = r.ReadBlockPos(layout);
                float angle = hasAngle ? r.ReadFloat() : 0f;
                return new ClientboundSetDefaultSpawnPositionPacket(pos, angle, Dimension: null, Pitch: 0f);
            });

    internal static PacketCodec<ClientboundBlockUpdatePacket> MakeBlockUpdate(BlockPosLayout layout) =>
        PacketCodec<ClientboundBlockUpdatePacket>.Of(
            (ref PacketWriter w, ClientboundBlockUpdatePacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Position, layout);
                w.WriteVarInt(p.BlockStateId);
            },
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundBlockUpdatePacket(r.ReadBlockPos(layout), r.ReadVarInt()),
            WireShape.Of("block_pos,varint", layout.ToString()));

    // The NBT root framing follows the era: named root before 1.20.2, unnamed from 1.20.2 on. Reading the wrong flavor silently under-consumes (the named root's name length parses as an End entry), which the strict decode policy escalates to a session-fatal trailing-bytes violation.
    internal static PacketCodec<ClientboundBlockEntityDataPacket> MakeBlockEntityData(
        BlockEntityDataWire era) =>
        PacketCodec<ClientboundBlockEntityDataPacket>.Of(
            (ref PacketWriter w, ClientboundBlockEntityDataPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Position, era.Pos);
                if (era.ActionIsByte)
                    w.WriteByte((byte)p.BlockEntityType);

                else
                    w.WriteVarInt(p.BlockEntityType);

                w.WriteNbt(p.Nbt, era.Nbt);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                BlockPos pos = r.ReadBlockPos(era.Pos);
                int type = era.ActionIsByte ? r.ReadByte() : r.ReadVarInt();
                NbtTag nbt = r.ReadNbt(era.Nbt);
                return new ClientboundBlockEntityDataPacket(pos, type, nbt);
            },
            WireShape.OfEra("block_entity_data", era));

    internal static PacketCodec<ClientboundBlockEventPacket> MakeBlockEvent(BlockPosLayout layout) =>
        PacketCodec<ClientboundBlockEventPacket>.Of(
            (ref PacketWriter w, ClientboundBlockEventPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Position, layout);
                w.WriteByte(p.B0);
                w.WriteByte(p.B1);
                w.WriteVarInt(p.BlockId);
            },
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundBlockEventPacket(r.ReadBlockPos(layout), r.ReadByte(), r.ReadByte(), r.ReadVarInt()));

    internal static PacketCodec<ClientboundBlockDestructionPacket> MakeBlockDestruction(BlockPosLayout layout) =>
        PacketCodec<ClientboundBlockDestructionPacket>.Of(
            (ref PacketWriter w, ClientboundBlockDestructionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteBlockPos(p.Position, layout);
                w.WriteByte(p.Progress);
            },
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundBlockDestructionPacket(r.ReadVarInt(), r.ReadBlockPos(layout), r.ReadByte()));

    internal static PacketCodec<ClientboundGameEventPacket> MakeGameEvent() =>
        PacketCodec<ClientboundGameEventPacket>.Of(
            static (ref PacketWriter w, ClientboundGameEventPacket p, PacketCodecContext _) =>
            {
                w.WriteByte(p.Event);
                w.WriteFloat(p.Param);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundGameEventPacket(r.ReadByte(), r.ReadFloat()));

    internal static PacketCodec<ClientboundLevelEventPacket> MakeLevelEvent(BlockPosLayout layout) =>
        PacketCodec<ClientboundLevelEventPacket>.Of(
            (ref PacketWriter w, ClientboundLevelEventPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.EffectId);
                w.WriteBlockPos(p.Position, layout);
                w.WriteInt(p.Data);
                w.WriteBool(p.Global);
            },
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundLevelEventPacket(r.ReadInt(), r.ReadBlockPos(layout), r.ReadInt(), r.ReadBool()));

    /// <summary>The 1.20.5+ level-particles frame, where the particle (its type id AND its options) moved to the END of the packet. Version 1.20.5 writes <c>overrideLimiter, x, y, z, xDist, yDist, zDist, maxSpeed, count</c> and only then the particle payload. Earlier versions write the type id first.</summary>
    /// <param name="era">The era's particle tables and frame shape.</param>
    /// <returns>The codec.</returns>
    internal static PacketCodec<ClientboundLevelParticlesPacket> MakeModernParticles(ParticlesWire era) =>
        PacketCodec<ClientboundLevelParticlesPacket>.Of(
            (ref PacketWriter w, ClientboundLevelParticlesPacket p, PacketCodecContext _) =>
            {
                w.WriteBool(p.OverrideLimiter);
                if (era.HasAlwaysShow)
                    w.WriteBool(p.AlwaysShow);

                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteFloat(p.XDist);
                w.WriteFloat(p.YDist);
                w.WriteFloat(p.ZDist);
                w.WriteFloat(p.MaxSpeed);
                w.WriteInt(p.Count);
                ParticleCodec.WriteModern(ref w, p.Particle);
            },
            (ref PacketReader r, PacketCodecContext ctx) =>
            {
                bool limiter = r.ReadBool();
                bool alwaysShow = !era.HasAlwaysShow || r.ReadBool();
                double x = r.ReadDouble();
                double y = r.ReadDouble();
                double z = r.ReadDouble();
                float xd = r.ReadFloat();
                float yd = r.ReadFloat();
                float zd = r.ReadFloat();
                float speed = r.ReadFloat();
                int count = r.ReadInt();
                ParticleData particle = ParticleCodec.ReadModern(ref r, era.Shapes, era.Icons, ctx);
                return new ClientboundLevelParticlesPacket(limiter, alwaysShow, x, y, z, xd, yd, zd, speed, count, particle);
            },
            WireShape.OfEra("level_particles_modern", era));

    /// <summary>The 1.14-1.20.4 level-particles frame, where the particle TYPE ID is written FIRST and only the type-specific options trail the packet. Two things vary across that band and nothing else does.</summary>
    /// <param name="varIntTypeId">False for 477-758, where the id is a raw big-endian int; true from 1.19 (759), where it became a VarInt through protocol 765.</param>
    /// <param name="doublePosition">False for 477-498, where x/y/z are floats; true from 1.15 (573), where they became doubles. Twelve bytes of difference means a frame read under the wrong form never lines up.</param>
    /// <returns>The codec.</returns>
    /// <remarks>The trailing options are captured as the frame remainder rather than decoded: UMPK has no pre-1.20.5 particle option table, and a raw capture re-encodes byte-for-byte. That is the same honest policy the 1.13 codec already used.</remarks>
    internal static PacketCodec<ClientboundLevelParticlesPacket> MakeTypeIdFirstParticles(
        bool varIntTypeId, bool doublePosition) =>
        PacketCodec<ClientboundLevelParticlesPacket>.Of(
            (ref PacketWriter w, ClientboundLevelParticlesPacket p, PacketCodecContext _) =>
            {
                if (varIntTypeId)
                    w.WriteVarInt(p.Particle.TypeId);

                else
                    w.WriteInt(p.Particle.TypeId);

                w.WriteBool(p.OverrideLimiter);
                if (doublePosition)
                {
                    w.WriteDouble(p.X);
                    w.WriteDouble(p.Y);
                    w.WriteDouble(p.Z);
                }
                else
                {
                    w.WriteFloat((float)p.X);
                    w.WriteFloat((float)p.Y);
                    w.WriteFloat((float)p.Z);
                }

                w.WriteFloat(p.XDist);
                w.WriteFloat(p.YDist);
                w.WriteFloat(p.ZDist);
                w.WriteFloat(p.MaxSpeed);
                w.WriteInt(p.Count);
                w.WriteBytes(p.Particle.Options ?? []);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int typeId = varIntTypeId ? r.ReadVarInt() : r.ReadInt();
                bool limiter = r.ReadBool();
                double x = doublePosition ? r.ReadDouble() : r.ReadFloat();
                double y = doublePosition ? r.ReadDouble() : r.ReadFloat();
                double z = doublePosition ? r.ReadDouble() : r.ReadFloat();
                float xd = r.ReadFloat();
                float yd = r.ReadFloat();
                float zd = r.ReadFloat();
                float speed = r.ReadFloat();
                int count = r.ReadInt();
                byte[] options = r.ReadRemaining().ToArray();
                return new ClientboundLevelParticlesPacket(
                    limiter, AlwaysShow: false, x, y, z, xd, yd, zd, speed, count, new ParticleData(typeId, options));
            });

    // Shared helpers

    internal static void WriteVec3(ref PacketWriter w, Vec3d v)
    {
        w.WriteDouble(v.X);
        w.WriteDouble(v.Y);
        w.WriteDouble(v.Z);
    }

    internal static Vec3d ReadVec3(ref PacketReader r) => new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

    internal static Vec3d? ReadOptionalVec3(ref PacketReader r) =>
        r.ReadOptionalStruct(static (ref PacketReader sr) => ReadVec3(ref sr));

    // Modern ChunkPos long: z in the high 32 bits, x in the low 32 (as an unsigned int).
    internal static void WriteChunkPos(ref PacketWriter w, ChunkPos pos) =>
        w.WriteLong(((long)pos.Z << 32) | (pos.X & 0xFFFFFFFFL));

    internal static ChunkPos ReadChunkPos(ref PacketReader r)
    {
        long value = r.ReadLong();
        return new ChunkPos((int)(value & 0xFFFFFFFFL), (int)(value >> 32));
    }

    internal static void WriteLightData(ref PacketWriter w, LightUpdateData light)
    {
        WriteLongArray(ref w, light.SkyYMask);
        WriteLongArray(ref w, light.BlockYMask);
        WriteLongArray(ref w, light.EmptySkyYMask);
        WriteLongArray(ref w, light.EmptyBlockYMask);
        WriteNibbleArrays(ref w, light.SkyUpdates);
        WriteNibbleArrays(ref w, light.BlockUpdates);
    }

    internal static LightUpdateData ReadLightData(ref PacketReader r)
    {
        long[] sky = ReadLongArray(ref r);
        long[] block = ReadLongArray(ref r);
        long[] emptySky = ReadLongArray(ref r);
        long[] emptyBlock = ReadLongArray(ref r);
        byte[][] skyUpdates = ReadNibbleArrays(ref r);
        byte[][] blockUpdates = ReadNibbleArrays(ref r);
        return new LightUpdateData(sky, block, emptySky, emptyBlock, skyUpdates, blockUpdates);
    }

    internal static void WriteLongArray(ref PacketWriter w, long[] values)
    {
        w.WriteVarInt(values.Length);
        for (int i = 0; i < values.Length; i++)
            w.WriteLong(values[i]);

    }

    internal static long[] ReadLongArray(ref PacketReader r)
    {
        int len = r.ReadVarInt();
        var values = new long[len];
        for (int i = 0; i < len; i++)
            values[i] = r.ReadLong();

        return values;
    }

    internal static void WriteNibbleArrays(ref PacketWriter w, IReadOnlyList<byte[]> arrays)
    {
        w.WriteVarInt(arrays.Count);
        for (int i = 0; i < arrays.Count; i++)
        {
            if (arrays[i].Length != LightArrayLength)
                throw new ProtocolViolationException($"A light nibble array must be {LightArrayLength} bytes, got {arrays[i].Length}.");

            w.WriteByteArray(arrays[i]);
        }
    }

    internal static byte[][] ReadNibbleArrays(ref PacketReader r)
    {
        int count = r.ReadVarInt();
        var arrays = new byte[count][];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> bytes = r.ReadByteArray();
            if (bytes.Length != LightArrayLength)
                throw new ProtocolViolationException($"A light nibble array must be {LightArrayLength} bytes, got {bytes.Length}.");

            arrays[i] = bytes.ToArray();
        }

        return arrays;
    }

    internal static ParticleData ReadLegacyParticleArgs(ref PacketReader r, int typeId)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var echo = new PacketWriter(buffer);
        int args = LegacyParticleArgumentCount(typeId);
        for (int i = 0; i < args; i++)
            echo.WriteVarInt(r.ReadVarInt());

        return new ParticleData(typeId, buffer.WrittenSpan.ToArray());
    }

    internal static int LegacyParticleArgumentCount(int typeId) => typeId switch
    {
        36 => 2,
        37 => 1,
        38 => 1,
        _ => 0,
    };

    // 1.8 map patch: columns byte; if 0, done. Else rows, startX, startY unsigned bytes, then a VarInt-prefixed color byte array.
    internal static void WriteMapPatchLegacy(ref PacketWriter w, MapPatch patch)
    {
        w.WriteByte(patch.Columns);
        if (patch.Columns == 0)
            return;

        w.WriteByte(patch.Rows);
        w.WriteByte(patch.StartX);
        w.WriteByte(patch.StartY);
        w.WriteByteArray(patch.Colors ?? []);
    }

    internal static MapPatch ReadMapPatchLegacy(ref PacketReader r)
    {
        byte columns = r.ReadByte();
        if (columns == 0)
            return new MapPatch(0, 0, 0, 0, []);

        byte rows = r.ReadByte();
        byte startX = r.ReadByte();
        byte startY = r.ReadByte();
        byte[] colors = r.ReadByteArray().ToArray();
        return new MapPatch(columns, rows, startX, startY, colors);
    }

    // Modern map patch: same shape as 1.8 (columns byte gating the rest).
    internal static void WriteMapPatchModern(ref PacketWriter w, MapPatch patch) => WriteMapPatchLegacy(ref w, patch);

    internal static MapPatch ReadMapPatchModern(ref PacketReader r) => ReadMapPatchLegacy(ref r);
}
