using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Resource pack push (<c>minecraft:resource_pack_push</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigResourcePackPushPacket> ResourcePackPush =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("resource_pack_push"));
    }
}

/// <summary>Configuration resource-pack push: uuid, url, hash, required flag, optional prompt.</summary>
public sealed record ClientboundConfigResourcePackPushPacket(
    Guid Id, string Url, string Hash, bool Required, Component? Prompt) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.ResourcePackPush;
}
