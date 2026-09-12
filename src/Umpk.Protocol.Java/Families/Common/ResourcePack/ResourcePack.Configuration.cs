using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Resource pack response (<c>minecraft:resource_pack</c>), serverbound.</summary>
        public static readonly PacketType<ServerboundConfigResourcePackPacket> ResourcePack =
            new(ProtocolPhase.Configuration, PacketFlow.Serverbound, Identifier.Minecraft("resource_pack"));
    }
}

/// <summary>Configuration resource-pack response: uuid + action ordinal.</summary>
public sealed record ServerboundConfigResourcePackPacket(Guid Id, int Action) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.ResourcePack;
}
