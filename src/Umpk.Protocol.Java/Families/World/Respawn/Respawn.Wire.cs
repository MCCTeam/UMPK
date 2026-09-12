using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldStateCodecs
{
    //
    // Ten era shapes. Respawn is the packet that tells a client it changed dimension or came back from the death screen, so a wrong shape here is session-fatal on the first death and on the first portal. The wire layouts are:
    //
    //  47-404   (1.8-1.13.2)   int dimension, difficulty byte, gamemode byte, level type.
    //  477-498  (1.14-1.14.4)  the difficulty byte is removed.
    //  573-578  (1.15-1.15.2)  a world-seed long follows the dimension.
    //  735-736  (1.16-1.16.1)  two resource locations, a long, two bytes, and three bools.
    //  751-758  (1.16.2-1.18.2) the dimension type becomes
    //                          an inline named-root NBT compound; the rest is
    //                          unchanged.
    //  759-762  (1.19-1.19.4)  the dimension type becomes a resource-key string again and an optional
    //                          last-death location is appended. Protocol 761 widens the keep-all bool
    //                          to a data-to-keep byte, which has the same wire width.
    //  763      (1.20-1.20.1)  the 1.19 layout plus a trailing portal-cooldown VarInt.
    //  764-765  (1.20.2-1.20.4) CommonPlayerSpawnInfo + data-to-keep byte, dimension type as a resource
    //                          key string.
    //  766-767  (1.20.5-1.21.1) the same with a VarInt registry-id dimension type.
    //  768-776  (1.21.2+)      the same plus a trailing sea-level VarInt.
    //
    // CommonPlayerSpawnInfo does not exist below 764, so the modern codec cannot serve earlier bands.

    /// <summary>47-404 (1.8-1.13.2): int dimension, difficulty byte, gamemode byte, level-type string.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_8 = MakeLegacyRespawn(hasDifficulty: true, hasSeed: false);

    /// <summary>477-498 (1.14-1.14.4): the 1.8 shape with the difficulty byte removed.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_14 = MakeLegacyRespawn(hasDifficulty: false, hasSeed: false);

    /// <summary>573-578 (1.15-1.15.2): a hashed world seed between the dimension id and the gamemode.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_15 = MakeLegacyRespawn(hasDifficulty: false, hasSeed: true);

    /// <summary>735/736 (1.16-1.16.1): dimension type as a resource-key STRING.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_16 = MakeNettyModernRespawn(nbtDimensionType: false);

    /// <summary>751-758 (1.16.2-1.18.2): dimension type as an inline named-root NBT compound.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_16_2 = MakeNettyModernRespawn(nbtDimensionType: true);

    /// <summary>759-762 (1.19-1.19.4): resource-key dimension type plus the optional last-death block.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_19 = MakeSigningEraRespawn(hasPortalCooldown: false);

    /// <summary>763 (1.20-1.20.1): the 1.19 shape plus a trailing portal-cooldown VarInt.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_20 = MakeSigningEraRespawn(hasPortalCooldown: true);

    /// <summary>764/765 (1.20.2-1.20.4): CommonPlayerSpawnInfo (string dimension type) + data-to-keep byte.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_20_2 = MakeCommonSpawnInfoRespawn(CommonSpawnInfoWire.V1_20_2);

    /// <summary>766/767 (1.20.5-1.21.1): CommonPlayerSpawnInfo (VarInt dimension type) + data-to-keep byte.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_20_5 = MakeCommonSpawnInfoRespawn(CommonSpawnInfoWire.V1_20_5);

    /// <summary>768-776 (1.21.2+): the 766 shape plus the trailing sea-level VarInt.</summary>
    public static readonly PacketCodec<ClientboundRespawnPacket> RespawnV1_21_2 = MakeCommonSpawnInfoRespawn(CommonSpawnInfoWire.V1_21_2);

    /// <summary>The pre-1.16 shape: an int dimension id, the difficulty byte 1.8-1.13.2 carry, the hashed world seed 1.15 inserts, then the gamemode byte and the level-type string. No era carries both the difficulty byte and the seed.</summary>
    private static PacketCodec<ClientboundRespawnPacket> MakeLegacyRespawn(bool hasDifficulty, bool hasSeed) =>
        PacketCodec<ClientboundRespawnPacket>.Of(
            (ref PacketWriter w, ClientboundRespawnPacket p, PacketCodecContext _) =>
            {
                LegacyRespawnFields l = p.Legacy ?? throw new ProtocolViolationException("Legacy respawn requires the legacy fields.");
                w.WriteInt(l.Dimension);
                if (hasDifficulty)
                    w.WriteByte(l.Difficulty);

                if (hasSeed)
                    w.WriteLong(l.Seed);

                w.WriteByte(l.GameMode);
                w.WriteString(l.LevelType, 16);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int dim = r.ReadInt();
                byte difficulty = hasDifficulty ? r.ReadByte() : (byte)0;
                long seed = hasSeed ? r.ReadLong() : 0;
                byte gameMode = r.ReadByte();
                string levelType = r.ReadString(16);
                return new ClientboundRespawnPacket(SpawnInfo: null, DataToKeep: 0,
                    new LegacyRespawnFields(dim, difficulty, gameMode, levelType) { Seed = seed });
            });

    /// <summary>The 735-758 shape. The dimension type is a resource-key string on 735/736 and an inline named-root NBT compound from 751; everything after it is identical, ending in the keep-all-player-data bool that later eras widen into the data-to-keep mask.</summary>
    private static PacketCodec<ClientboundRespawnPacket> MakeNettyModernRespawn(bool nbtDimensionType) =>
        PacketCodec<ClientboundRespawnPacket>.Of(
            (ref PacketWriter w, ClientboundRespawnPacket p, PacketCodecContext _) =>
            {
                CommonPlayerSpawnInfo s = p.SpawnInfo ?? throw new ProtocolViolationException("1.16-1.18.2 respawn requires spawn info.");
                if (nbtDimensionType)
                    w.WriteNbt(p.DimensionTypeNbt ?? NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);

                else
                    w.WriteString(s.DimensionTypeName ?? string.Empty);

                w.WriteString(s.Dimension);
                w.WriteLong(s.Seed);
                w.WriteByte((byte)s.GameType);
                w.WriteByte((byte)s.PreviousGameType);
                w.WriteBool(s.IsDebug);
                w.WriteBool(s.IsFlat);
                w.WriteBool(p.DataToKeep != 0);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                NbtTag? dimensionTypeNbt = null;
                string? dimensionTypeName = null;
                if (nbtDimensionType)
                    dimensionTypeNbt = r.ReadNbt(NbtWireFormat.JavaNamedRoot);

                else
                    dimensionTypeName = r.ReadString();

                string dimension = r.ReadString();
                long seed = r.ReadLong();
                sbyte gameType = r.ReadSByte();
                sbyte prevGameType = r.ReadSByte();
                bool isDebug = r.ReadBool();
                bool isFlat = r.ReadBool();
                bool keepAll = r.ReadBool();

                CommonPlayerSpawnInfo spawn = new(
                    DimensionTypeId: 0, dimension, seed, gameType, prevGameType, isDebug, isFlat,
                    LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 0)
                {
                    DimensionTypeName = dimensionTypeName,
                };

                return new ClientboundRespawnPacket(spawn, keepAll ? (byte)1 : (byte)0, Legacy: null)
                {
                    DimensionTypeNbt = dimensionTypeNbt,
                };
            });

    /// <summary>The 759-763 shape: the dimension type is a resource-key string again, the keep-all flag is one byte (a bool on 759/760, a data-to-keep mask from 761, the same width either way), and an optional dimension-and-position last-death block follows. Protocol 763 appends the portal-cooldown VarInt.</summary>
    private static PacketCodec<ClientboundRespawnPacket> MakeSigningEraRespawn(bool hasPortalCooldown) =>
        PacketCodec<ClientboundRespawnPacket>.Of(
            (ref PacketWriter w, ClientboundRespawnPacket p, PacketCodecContext _) =>
            {
                CommonPlayerSpawnInfo s = p.SpawnInfo ?? throw new ProtocolViolationException("1.19-1.20.1 respawn requires spawn info.");
                w.WriteString(s.DimensionTypeName ?? string.Empty);
                w.WriteString(s.Dimension);
                w.WriteLong(s.Seed);
                w.WriteByte((byte)s.GameType);
                w.WriteByte((byte)s.PreviousGameType);
                w.WriteBool(s.IsDebug);
                w.WriteBool(s.IsFlat);
                w.WriteByte(p.DataToKeep);
                if (s.LastDeathDimensionAndPos is { } pos)
                {
                    w.WriteBool(true);
                    w.WriteString(s.LastDeathDimension ?? s.Dimension);
                    w.WriteLong(pos);
                }
                else
                    w.WriteBool(false);

                if (hasPortalCooldown)
                    w.WriteVarInt(s.PortalCooldown);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string dimensionType = r.ReadString();
                string dimension = r.ReadString();
                long seed = r.ReadLong();
                sbyte gameType = r.ReadSByte();
                sbyte prevGameType = r.ReadSByte();
                bool isDebug = r.ReadBool();
                bool isFlat = r.ReadBool();
                byte dataToKeep = r.ReadByte();
                long? lastDeathPos = null;
                string? lastDeathDim = null;
                if (r.ReadBool())
                {
                    lastDeathDim = r.ReadString();
                    lastDeathPos = r.ReadLong();
                }

                int portalCooldown = hasPortalCooldown ? r.ReadVarInt() : 0;

                CommonPlayerSpawnInfo spawn = new(
                    DimensionTypeId: 0, dimension, seed, gameType, prevGameType, isDebug, isFlat,
                    lastDeathPos, portalCooldown, SeaLevel: 0)
                {
                    DimensionTypeName = dimensionType,
                    LastDeathDimension = lastDeathDim,
                };

                return new ClientboundRespawnPacket(spawn, dataToKeep, Legacy: null);
            });

    /// <summary>764 onward: the shared spawn-info block, then the data-to-keep mask byte.</summary>
    private static PacketCodec<ClientboundRespawnPacket> MakeCommonSpawnInfoRespawn(CommonSpawnInfoWire wire) =>
        PacketCodec<ClientboundRespawnPacket>.Of(
            (ref PacketWriter w, ClientboundRespawnPacket p, PacketCodecContext _) =>
            {
                CommonSpawnInfoCodec.Write(
                    ref w,
                    p.SpawnInfo ?? throw new ProtocolViolationException("1.20.2+ respawn requires spawn info."),
                    wire);
                w.WriteByte(p.DataToKeep);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                CommonPlayerSpawnInfo info = CommonSpawnInfoCodec.Read(ref r, wire);
                return new ClientboundRespawnPacket(info, r.ReadByte(), Legacy: null);
            },
            WireShape.Of("spawn_info,byte", wire.ToString()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRespawn(PacketBindings bindings)
    {
        // Ten era forms match the field boundaries below. CommonPlayerSpawnInfo begins at protocol 764;
        // using that shape below 764 faults every death or dimension-change frame.
        bindings.Packet(WorldPackets.Clientbound.Respawn)
            .From(JavaProtocols.V1_8, WorldStateCodecs.RespawnV1_8)
            .From(JavaProtocols.V1_14, WorldStateCodecs.RespawnV1_14)
            .From(JavaProtocols.V1_15, WorldStateCodecs.RespawnV1_15)
            .From(JavaProtocols.V1_16, WorldStateCodecs.RespawnV1_16)
            .From(JavaProtocols.V1_16_2, WorldStateCodecs.RespawnV1_16_2)
            .From(JavaProtocols.V1_19, WorldStateCodecs.RespawnV1_19)
            .From(JavaProtocols.V1_20, WorldStateCodecs.RespawnV1_20)
            .From(JavaProtocols.V1_20_2, WorldStateCodecs.RespawnV1_20_2)
            .From(JavaProtocols.V1_20_5, WorldStateCodecs.RespawnV1_20_5)
            .From(JavaProtocols.V1_21_2, WorldStateCodecs.RespawnV1_21_2);
    }
}
