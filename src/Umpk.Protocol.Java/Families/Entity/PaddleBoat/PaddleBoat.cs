using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Paddle boat (<c>minecraft:paddle_boat</c>).</summary>
        public static readonly PacketType<ServerboundPaddleBoatPacket> PaddleBoat =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("paddle_boat"));
    }
}

/// <summary>Paddle boat: left and right paddle booleans.</summary>
public sealed record ServerboundPaddleBoatPacket(bool Left, bool Right) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.PaddleBoat;
}
