using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class RecipeCodecs
{
    /// <summary>recipe-book-remove: VarInt-prefixed list of recipe display ids.</summary>
    public static PacketCodec<ClientboundRecipeBookRemovePacket> RecipeBookRemoveModern { get; } =
        PacketCodec<ClientboundRecipeBookRemovePacket>.Of(
            static (ref PacketWriter w, ClientboundRecipeBookRemovePacket p, PacketCodecContext _) =>
                w.WriteList(p.RecipeIds, static (ref PacketWriter iw, int id) => iw.WriteVarInt(id)),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundRecipeBookRemovePacket(r.ReadList(static (ref PacketReader ir) => ir.ReadVarInt())));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRecipeBookRemove(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Clientbound.RecipeBookRemove)
            .From(JavaEras.WideIds, RecipeCodecs.RecipeBookRemoveModern);
    }
}
