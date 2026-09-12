using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Configuration disconnect (<c>minecraft:disconnect</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigDisconnectPacket> Disconnect =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("disconnect"));
    }
}

/// <summary>Configuration disconnect carrying a reason component (NBT).</summary>
public sealed record ClientboundConfigDisconnectPacket(Component Reason) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.Disconnect;
}
