using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Server links (<c>minecraft:server_links</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigServerLinksPacket> ServerLinks =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("server_links"));
    }
}

/// <summary>Configuration server-links: untrusted server link entries.</summary>
public sealed record ClientboundConfigServerLinksPacket(IReadOnlyList<ServerLinkEntry> Links) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.ServerLinks;
}
