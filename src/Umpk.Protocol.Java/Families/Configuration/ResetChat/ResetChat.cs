using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Reset chat (<c>minecraft:reset_chat</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigResetChatPacket> ResetChat =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("reset_chat"));
    }
}

/// <summary>Configuration reset-chat (empty marker, resets the client chat session).</summary>
public sealed record ClientboundConfigResetChatPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.ResetChat;
}
