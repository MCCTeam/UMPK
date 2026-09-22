using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Accept teleportation (<c>minecraft:accept_teleportation</c>).</summary>
        public static readonly PacketType<ServerboundAcceptTeleportationPacket> AcceptTeleportation =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("accept_teleportation"));
    }
}

/// <summary>Accept teleportation: the VarInt teleport id being confirmed, plus (26.3+) the echoed destination. Older eras carry the id alone; each era codec reads only what its version has.</summary>
public sealed record ServerboundAcceptTeleportationPacket(int TeleportId) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.AcceptTeleportation;

    /// <summary>The echoed destination X, sent only on 26.3+.</summary>
    public double X { get; init; }

    /// <summary>The echoed destination Y, sent only on 26.3+.</summary>
    public double Y { get; init; }

    /// <summary>The echoed destination Z, sent only on 26.3+.</summary>
    public double Z { get; init; }

    /// <summary>The echoed destination yaw, sent only on 26.3+.</summary>
    public float YRot { get; init; }

    /// <summary>The echoed destination pitch, sent only on 26.3+.</summary>
    public float XRot { get; init; }

    /// <summary>Whether the echoed destination is present. False for legacy one-argument construction, true for six-argument construction. The 26.3 writer rejects an absent destination rather than emitting zeroes.</summary>
    public bool HasDestination { get; init; }

    /// <summary>Builds a 26.3+ teleport confirmation carrying the echoed destination.</summary>
    public ServerboundAcceptTeleportationPacket(int teleportId, double x, double y, double z, float yRot, float xRot)
        : this(teleportId)
    {
        X = x;
        Y = y;
        Z = z;
        YRot = yRot;
        XRot = xRot;
        HasDestination = true;
    }
}
