using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Login
    {
        /// <summary>Login plugin request (<c>minecraft:custom_query</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundLoginCustomQueryPacket> CustomQuery =
            new(ProtocolPhase.Login, PacketFlow.Clientbound, Identifier.Minecraft("custom_query"));
    }
}

/// <summary>Login plugin request (custom_query): a transaction id, a channel, and raw payload bytes.</summary>
public sealed record ClientboundLoginCustomQueryPacket(int TransactionId, Identifier Channel, byte[] Data) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Login.CustomQuery;
}
