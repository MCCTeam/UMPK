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
    /// <summary>update-recipes: the whole payload preserved raw (nested recipe-property trees).</summary>
    public static PacketCodec<ClientboundUpdateRecipesPacket> UpdateRecipesModern { get; } =
        PacketCodec<ClientboundUpdateRecipesPacket>.Of(
            static (ref PacketWriter w, ClientboundUpdateRecipesPacket p, PacketCodecContext _) => w.WriteBytes(p.Payload),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundUpdateRecipesPacket(r.ReadRemaining().ToArray()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUpdateRecipes(PacketBindings bindings)
    {
        // update_recipes carries a VarInt count and then one nested, per-recipe-serializer property tree per entry, whose shape is a function of the recipe type rather than of the protocol. The codec preserves the whole payload verbatim, so it is byte-exact on every band the identifier exists on and the 393-404 and 764-767 markers were gaps, not eras. TODO(recipe-tree): decode structurally.
        bindings.Packet(ItemPackets.Clientbound.UpdateRecipes)
            .From(JavaEras.Flattening, RecipeCodecs.UpdateRecipesModern);
    }
}
