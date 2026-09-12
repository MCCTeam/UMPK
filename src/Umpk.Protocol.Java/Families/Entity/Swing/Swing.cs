using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Swing arm (<c>minecraft:swing</c>).</summary>
        public static readonly PacketType<ServerboundSwingPacket> Swing =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("swing"));
    }
}

/// <summary>Swing arm: the hand (0 main, 1 off) as a VarInt; 1.8 sends no fields (<see cref="HasHand"/> false).</summary>
public sealed record ServerboundSwingPacket(int Hand, bool HasHand) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.Swing;
}
