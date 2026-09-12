using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Custom payload / plugin message (<c>minecraft:custom_payload</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigCustomPayloadPacket> CustomPayload =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("custom_payload"));

        /// <summary>Custom payload / plugin message (<c>minecraft:custom_payload</c>), serverbound.</summary>
        public static readonly PacketType<ServerboundConfigCustomPayloadPacket> CustomPayloadServerbound =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("custom_payload"));
    }
}

/// <summary>Configuration custom payload (plugin message): a channel and raw payload bytes.</summary>
public sealed record ClientboundConfigCustomPayloadPacket(Identifier Channel, byte[] Data) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.CustomPayload;
}

/// <summary>Configuration custom payload (plugin message), serverbound.</summary>
public sealed record ServerboundConfigCustomPayloadPacket(Identifier Channel, byte[] Data) : IPacket
{
    /// <inheritdoc />
    /// <remarks>This must return the serverbound type because outbound lookup resolves <c>packet.Type</c> in the serverbound configuration registry.</remarks>
    public PacketType Type => LoginFamilyPackets.Config.CustomPayloadServerbound;
}
