using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Health (<c>minecraft:set_health</c> / 1.8 <c>update_health</c>).</summary>
        public static readonly PacketType<ClientboundSetHealthPacket> SetHealth =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_health"));
    }
}

/// <summary>Health: health float, food VarInt, saturation float.</summary>
public sealed record ClientboundSetHealthPacket(float Health, int Food, float Saturation) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetHealth;
}
