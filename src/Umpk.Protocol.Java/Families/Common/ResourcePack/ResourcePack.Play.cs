using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Legacy 1.8 resource pack send (<c>minecraft:resource_pack</c>, 47).</summary>
        public static readonly PacketType<ClientboundLegacyResourcePackPacket> LegacyResourcePack =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("resource_pack"));
    }

    public static partial class Serverbound
    {
        /// <summary>Resource pack response (<c>minecraft:resource_pack</c> 770/776, <c>minecraft:resource_pack_response</c> 47).</summary>
        public static readonly PacketType<ServerboundResourcePackPacket> ResourcePack =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("resource_pack"));
    }
}

/// <summary>Legacy 1.8 resource pack send (47): a url and a hash.</summary>
public sealed record ClientboundLegacyResourcePackPacket(string Url, string Hash) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.LegacyResourcePack;
}

/// <summary>Resource pack response (770/776): a pack uuid and an action.</summary>
public sealed record ServerboundResourcePackPacket(Guid Id, ResourcePackAction Action) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.ResourcePack;
}
