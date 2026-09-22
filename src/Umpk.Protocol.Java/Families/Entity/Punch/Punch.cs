using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Punch (<c>minecraft:punch</c>, 26.3+): replaces <c>minecraft:swing</c>.</summary>
        public static readonly PacketType<ServerboundPunchPacket> Punch =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("punch"));
    }
}

/// <summary>Punch (serverbound play, empty payload, 26.3+). The frame carries nothing: its whole meaning is its arrival, which replaces the old swing-arm send.</summary>
public sealed record ServerboundPunchPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.Punch;
}
