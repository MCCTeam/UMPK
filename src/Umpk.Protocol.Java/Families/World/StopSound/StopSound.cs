using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Stop sound (<c>minecraft:stop_sound</c>).</summary>
        public static readonly PacketType<ClientboundStopSoundPacket> StopSound =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("stop_sound"));
    }
}

/// <summary>Stop sound: an optional sound source and an optional sound name, gated by a flags byte.</summary>
public sealed record ClientboundStopSoundPacket(int? Source, string? Name) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.StopSound;
}
