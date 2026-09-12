using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Ping (<c>minecraft:ping</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigPingPacket> Ping =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("ping"));
    }
}

/// <summary>Configuration ping carrying an int id echoed by pong.</summary>
public sealed record ClientboundConfigPingPacket(int Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.Ping;
}
