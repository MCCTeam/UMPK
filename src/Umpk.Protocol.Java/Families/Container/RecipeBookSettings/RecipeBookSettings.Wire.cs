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
    /// <summary>recipe-book-settings: four fixed (open, filtering) pairs.</summary>
    public static PacketCodec<ClientboundRecipeBookSettingsPacket> RecipeBookSettingsModern { get; } =
        PacketCodec<ClientboundRecipeBookSettingsPacket>.Of(
            static (ref PacketWriter w, ClientboundRecipeBookSettingsPacket p, PacketCodecContext _) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    RecipeBookSetting setting = i < p.Books.Count ? p.Books[i] : new RecipeBookSetting(false, false);
                    w.WriteBool(setting.Open);
                    w.WriteBool(setting.Filtering);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                var books = new RecipeBookSetting[4];
                for (int i = 0; i < 4; i++)
                    books[i] = new RecipeBookSetting(r.ReadBool(), r.ReadBool());

                return new ClientboundRecipeBookSettingsPacket(books);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRecipeBookSettings(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Clientbound.RecipeBookSettings)
            .From(JavaEras.WideIds, RecipeCodecs.RecipeBookSettingsModern);
    }
}
