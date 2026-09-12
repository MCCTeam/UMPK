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
        // resource pack

        /// <summary>Resource pack push (<c>minecraft:resource_pack_push</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundResourcePackPushPacket> ResourcePackPush =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("resource_pack_push"));
    }
}

/// <summary>Resource pack push (770/776): a pack uuid, url, hash, required flag, and optional prompt.</summary>
public sealed record ClientboundResourcePackPushPacket(
    Guid Id,
    string Url,
    string Hash,
    bool Required,
    Component? Prompt) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ResourcePackPush;
}
