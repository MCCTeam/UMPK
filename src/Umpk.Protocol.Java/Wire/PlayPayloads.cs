using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

/// <summary>The modern spawn-info block shared by JoinGame and Respawn (1.20.6+).</summary>
public sealed record CommonPlayerSpawnInfo(
    int DimensionTypeId,
    string Dimension,
    long Seed,
    sbyte GameType,
    sbyte PreviousGameType,
    bool IsDebug,
    bool IsFlat,
    long? LastDeathDimensionAndPos,
    int PortalCooldown,
    int SeaLevel)
{
    /// <summary>The dimension-type RESOURCE KEY string carried instead of a registry id on 1.20.2-1.20.4 (764/765), where <see cref="DimensionTypeId"/> is not on the wire. Null on 1.20.5+ eras.</summary>
    public string? DimensionTypeName { get; init; }

    /// <summary>The dimension resource-key of the last-death <c>GlobalPos</c> on 1.19-1.20.1, paired with <see cref="LastDeathDimensionAndPos"/> (the packed block position). Null when there is no last death location or on eras that do not carry the dimension separately.</summary>
    public string? LastDeathDimension { get; init; }
}

/// <summary>The client's acknowledgment window for signed chat (offset, 20-bit ack bitset, checksum).</summary>
public sealed record LastSeenMessagesUpdate(int Offset, byte[] Acknowledged, byte Checksum);

/// <summary>One entry of the 1.19.1/1.19.2 (v2) serverbound last-seen list: the sender's profile id and the full signature of the last message this client saw from them. The v2 acknowledgment is a plain list of these pairs; 1.19.3 replaced it with the offset + 20-bit bitset form (<see cref="LastSeenMessagesUpdate"/>) and 1.19 had no acknowledgment at all.</summary>
/// <param name="ProfileId">The uuid of the player whose message is being acknowledged.</param>
/// <param name="Signature">That message's signature, length-prefixed on the wire (not the fixed 256 bytes of v3).</param>
public sealed record LastSeenMessageEntry(Guid ProfileId, byte[] Signature);

/// <summary>One signed command argument: the Brigadier argument name and the signature over that argument's value. Vanilla <c>ArgumentSignatures.Entry</c>. The signature is a VarInt-prefixed byte array on the 1.19 and 1.19.1/1.19.2 eras and a fixed 256-byte <c>MessageSignature</c> from 1.19.3 onward, so the byte count is an era property of the codec rather than of this record.</summary>
/// <param name="Name">The Brigadier argument name (max 16 chars on the wire).</param>
/// <param name="Signature">The RSA-SHA256 signature over the argument value's signed body.</param>
public sealed record SignedCommandArgument(string Name, byte[] Signature);

/// <summary>A block-entity update embedded in a level-chunk packet.</summary>
public sealed record ChunkBlockEntity(BlockPos Position, NbtCompound Nbt);
