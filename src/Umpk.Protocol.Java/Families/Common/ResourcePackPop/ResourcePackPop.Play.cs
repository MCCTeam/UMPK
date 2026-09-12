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
        /// <summary>Resource pack pop (<c>minecraft:resource_pack_pop</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundResourcePackPopPacket> ResourcePackPop =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("resource_pack_pop"));
    }
}

/// <summary>Resource pack pop (770/776): an optional pack uuid (absent pops all).</summary>
public sealed record ClientboundResourcePackPopPacket(Guid? Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ResourcePackPop;
}
