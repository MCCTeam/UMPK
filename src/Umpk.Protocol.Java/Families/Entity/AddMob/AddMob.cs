using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 spawn mob (<c>minecraft:add_mob</c>).</summary>
        public static readonly PacketType<ClientboundAddMobPacket> AddMob =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("add_mob"));
    }
}

/// <summary>Spawn mob / living entity. 1.8: fixed-point position, byte type, three byte rotations, short velocity, then legacy metadata (no UUID). 1.9-1.13.2: a UUID after the id, double positions, the 1.9 typed metadata, and a byte type (1.9-1.12) or VarInt type (1.13+); the UUID is carried in <see cref="Uuid"/> so the pre-flattening frame re-encodes byte-exact.</summary>
public sealed record ClientboundAddMobPacket(
    int EntityId,
    int TypeId,
    double X,
    double Y,
    double Z,
    float Yaw,
    float Pitch,
    float HeadPitch,
    short VelocityX,
    short VelocityY,
    short VelocityZ,
    EntityMetadataList Metadata,
    Guid Uuid = default) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.AddMob;
}
