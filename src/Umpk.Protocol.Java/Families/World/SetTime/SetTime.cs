using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Time update (<c>minecraft:set_time</c> / 1.8 <c>update_time</c>).</summary>
        public static readonly PacketType<ClientboundSetTimePacket> SetTime =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_time"));
    }
}

// Time, spawn, respawn, and difficulty

/// <summary>Time update. The 1.8/1.21.5 shape carries <see cref="GameTime"/> and <see cref="DayTime"/> (with a 1.21.5-only tick-day-time flag); the 26.2 shape drops those two and carries a per-world-clock map (<see cref="ClockUpdates"/>), kept as raw payload bytes because the WorldClock registry is host state.</summary>
public sealed record ClientboundSetTimePacket(
    long GameTime,
    long DayTime,
    bool TickDayTime,
    byte[] ClockUpdates) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetTime;
}
