using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Server difficulty (<c>minecraft:change_difficulty</c> / 1.8 <c>server_difficulty</c>).</summary>
        public static readonly PacketType<ClientboundChangeDifficultyPacket> ChangeDifficulty =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("change_difficulty"));
    }
}

/// <summary>Server difficulty. 1.8 sends only the difficulty byte (no lock); 1.19+ adds a locked flag.</summary>
public sealed record ClientboundChangeDifficultyPacket(byte Difficulty, bool Locked) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.ChangeDifficulty;
}
