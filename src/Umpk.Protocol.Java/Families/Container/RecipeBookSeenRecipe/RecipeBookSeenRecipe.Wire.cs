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
    /// <summary>recipe-book-seen-recipe (1.21.2+): VarInt recipe display id.</summary>
    public static PacketCodec<ServerboundRecipeBookSeenRecipePacket> RecipeBookSeenRecipeModern { get; } =
        PacketCodec<ServerboundRecipeBookSeenRecipePacket>.Of(
            static (ref PacketWriter w, ServerboundRecipeBookSeenRecipePacket p, PacketCodecContext _) => w.WriteVarInt(p.RecipeId),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundRecipeBookSeenRecipePacket(r.ReadVarInt()));

    /// <summary>recipe-book-seen-recipe (1.16.2-1.21.1): a single resource-location recipe identifier. Verified The display-id VarInt only arrives at 1.21.2, so the modern member cannot serve this band: it would send a bare VarInt where the server expects a length-prefixed string.</summary>
    public static PacketCodec<ServerboundRecipeBookSeenRecipeByNamePacket> RecipeBookSeenRecipeByNameV1_16_2 { get; } =
        PacketCodec<ServerboundRecipeBookSeenRecipeByNamePacket>.Of(
            static (ref PacketWriter w, ServerboundRecipeBookSeenRecipeByNamePacket p, PacketCodecContext _) =>
                w.WriteString(p.Recipe.ToString()),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundRecipeBookSeenRecipeByNamePacket(Identifier.Parse(r.ReadString())));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRecipeBookSeenRecipe(PacketBindings bindings)
    {
        // The seen-recipe value is a resource location on 1.16.2-1.21.1 and a VarInt recipe-display id from 1.21.2. The earlier form uses the by-name record, matching place_recipe's split model.
        bindings.Packet(ItemPackets.Serverbound.RecipeBookSeenRecipe)
            .FromAs(JavaProtocols.V1_16_2, ItemPackets.Serverbound.RecipeBookSeenRecipeByName, RecipeCodecs.RecipeBookSeenRecipeByNameV1_16_2)
            .From(JavaProtocols.V1_21_2, RecipeCodecs.RecipeBookSeenRecipeModern);
    }
}
