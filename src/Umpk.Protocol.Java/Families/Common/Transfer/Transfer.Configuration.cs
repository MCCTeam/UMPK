using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Transfer (<c>minecraft:transfer</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigTransferPacket> Transfer =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("transfer"));
    }
}

/// <summary>Configuration transfer: hand the client off to another server host/port.</summary>
public sealed record ClientboundConfigTransferPacket(string Host, int Port) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.Transfer;
}
