using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Resource pack pop (<c>minecraft:resource_pack_pop</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigResourcePackPopPacket> ResourcePackPop =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("resource_pack_pop"));
    }
}

/// <summary>Configuration resource-pack pop: pop a resource pack by optional uuid (all when absent).</summary>
public sealed record ClientboundConfigResourcePackPopPacket(Guid? Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.ResourcePackPop;
}
