using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Join game / login (<c>minecraft:login</c>), the first play packet.</summary>
        public static readonly PacketType<ClientboundLoginPacket> Login =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("login"));
    }
}

/// <summary>Join game / login, the first play packet. Fields are the version superset; each era codec reads only what its version has. 1.8: entity id, gamemode+hardcore byte, dimension byte, difficulty, max players, level type, reduced-debug flag. 1.20.6+: the modern CommonPlayerSpawnInfo shape. The 26.2 (V26_2) era adds a trailing online-mode boolean before enforces-secure-chat.</summary>
public sealed record ClientboundLoginPacket(
    int PlayerId,
    bool Hardcore,
    IReadOnlyList<string> Dimensions,
    int MaxPlayers,
    int ViewDistance,
    int SimulationDistance,
    bool ReducedDebugInfo,
    bool ShowDeathScreen,
    bool DoLimitedCrafting,
    CommonPlayerSpawnInfo SpawnInfo,
    bool OnlineMode,
    bool EnforcesSecureChat,
    LegacyLoginFields? Legacy) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.Login;

    /// <summary>The registry-codec NBT blob embedded in JoinGame on 1.16-1.20.1 (the descriptor-declared first-packet registry update). Carried verbatim so the frame re-encodes byte-exact; null on 1.20.2+ where the registries moved into the configuration phase.</summary>
    public Umpk.Nbt.NbtTag? JoinGameRegistry { get; init; }

    /// <summary>The current-dimension <c>DimensionType</c> NBT compound carried inline in JoinGame on 1.16.2-1.18.2 (protocols 751-758), where the dimension type is sent as a named-root NBT compound (<c>DimensionType.CODEC</c>) rather than a resource-key string. Carried verbatim so the frame re-encodes byte-exact; null on 1.16 (dimension type is a string in <see cref="CommonPlayerSpawnInfo.DimensionTypeName"/>) and on 1.19+ (string form again).</summary>
    public Umpk.Nbt.NbtTag? DimensionTypeNbt { get; init; }
}

/// <summary>The 1.8-era JoinGame fields, present only on the legacy codec.</summary>
public sealed record LegacyLoginFields(sbyte Dimension, byte Difficulty, string LevelType);
