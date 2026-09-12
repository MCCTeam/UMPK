using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class JoinGameCodecs
{
    /// <summary>1.8-era JoinGame.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_8 = Create(JoinGameWire.Legacy1_8);

    /// <summary>1.21.5-era JoinGame (modern CommonPlayerSpawnInfo, no online-mode flag).</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_21_2 = Create(JoinGameWire.Modern);

    /// <summary>26.1-era JoinGame (same shape as 1.21.5).</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V26_1 = Create(JoinGameWire.Modern);

    /// <summary>26.2-era JoinGame (adds a trailing online-mode boolean).</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V26_2 = Create(JoinGameWire.Modern with { HasOnlineMode = true });

    /// <summary>393-404 (1.13-1.13.2) JoinGame. The 1.9-1.13 flat shape: it matches the 1.8 layout except the dimension is a full <c>i32</c> (1.8 wrote it as a byte). Fields: int playerId; u8 gameMode (0x8 = hardcore); i32 dimension; u8 difficulty; u8 maxPlayers; string(16) levelType; bool reducedDebugInfo. 1.14 dropped difficulty and added a view-distance VarInt, so this cannot reuse V1_14.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_9_1 =
        PacketCodec<ClientboundLoginPacket>.Of(
            static (ref PacketWriter w, ClientboundLoginPacket p, PacketCodecContext _) =>
            {
                LegacyLoginFields legacy = p.Legacy
                    ?? throw new ProtocolViolationException("1.13 JoinGame requires the legacy fields block.");
                int gamemode = p.SpawnInfo.GameType & 0x7;
                if (p.Hardcore)
                    gamemode |= 0x8;

                w.WriteInt(p.PlayerId);
                w.WriteByte((byte)gamemode);
                w.WriteInt(legacy.Dimension);
                w.WriteByte(legacy.Difficulty);
                w.WriteByte((byte)p.MaxPlayers);
                w.WriteString(legacy.LevelType, 16);
                w.WriteBool(p.ReducedDebugInfo);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int playerId = r.ReadInt();
                byte gm = r.ReadByte();
                bool hardcore = (gm & 0x8) != 0;
                int dimension = r.ReadInt();
                byte difficulty = r.ReadByte();
                byte maxPlayers = r.ReadByte();
                string levelType = r.ReadString(16);
                bool reducedDebug = r.ReadBool();
                var spawn = new CommonPlayerSpawnInfo(-1, LegacyDimensionName(dimension), 0, (sbyte)(gm & 0x7), -1, false, false, null, 0, 0);
                return new ClientboundLoginPacket(
                    playerId, hardcore, [], maxPlayers, 0, 0, reducedDebug, true, false, spawn, true, false,
                    new LegacyLoginFields((sbyte)dimension, difficulty, levelType));
            });

    private static PacketCodec<ClientboundLoginPacket> Create(JoinGameWire wire) =>
        PacketCodec<ClientboundLoginPacket>.Of(
            (ref PacketWriter w, ClientboundLoginPacket p, PacketCodecContext _) => Encode(ref w, p, wire),
            (ref PacketReader r, PacketCodecContext _) => Decode(ref r, wire),
            WireShape.OfEra("join_game", wire));

    private static void Encode(ref PacketWriter w, ClientboundLoginPacket p, JoinGameWire wire)
    {
        w.WriteInt(p.PlayerId);
        if (wire.Legacy)
        {
            LegacyLoginFields legacy = p.Legacy
                ?? throw new ProtocolViolationException("Legacy JoinGame requires the legacy fields block.");
            int gamemode = p.SpawnInfo.GameType & 0x7;
            if (p.Hardcore)
                gamemode |= 0x8;

            w.WriteByte((byte)gamemode);
            w.WriteByte((byte)legacy.Dimension);
            w.WriteByte(legacy.Difficulty);
            w.WriteByte((byte)p.MaxPlayers);
            w.WriteString(legacy.LevelType, 16);
            w.WriteBool(p.ReducedDebugInfo);
            return;
        }

        WriteModernJoinHeader(ref w, p);
        CommonSpawnInfoCodec.Write(ref w, p.SpawnInfo, CommonSpawnInfoWire.V1_21_2);
        if (wire.HasOnlineMode)
            w.WriteBool(p.OnlineMode);

        w.WriteBool(p.EnforcesSecureChat);
    }

    // The 1.20.2+ JoinGame header block, byte-identical between the netty-modern-with-CommonPlayerSpawnInfo member (770/776) and the 764-767 member: hardcore bool, the dimension resource-key list, then the max-players / view-distance / simulation-distance VarInts and the reduced-debug / show-death / limited-crafting bools. It sits after the player-id int and before the spawn-info block, whose per-era differences ride in the CommonSpawnInfoWire each member hands the shared spawn-info codec.
    private static void WriteModernJoinHeader(ref PacketWriter w, ClientboundLoginPacket p)
    {
        w.WriteBool(p.Hardcore);
        w.WriteList(p.Dimensions, static (ref PacketWriter dw, string d) => dw.WriteString(d));
        w.WriteVarInt(p.MaxPlayers);
        w.WriteVarInt(p.ViewDistance);
        w.WriteVarInt(p.SimulationDistance);
        w.WriteBool(p.ReducedDebugInfo);
        w.WriteBool(p.ShowDeathScreen);
        w.WriteBool(p.DoLimitedCrafting);
    }

    private static ModernJoinHeader ReadModernJoinHeader(ref PacketReader r)
    {
        bool hardcore = r.ReadBool();
        string[] dims = r.ReadList(static (ref PacketReader dr) => dr.ReadString());
        int maxPlayers = r.ReadVarInt();
        int viewDistance = r.ReadVarInt();
        int simDistance = r.ReadVarInt();
        bool reducedDebug = r.ReadBool();
        bool showDeath = r.ReadBool();
        bool limitedCrafting = r.ReadBool();
        return new ModernJoinHeader(hardcore, dims, maxPlayers, viewDistance, simDistance, reducedDebug, showDeath, limitedCrafting);
    }

    private static ClientboundLoginPacket Decode(ref PacketReader r, JoinGameWire wire)
    {
        int playerId = r.ReadInt();
        if (wire.Legacy)
        {
            byte gm = r.ReadByte();
            bool hardcore = (gm & 0x8) != 0;
            sbyte dimension = r.ReadSByte();
            byte difficulty = r.ReadByte();
            byte maxPlayers = r.ReadByte();
            string levelType = r.ReadString(16);
            bool reducedDebug = r.ReadBool();
            var spawn = new CommonPlayerSpawnInfo(-1, LegacyDimensionName(dimension), 0, (sbyte)(gm & 0x7), -1, false, false, null, 0, 0);
            return new ClientboundLoginPacket(
                playerId, hardcore, [], maxPlayers, 0, 0, reducedDebug, true, false, spawn, true, false,
                new LegacyLoginFields(dimension, difficulty, levelType));
        }

        ModernJoinHeader header = ReadModernJoinHeader(ref r);
        CommonPlayerSpawnInfo spawnInfo = CommonSpawnInfoCodec.Read(ref r, CommonSpawnInfoWire.V1_21_2);
        bool onlineMode = wire.HasOnlineMode && r.ReadBool();
        bool enforcesSecureChat = r.ReadBool();
        return new ClientboundLoginPacket(
            playerId, header.Hardcore, header.Dimensions, header.MaxPlayers, header.ViewDistance,
            header.SimulationDistance, header.ReducedDebugInfo, header.ShowDeathScreen,
            header.DoLimitedCrafting, spawnInfo, onlineMode, enforcesSecureChat, Legacy: null);
    }

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLogin(PacketBindings bindings)
    {
        // join-game The dimension field is the one wire delta across the 1.9 range: an i8 on 1.9.0 (protocol 107, byte-identical to 1.8) and widened to an i32 from 1.9.1 onward - the same int-dimension shape 1.13 uses. 1.16-1.20.1 carry the RegistryAccess NBT blob in this first packet (1.20 adds a trailing portal-cooldown VarInt); 1.20.2 moves registry sync into the configuration phase. 1.21.2's spawn-info tail is wire-shared with 1.21.5. 26.1/26.2 take their own wire deltas.
        bindings.Packet(PlayPackets.Clientbound.Login)
            .From(JavaProtocols.V1_8, JoinGameCodecs.V1_8)
            .From(JavaProtocols.V1_9_1, JoinGameCodecs.V1_9_1)
            .From(JavaProtocols.V1_14, JoinGameCodecs.V1_14)
            .From(JavaProtocols.V1_15, JoinGameCodecs.V1_15)
            .From(JavaProtocols.V1_16, JoinGameCodecs.V1_16)
            .From(JavaProtocols.V1_16_2, JoinGameCodecs.V1_16_2)
            .From(JavaProtocols.V1_18, JoinGameCodecs.V1_18)
            .From(JavaProtocols.V1_19, JoinGameCodecs.V1_19)
            .From(JavaProtocols.V1_20, JoinGameCodecs.V1_20)
            .From(JavaProtocols.V1_20_2, JoinGameCodecs.V1_20_2)
            .From(JavaProtocols.V1_20_5, JoinGameCodecs.V1_20_5)
            .From(JavaProtocols.V1_21_2, JoinGameCodecs.V1_21_2)
            .From(JavaProtocols.V26_1, JoinGameCodecs.V26_1)
            .From(JavaProtocols.V26_2, JoinGameCodecs.V26_2);
    }

    /// <summary>759/760/761/762 (1.19-1.19.4): no trailing portal cooldown.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_19 = MakeSigningEra(hasPortalCooldown: false);

    /// <summary>763 (1.20/1.20.1): adds a trailing VarInt portal cooldown.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_20 = MakeSigningEra(hasPortalCooldown: true);

    private static PacketCodec<ClientboundLoginPacket> MakeSigningEra(bool hasPortalCooldown) =>
        PacketCodec<ClientboundLoginPacket>.Of(
            (ref PacketWriter w, ClientboundLoginPacket p, PacketCodecContext _) =>
            {
                CommonPlayerSpawnInfo s = p.SpawnInfo;
                w.WriteInt(p.PlayerId);
                w.WriteBool(p.Hardcore);
                w.WriteByte((byte)s.GameType);
                w.WriteByte((byte)s.PreviousGameType);
                w.WriteList(p.Dimensions, static (ref PacketWriter dw, string d) => dw.WriteString(d));
                w.WriteNbt(p.JoinGameRegistry ?? NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);
                w.WriteString(s.DimensionTypeName ?? string.Empty);
                w.WriteString(s.Dimension);
                w.WriteLong(s.Seed);
                w.WriteVarInt(p.MaxPlayers);
                w.WriteVarInt(p.ViewDistance);
                w.WriteVarInt(p.SimulationDistance);
                w.WriteBool(p.ReducedDebugInfo);
                w.WriteBool(p.ShowDeathScreen);
                w.WriteBool(s.IsDebug);
                w.WriteBool(s.IsFlat);
                if (s.LastDeathDimensionAndPos is long pos)
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
                int playerId = r.ReadInt();
                bool hardcore = r.ReadBool();
                sbyte gameType = r.ReadSByte();
                sbyte prevGameType = r.ReadSByte();
                string[] levels = r.ReadList(static (ref PacketReader dr) => dr.ReadString());
                NbtTag registry = r.ReadNbt(NbtWireFormat.JavaNamedRoot);
                string dimensionType = r.ReadString();
                string dimension = r.ReadString();
                long seed = r.ReadLong();
                int maxPlayers = r.ReadVarInt();
                int viewDistance = r.ReadVarInt();
                int simulationDistance = r.ReadVarInt();
                bool reducedDebug = r.ReadBool();
                bool showDeath = r.ReadBool();
                bool isDebug = r.ReadBool();
                bool isFlat = r.ReadBool();
                long? lastDeathPos = null;
                string? lastDeathDim = null;
                if (r.ReadBool())
                {
                    lastDeathDim = r.ReadString();
                    lastDeathPos = r.ReadLong();
                }
                int portalCooldown = hasPortalCooldown ? r.ReadVarInt() : 0;

                CommonPlayerSpawnInfo spawn = new(
                    DimensionTypeId: 0,
                    Dimension: dimension,
                    Seed: seed,
                    GameType: gameType,
                    PreviousGameType: prevGameType,
                    IsDebug: isDebug,
                    IsFlat: isFlat,
                    LastDeathDimensionAndPos: lastDeathPos,
                    PortalCooldown: portalCooldown,
                    SeaLevel: 0)
                {
                    DimensionTypeName = dimensionType,
                    LastDeathDimension = lastDeathDim,
                };

                return new ClientboundLoginPacket(
                    PlayerId: playerId,
                    Hardcore: hardcore,
                    Dimensions: levels,
                    MaxPlayers: maxPlayers,
                    ViewDistance: viewDistance,
                    SimulationDistance: simulationDistance,
                    ReducedDebugInfo: reducedDebug,
                    ShowDeathScreen: showDeath,
                    DoLimitedCrafting: false,
                    SpawnInfo: spawn,
                    OnlineMode: false,
                    EnforcesSecureChat: false,
                    Legacy: null)
                {
                    JoinGameRegistry = registry,
                };
            });

    /// <summary>764/765 (1.20.2-1.20.4) JoinGame.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_20_2 =
        Create764To767(CommonSpawnInfoWire.V1_20_2, hasEnforcesSecureChat: false);

    /// <summary>766/767 (1.20.5-1.21.1) JoinGame.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_20_5 =
        Create764To767(CommonSpawnInfoWire.V1_20_5, hasEnforcesSecureChat: true);

    private static PacketCodec<ClientboundLoginPacket> Create764To767(CommonSpawnInfoWire spawnInfo, bool hasEnforcesSecureChat) =>
        PacketCodec<ClientboundLoginPacket>.Of(
            (ref PacketWriter w, ClientboundLoginPacket p, PacketCodecContext _) =>
            {
                w.WriteInt(p.PlayerId);
                WriteModernJoinHeader(ref w, p);
                CommonSpawnInfoCodec.Write(ref w, p.SpawnInfo, spawnInfo);
                if (hasEnforcesSecureChat)
                    w.WriteBool(p.EnforcesSecureChat);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int playerId = r.ReadInt();
                ModernJoinHeader header = ReadModernJoinHeader(ref r);
                CommonPlayerSpawnInfo info = CommonSpawnInfoCodec.Read(ref r, spawnInfo);
                bool enforcesSecureChat = hasEnforcesSecureChat && r.ReadBool();
                return new ClientboundLoginPacket(
                    playerId, header.Hardcore, header.Dimensions, header.MaxPlayers, header.ViewDistance,
                    header.SimulationDistance, header.ReducedDebugInfo, header.ShowDeathScreen,
                    header.DoLimitedCrafting, info, OnlineMode: false, enforcesSecureChat, Legacy: null);
            });

    /// <summary>477-498 (1.14-1.14.4): no seed, no show-death-screen bool.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_14 = MakeFlatteningEra(hasSeed: false, hasShowDeathScreen: false);

    /// <summary>573-578 (1.15-1.15.2): adds a long seed after the dimension and a trailing show-death-screen bool.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_15 = MakeFlatteningEra(hasSeed: true, hasShowDeathScreen: true);

    private static PacketCodec<ClientboundLoginPacket> MakeFlatteningEra(bool hasSeed, bool hasShowDeathScreen) =>
        PacketCodec<ClientboundLoginPacket>.Of(
            (ref PacketWriter w, ClientboundLoginPacket p, PacketCodecContext _) =>
            {
                LegacyLoginFields legacy = p.Legacy
                    ?? throw new ProtocolViolationException("Flattening-era JoinGame requires the legacy fields block.");
                int gm = p.SpawnInfo.GameType & 0x7;
                if (p.Hardcore)
                    gm |= 0x8;

                w.WriteInt(p.PlayerId);
                w.WriteByte((byte)gm);
                w.WriteInt(legacy.Dimension);
                if (hasSeed)
                    w.WriteLong(p.SpawnInfo.Seed);

                w.WriteByte((byte)p.MaxPlayers);
                w.WriteString(legacy.LevelType, 16);
                w.WriteVarInt(p.ViewDistance);
                w.WriteBool(p.ReducedDebugInfo);
                if (hasShowDeathScreen)
                    w.WriteBool(p.ShowDeathScreen);

            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int playerId = r.ReadInt();
                byte gm = r.ReadByte();
                bool hardcore = (gm & 0x8) != 0;
                int dimension = r.ReadInt();
                long seed = hasSeed ? r.ReadLong() : 0;
                byte maxPlayers = r.ReadByte();
                string levelType = r.ReadString(16);
                int chunkRadius = r.ReadVarInt();
                bool reducedDebug = r.ReadBool();
                bool showDeath = hasShowDeathScreen ? r.ReadBool() : true;

                CommonPlayerSpawnInfo spawn = new(-1, LegacyDimensionName(dimension), seed, (sbyte)(gm & 0x7), -1, false, false, null, 0, 0);
                return new ClientboundLoginPacket(
                    PlayerId: playerId,
                    Hardcore: hardcore,
                    Dimensions: [],
                    MaxPlayers: maxPlayers,
                    ViewDistance: chunkRadius,
                    SimulationDistance: 0,
                    ReducedDebugInfo: reducedDebug,
                    ShowDeathScreen: showDeath,
                    DoLimitedCrafting: false,
                    SpawnInfo: spawn,
                    OnlineMode: false,
                    EnforcesSecureChat: false,
                    Legacy: new LegacyLoginFields((sbyte)dimension, 0, levelType));
            });

    /// <summary>735/736 (1.16/1.16.1) JoinGame.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_16 =
        MakeNettyModern(NettyLoginWire.V1_16);

    /// <summary>751/753/754 (1.16.2-1.16.5) and 755/756 (1.17/1.17.1) JoinGame (wire-identical).</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_16_2 =
        MakeNettyModern(NettyLoginWire.V1_16_2);

    /// <summary>757/758 (1.18-1.18.2) JoinGame: adds a trailing varint simulationDistance.</summary>
    public static readonly PacketCodec<ClientboundLoginPacket> V1_18 =
        MakeNettyModern(NettyLoginWire.V1_18);

    private static PacketCodec<ClientboundLoginPacket> MakeNettyModern(
        NettyLoginWire era) =>
        PacketCodec<ClientboundLoginPacket>.Of(
            (ref PacketWriter w, ClientboundLoginPacket p, PacketCodecContext _) =>
            {
                CommonPlayerSpawnInfo s = p.SpawnInfo;
                w.WriteInt(p.PlayerId);
                if (era.ByteGamemode)
                {
                    byte g = (byte)((byte)s.GameType & 0x07);
                    if (p.Hardcore)
                        g |= 0x08;

                    w.WriteByte(g);
                }
                else
                {
                    w.WriteBool(p.Hardcore);
                    w.WriteByte((byte)s.GameType);
                }

                w.WriteByte((byte)s.PreviousGameType);
                w.WriteList(p.Dimensions, static (ref PacketWriter dw, string d) => dw.WriteString(d));
                w.WriteNbt(p.JoinGameRegistry ?? NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);
                if (era.NbtDimensionType)
                    w.WriteNbt(p.DimensionTypeNbt ?? NbtEnd.Instance, NbtWireFormat.JavaNamedRoot);

                else
                    w.WriteString(s.DimensionTypeName ?? string.Empty);

                w.WriteString(s.Dimension);
                w.WriteLong(s.Seed);
                if (era.ByteMaxPlayers)
                    w.WriteByte((byte)p.MaxPlayers);

                else
                    w.WriteVarInt(p.MaxPlayers);

                w.WriteVarInt(p.ViewDistance);
                if (era.HasSimulationDistance)
                    w.WriteVarInt(p.SimulationDistance);

                w.WriteBool(p.ReducedDebugInfo);
                w.WriteBool(p.ShowDeathScreen);
                w.WriteBool(s.IsDebug);
                w.WriteBool(s.IsFlat);
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int playerId = r.ReadInt();
                bool hardcore;
                sbyte gameType;
                if (era.ByteGamemode)
                {
                    byte g = r.ReadByte();
                    hardcore = (g & 0x08) == 0x08;
                    gameType = (sbyte)(g & 0x07);
                }
                else
                {
                    hardcore = r.ReadBool();
                    gameType = r.ReadSByte();
                }

                sbyte prevGameType = r.ReadSByte();
                string[] levels = r.ReadList(static (ref PacketReader dr) => dr.ReadString());
                NbtTag registry = r.ReadNbt(NbtWireFormat.JavaNamedRoot);
                NbtTag? dimensionTypeNbt = null;
                string? dimensionTypeName = null;
                if (era.NbtDimensionType)
                    dimensionTypeNbt = r.ReadNbt(NbtWireFormat.JavaNamedRoot);

                else
                    dimensionTypeName = r.ReadString();

                string dimension = r.ReadString();
                long seed = r.ReadLong();
                int maxPlayers = era.ByteMaxPlayers ? r.ReadByte() : r.ReadVarInt();
                int viewDistance = r.ReadVarInt();
                int simulationDistance = era.HasSimulationDistance ? r.ReadVarInt() : 0;
                bool reducedDebug = r.ReadBool();
                bool showDeath = r.ReadBool();
                bool isDebug = r.ReadBool();
                bool isFlat = r.ReadBool();

                CommonPlayerSpawnInfo spawn = new(
                    DimensionTypeId: 0,
                    Dimension: dimension,
                    Seed: seed,
                    GameType: gameType,
                    PreviousGameType: prevGameType,
                    IsDebug: isDebug,
                    IsFlat: isFlat,
                    LastDeathDimensionAndPos: null,
                    PortalCooldown: 0,
                    SeaLevel: 0)
                {
                    DimensionTypeName = dimensionTypeName,
                };

                return new ClientboundLoginPacket(
                    PlayerId: playerId,
                    Hardcore: hardcore,
                    Dimensions: levels,
                    MaxPlayers: maxPlayers,
                    ViewDistance: viewDistance,
                    SimulationDistance: simulationDistance,
                    ReducedDebugInfo: reducedDebug,
                    ShowDeathScreen: showDeath,
                    DoLimitedCrafting: false,
                    SpawnInfo: spawn,
                    OnlineMode: false,
                    EnforcesSecureChat: false,
                    Legacy: null)
                {
                    JoinGameRegistry = registry,
                    DimensionTypeNbt = dimensionTypeNbt,
                };
            },
            WireShape.OfEra("join_game_netty", era));
}

/// <summary>Per-era layout flags for JoinGame, resolved at codec construction.</summary>
/// <param name="Legacy">True for the 1.8 flat shape (no CommonPlayerSpawnInfo, no dimension list).</param>
/// <param name="HasOnlineMode">True for the 26.2 (V26_2) trailing online-mode boolean.</param>
public readonly record struct JoinGameWire(bool Legacy, bool HasOnlineMode)
{
    /// <summary>The 1.8 flat JoinGame layout.</summary>
    public static JoinGameWire Legacy1_8 => new(Legacy: true, HasOnlineMode: false);

    /// <summary>The modern CommonPlayerSpawnInfo layout (1.20.6+ without the online-mode flag).</summary>
    public static JoinGameWire Modern => new(Legacy: false, HasOnlineMode: false);

    /// <inheritdoc />
    public override string ToString() => $"legacy={(Legacy ? 1 : 0)},onlinemode={(HasOnlineMode ? 1 : 0)}";
}

/// <summary>
/// The protocol 735-758 JoinGame (<c>minecraft:login</c>) layout. This range carries the whole <c>RegistryAccess</c> as a named-root NBT blob inside the first play packet (the descriptor-declared first-packet registry exception), and (from 1.16.2) the current dimension type as an inline named-root NBT compound. Hardcore encoding, dimension-type representation, max-player width, and simulation distance change independently across the range:
/// <list type="bullet">
/// <item>1.16/1.16.1 (V1_16): int playerId; BYTE gamemode (hardcore = bit 0x8, gameType = low bits);
/// byte previousGameType; collection&lt;resourceLocation&gt; levels; NBT registryHolder; resourceLocation dimensionType (STRING); resourceLocation dimension; long seed; BYTE maxPlayers; varint chunkRadius; bool reducedDebug/showDeath/isDebug/isFlat.</item>
/// <item>1.16.2-1.17.1 (V1_16_2, shared by 751-756): int playerId; bool hardcore; byte gameType;
/// byte previousGameType; collection levels; NBT registryHolder; NBT dimensionType (DimensionType.CODEC compound); resourceLocation dimension; long seed; varint maxPlayers; varint chunkRadius; four bools. The 1.16.2 reshuffle (hardcore split out, gameType a full byte, dimensionType inline NBT, maxPlayers a varint); 1.17/1.17.1 are byte-identical.</item>
/// <item>1.18-1.18.2 (V1_18): the V1_16_2 shape plus a trailing varint simulationDistance after
/// chunkRadius (before the four bools).</item>
/// </list>
/// Resource keys and locations are their location strings; the NBT tags use named-root compounds.
/// </summary>
/// <param name="ByteGamemode">1.16/1.16.1: hardcore rides bit 0x08 of the gamemode byte.</param>
/// <param name="NbtDimensionType">1.16.2+: the dimension type is an inline NBT compound, not a string.</param>
/// <param name="ByteMaxPlayers">1.16/1.16.1: maxPlayers is a byte; a VarInt from 1.16.2.</param>
/// <param name="HasSimulationDistance">1.18+: a simulation-distance VarInt after the chunk radius.</param>
internal readonly record struct NettyLoginWire(
    bool ByteGamemode,
    bool NbtDimensionType,
    bool ByteMaxPlayers,
    bool HasSimulationDistance)
{
    /// <summary>735/736 (1.16/1.16.1).</summary>
    internal static NettyLoginWire V1_16 { get; } =
        new(ByteGamemode: true, NbtDimensionType: false, ByteMaxPlayers: true, HasSimulationDistance: false);

    /// <summary>751-756 (1.16.2-1.17.1).</summary>
    internal static NettyLoginWire V1_16_2 { get; } =
        new(ByteGamemode: false, NbtDimensionType: true, ByteMaxPlayers: false, HasSimulationDistance: false);

    /// <summary>757/758 (1.18-1.18.2).</summary>
    internal static NettyLoginWire V1_18 { get; } = V1_16_2 with { HasSimulationDistance = true };

    /// <inheritdoc />
    public override string ToString() =>
        $"bytegamemode={(ByteGamemode ? 1 : 0)},nbtdimtype={(NbtDimensionType ? 1 : 0)}," +
        $"bytemaxplayers={(ByteMaxPlayers ? 1 : 0)},simdistance={(HasSimulationDistance ? 1 : 0)}";
}
