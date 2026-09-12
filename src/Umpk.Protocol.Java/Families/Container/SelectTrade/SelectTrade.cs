using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class ItemPackets
{
    public static partial class Serverbound
    {
        /// <summary>Select a merchant trade (<c>minecraft:select_trade</c>).</summary>
        public static readonly PacketType<ServerboundSelectTradePacket> SelectTrade =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("select_trade"));
    }
}

/// <summary>Select a merchant trade by list index.</summary>
/// <param name="SelectedSlot">The trade index.</param>
public sealed record ServerboundSelectTradePacket(int SelectedSlot) : IPacket
{
    /// <inheritdoc/>
    public PacketType Type => ItemPackets.Serverbound.SelectTrade;
}
