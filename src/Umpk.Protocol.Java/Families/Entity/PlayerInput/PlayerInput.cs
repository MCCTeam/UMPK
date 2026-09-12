using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Player input (<c>minecraft:player_input</c>).</summary>
        public static readonly PacketType<ServerboundPlayerInputPacket> PlayerInput =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("player_input"));
    }
}

/// <summary>Player input (1.21.2+): a bitflags byte of forward/back/left/right/jump/shift/sprint.</summary>
public sealed record ServerboundPlayerInputPacket(
    bool Forward, bool Backward, bool Left, bool Right, bool Jump, bool Shift, bool Sprint) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.PlayerInput;
}
