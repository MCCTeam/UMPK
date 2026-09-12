using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The common spawn-info block introduced at 1.20.2 (protocol 764) and shared by JoinGame and Respawn. Three era forms exist and the differences are all at the two ends of the block, so they are resolved from an explicit <see cref="CommonSpawnInfoWire"/> rather than re-derived.</summary>
/// <remarks>
/// <para>The record begins with two resource keys, a long, two bytes, two bools, an optional last-death position, and a portal-cooldown VarInt. The version-dependent fields wrap this common middle.</para>
/// <list type="bullet">
/// <item>764/765 (1.20.2-1.20.4) open with the dimension type as a RESOURCE KEY STRING.</item>
/// <item>766/767 (1.20.5-1.21.1) replace it with a plain VarInt registry id.</item>
/// <item>768+ (1.21.2 onward) append a sea-level VarInt after the portal cooldown.</item>
/// </list>
/// <para>The dimension type from protocol 766 is a plain registry-id VarInt, not the holder form where 0 selects an inline value and n+1 selects id n. Reading it as the holder scheme is fatal wherever the id happens to be 0, because the decoder then tries to parse the dimension resource key's own length prefix as an NBT tag type.</para>
/// </remarks>
public static class CommonSpawnInfoCodec
{
    /// <summary>Writes a common player spawn info block in the given era's shape.</summary>
    public static void Write(ref PacketWriter w, CommonPlayerSpawnInfo info, CommonSpawnInfoWire wire)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (wire.DimensionTypeAsString)
            w.WriteString(info.DimensionTypeName ?? "minecraft:overworld");

        else
            w.WriteVarInt(info.DimensionTypeId);

        w.WriteString(info.Dimension);
        w.WriteLong(info.Seed);
        w.WriteByte((byte)info.GameType);
        w.WriteByte((byte)info.PreviousGameType);
        w.WriteBool(info.IsDebug);
        w.WriteBool(info.IsFlat);
        WriteLastDeath(ref w, info);
        w.WriteVarInt(info.PortalCooldown);
        if (wire.HasSeaLevel)
            w.WriteVarInt(info.SeaLevel);

    }

    /// <summary>Reads a common player spawn info block in the given era's shape.</summary>
    public static CommonPlayerSpawnInfo Read(ref PacketReader r, CommonSpawnInfoWire wire)
    {
        int dimensionTypeId = -1;
        string? dimensionTypeName = null;
        if (wire.DimensionTypeAsString)
            dimensionTypeName = r.ReadString();

        else
            dimensionTypeId = r.ReadVarInt();

        string dimension = r.ReadString();
        long seed = r.ReadLong();
        sbyte gameType = r.ReadSByte();
        sbyte prevGameType = r.ReadSByte();
        bool isDebug = r.ReadBool();
        bool isFlat = r.ReadBool();
        string? lastDeathDimension = null;
        long? lastDeath = null;
        if (r.ReadBool())
        {
            lastDeathDimension = r.ReadString();
            BlockPos pos = r.ReadBlockPos(BlockPosLayout.Packed114);
            lastDeath = PackBlockPos(pos);
        }

        int portalCooldown = r.ReadVarInt();
        int seaLevel = wire.HasSeaLevel ? r.ReadVarInt() : 0;
        return new CommonPlayerSpawnInfo(dimensionTypeId, dimension, seed, gameType, prevGameType,
            isDebug, isFlat, lastDeath, portalCooldown, seaLevel)
        {
            DimensionTypeName = dimensionTypeName,
            LastDeathDimension = lastDeathDimension,
        };
    }

    /// <summary>Packs a block position into the single long the model carries for the last death.</summary>
    internal static long PackBlockPos(BlockPos pos) =>
        ((long)(pos.X & 0x3FFFFFF) << 38) | ((long)(pos.Z & 0x3FFFFFF) << 12) | (pos.Y & 0xFFFL);

    /// <summary>Unpacks the model's single long back into a block position.</summary>
    internal static BlockPos UnpackBlockPos(long packed) =>
        new((int)(packed >> 38), (int)(packed << 52 >> 52), (int)(packed << 26 >> 38));

    private static void WriteLastDeath(ref PacketWriter w, CommonPlayerSpawnInfo info)
    {
        if (info.LastDeathDimensionAndPos is not { } packed)
        {
            w.WriteBool(false);
            return;
        }

        w.WriteBool(true);
        w.WriteString(info.LastDeathDimension ?? info.Dimension);
        w.WriteBlockPos(UnpackBlockPos(packed), BlockPosLayout.Packed114);
    }
}

/// <summary>Per-era layout flags for the <c>CommonPlayerSpawnInfo</c> block, resolved at codec construction.</summary>
/// <param name="DimensionTypeAsString">True on 764/765, where the dimension type is a resource-key string; false from 766, where it is a plain VarInt registry id.</param>
/// <param name="HasSeaLevel">True from 768 (1.21.2), which appends a sea-level VarInt.</param>
public readonly record struct CommonSpawnInfoWire(bool DimensionTypeAsString, bool HasSeaLevel)
{
    /// <summary>764/765 (1.20.2-1.20.4): resource-key dimension type, no sea level.</summary>
    public static CommonSpawnInfoWire V1_20_2 => new(DimensionTypeAsString: true, HasSeaLevel: false);

    /// <summary>766/767 (1.20.5-1.21.1): registry-id dimension type, no sea level.</summary>
    public static CommonSpawnInfoWire V1_20_5 => new(DimensionTypeAsString: false, HasSeaLevel: false);

    /// <summary>768 onward (1.21.2+): registry-id dimension type plus a trailing sea-level VarInt.</summary>
    public static CommonSpawnInfoWire V1_21_2 => new(DimensionTypeAsString: false, HasSeaLevel: true);

    /// <inheritdoc />
    public override string ToString() =>
        $"dimtypestring={(DimensionTypeAsString ? 1 : 0)},sealevel={(HasSeaLevel ? 1 : 0)}";
}
