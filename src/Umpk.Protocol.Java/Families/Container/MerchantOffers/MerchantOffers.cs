using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Clientbound
    {
        /// <summary>Merchant/villager trade offers (<c>minecraft:merchant_offers</c>).</summary>
        public static readonly PacketType<ClientboundMerchantOffersPacket> MerchantOffers =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("merchant_offers"));
    }
}

/// <summary>Merchant/villager trade offers.</summary>
/// <param name="ContainerId">The window id.</param>
/// <param name="Offers">The trade offers.</param>
public sealed record ClientboundMerchantOffersPacket(int ContainerId, MerchantOffers Offers) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Clientbound.MerchantOffers;
}
