using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set-compression threshold (<c>minecraft:login_compression</c>).</summary>
        public static readonly PacketType<ClientboundLoginCompressionPacket> LoginCompression =
            new(ProtocolPhase.Login, PacketFlow.Clientbound, Identifier.Minecraft("login_compression"));
    }
}

/// <summary>Set-compression threshold; frames larger than the threshold are zlib-compressed afterward.</summary>
public sealed record ClientboundLoginCompressionPacket(int Threshold) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginPackets.Clientbound.LoginCompression;
}
