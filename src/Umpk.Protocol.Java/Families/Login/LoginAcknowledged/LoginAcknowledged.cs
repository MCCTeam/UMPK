using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginPackets
{
    public static partial class Serverbound
    {
        /// <summary>Login acknowledged, entering configuration (<c>minecraft:login_acknowledged</c>, 1.20.2+).</summary>
        public static readonly PacketType<ServerboundLoginAcknowledgedPacket> LoginAcknowledged =
            new(ProtocolPhase.Login, PacketFlow.Serverbound, Identifier.Minecraft("login_acknowledged"));
    }
}

/// <summary>Login acknowledged (1.20.2+): terminal login packet, handing off to the configuration phase.</summary>
public sealed record ServerboundLoginAcknowledgedPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginPackets.Serverbound.LoginAcknowledged;
}
