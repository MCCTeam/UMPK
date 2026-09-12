using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Respawn (<c>minecraft:respawn</c>).</summary>
        public static readonly PacketType<ClientboundRespawnPacket> Respawn =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("respawn"));
    }
}

/// <summary>Respawn. From 1.20.2 (protocol 764) the packet is a <see cref="CommonPlayerSpawnInfo"/> block plus a data-to-keep mask byte; 1.16-1.20.1 spell the same fields inline; 1.8-1.15.2 are a flat dimension/gamemode/level-type block carried in <see cref="Legacy"/>.</summary>
public sealed record ClientboundRespawnPacket(
    CommonPlayerSpawnInfo? SpawnInfo,
    byte DataToKeep,
    LegacyRespawnFields? Legacy) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.Respawn;

    /// <summary>The current-dimension <c>DimensionType</c> NBT compound carried inline on 1.16.2-1.18.2 (protocols 751-758), where respawn sends the dimension type as a named-root NBT compound (<c>DimensionType.CODEC</c>) rather than a resource-key string. Carried verbatim so the frame re-encodes byte-exact; null on every other era.</summary>
    public Umpk.Nbt.NbtTag? DimensionTypeNbt { get; init; }
}

/// <summary>The pre-1.16 respawn fields: an int dimension id, a gamemode byte and a level-type string, plus the difficulty byte that only 1.8-1.13.2 carry.</summary>
public sealed record LegacyRespawnFields(int Dimension, byte Difficulty, byte GameMode, string LevelType)
{
    /// <summary>The hashed world seed 1.15-1.15.2 (protocols 573-578) insert between the dimension id and the gamemode byte. Zero on every other legacy era, which does not carry it.</summary>
    public long Seed { get; init; }
}
