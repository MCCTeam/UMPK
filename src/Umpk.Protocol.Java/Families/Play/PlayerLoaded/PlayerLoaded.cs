using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Player loaded (<c>minecraft:player_loaded</c>, 1.21.4+): announces that the client has finished loading the level after a join, respawn, or dimension change. Until it arrives (or a 60-tick server-side timeout elapses) the server drops the player's movement and interaction packets.</summary>
        public static readonly PacketType<ServerboundPlayerLoadedPacket> PlayerLoaded =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("player_loaded"));
    }
}

/// <summary>Player loaded (1.21.4+): an empty payload announcing that the level finished loading.</summary>
public sealed record ServerboundPlayerLoadedPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.PlayerLoaded;
}
