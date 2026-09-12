using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Camera (<c>minecraft:set_camera</c> / 1.8 <c>camera</c>).</summary>
        public static readonly PacketType<ClientboundSetCameraPacket> SetCamera =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_camera"));
    }
}

// Player state

/// <summary>Camera: the spectated entity id.</summary>
public sealed record ClientboundSetCameraPacket(int CameraId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetCamera;
}
