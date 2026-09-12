using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class MerchantCodecs
{
    /// <summary>select-trade: VarInt selected slot.</summary>
    public static PacketCodec<ServerboundSelectTradePacket> SelectTradeModern { get; } =
        PacketCodec<ServerboundSelectTradePacket>.Of(
            static (ref PacketWriter w, ServerboundSelectTradePacket p, PacketCodecContext _) => w.WriteVarInt(p.SelectedSlot),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundSelectTradePacket(r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSelectTrade(PacketBindings bindings)
    {
        // A single VarInt slot since the packet arrived at 1.13; the 393-404 marker was a gap. Verified in the 1.13.2 packet layout.
        bindings.Packet(ItemPackets.Serverbound.SelectTrade)
            .From(JavaEras.Flattening, MerchantCodecs.SelectTradeModern);
    }
}
