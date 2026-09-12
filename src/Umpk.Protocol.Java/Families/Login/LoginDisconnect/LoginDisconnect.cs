using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginPackets
{
    public static partial class Clientbound
    {
        /// <summary>Login disconnect with a reason component (<c>minecraft:login_disconnect</c>).</summary>
        public static readonly PacketType<ClientboundLoginDisconnectPacket> Disconnect =
            new(ProtocolPhase.Login, PacketFlow.Clientbound, Identifier.Minecraft("login_disconnect"));
    }
}

/// <summary>Login disconnect carrying a JSON reason component.</summary>
public sealed record ClientboundLoginDisconnectPacket(Component Reason) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginPackets.Clientbound.Disconnect;
}
