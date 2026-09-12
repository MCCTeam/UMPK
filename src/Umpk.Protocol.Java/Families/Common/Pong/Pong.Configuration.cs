using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Pong (<c>minecraft:pong</c>), serverbound.</summary>
        public static readonly PacketType<ServerboundConfigPongPacket> Pong =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("pong"));
    }
}

/// <summary>Configuration pong echoing a ping id.</summary>
public sealed record ServerboundConfigPongPacket(int Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.Pong;
}
