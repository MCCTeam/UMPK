using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Client command (<c>minecraft:client_command</c>): the respawn request and the statistics request. Present on every supported version and the ONLY way to leave the death screen.</summary>
        public static readonly PacketType<ServerboundClientCommandPacket> ClientCommand =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("client_command"));
    }
}

/// <summary>The client command: respawn, statistics request, or (26.2+) game-rule request. The body is the action ordinal as a VarInt and has not changed since 1.8.</summary>
public sealed record ServerboundClientCommandPacket(ClientCommandAction Action) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ClientCommand;
}

/// <summary>The action carried by <see cref="ServerboundClientCommandPacket"/>. The wire form is the enum ordinal as a VarInt, unchanged from 1.8 to 26.2.</summary>
public enum ClientCommandAction
{
    /// <summary>Leave the death screen and respawn. The only respawn path in the protocol.</summary>
    PerformRespawn = 0,

    /// <summary>Ask the server to send the player's statistics (<c>award_stats</c>).</summary>
    RequestStats = 1,

    /// <summary>Ask the server for the current game-rule values. Added in 26.2; sending it to an older server is an out-of-range ordinal and is rejected there.</summary>
    RequestGameRuleValues = 2,
}
