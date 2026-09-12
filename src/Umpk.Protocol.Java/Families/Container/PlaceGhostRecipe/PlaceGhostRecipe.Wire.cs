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
    /// <summary>place-ghost-recipe: VarInt container id, then the recipe-display payload preserved raw.</summary>
    public static PacketCodec<ClientboundPlaceGhostRecipePacket> PlaceGhostRecipeModern { get; } =
        PacketCodec<ClientboundPlaceGhostRecipePacket>.Of(
            static (ref PacketWriter w, ClientboundPlaceGhostRecipePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.ContainerId);
                w.WriteBytes(p.RecipeDisplay);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                byte[] rest = r.ReadRemaining().ToArray();
                return new ClientboundPlaceGhostRecipePacket(id, rest);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlaceGhostRecipe(PacketBindings bindings)
    {
        // The ghost-recipe frame is a container id followed by the recipe reference; the reference is a numeric crafting-manager id on 1.12, a resource location on 1.13-1.21.1, and a structural RecipeDisplay from 1.21.2. This codec carries the reference verbatim, so it is correct on every one of those bands; the only wire assumption is the leading container id, and a signed byte and a VarInt encode identically over the 1..100 range assigned to menu containers. 338-404 and 764-767 were pure marker gaps rather than eras.
        bindings.Packet(ItemPackets.Clientbound.PlaceGhostRecipe)
            .From(JavaProtocols.V1_12_1, RecipeCodecs.PlaceGhostRecipeModern);
    }
}
