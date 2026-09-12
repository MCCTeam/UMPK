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
    /// <summary>place-recipe (1.21.2+): VarInt container id, VarInt recipe display id, bool use-max-items.</summary>
    public static PacketCodec<ServerboundPlaceRecipePacket> PlaceRecipeModern { get; } =
        PacketCodec<ServerboundPlaceRecipePacket>.Of(
            static (ref PacketWriter w, ServerboundPlaceRecipePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteVarInt(p.RecipeId);
                w.WriteBool(p.UseMaxItems);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPlaceRecipePacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadBool()));

    /// <summary>place-recipe (1.14-1.20.1): byte container id, a resource-location recipe identifier, bool use-max-items. The container id and recipe became a VarInt container id and a network recipe-display id only in 1.21.2.</summary>
    public static PacketCodec<ServerboundPlaceRecipeByNamePacket> PlaceRecipeByNameV1_13 { get; } =
        PacketCodec<ServerboundPlaceRecipeByNamePacket>.Of(
            static (ref PacketWriter w, ServerboundPlaceRecipeByNamePacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.ContainerId);
                w.WriteString(p.Recipe.ToString());
                w.WriteBool(p.UseMaxItems);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int containerId = r.ReadByte();
                Identifier recipe = Identifier.Parse(r.ReadString());
                return new ServerboundPlaceRecipeByNamePacket(containerId, recipe, r.ReadBool());
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlaceRecipe(PacketBindings bindings)
    {
        // 1.13-1.21.1 carries the recipe as a resource-location identifier (byte window + identifier + bool), so the 393-404 and 764-767 markers were gaps in the middle and at the top of one era. 1.21.2+ switched to a VarInt window id + network recipe-display id. 338/340 stay a marker: 1.12 sends a NUMERIC crafting-manager recipe id there, which the by-name record cannot represent.
        bindings.Packet(ItemPackets.Serverbound.PlaceRecipe)
            .MarkerFrom(
                JavaProtocols.V1_12_1,
                MarkerReason.WrongCodecWouldBeWorse,
                "1.12.1 and 1.12.2 send a numeric crafting-manager recipe id where 1.13 and later send a resource location, and the bound codec is the by-name record, so binding it here would write a length-prefixed string into a field the server reads as an int.")
            .FromAs(JavaProtocols.V1_13, ItemPackets.Serverbound.PlaceRecipeByName, RecipeCodecs.PlaceRecipeByNameV1_13)
            .From(JavaProtocols.V1_21_2, RecipeCodecs.PlaceRecipeModern);
    }
}
