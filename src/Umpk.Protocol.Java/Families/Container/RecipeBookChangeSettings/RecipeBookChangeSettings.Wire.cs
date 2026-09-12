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
    /// <summary>recipe-book-change-settings: enum-byte book type, bool open, bool filtering.</summary>
    public static PacketCodec<ServerboundRecipeBookChangeSettingsPacket> RecipeBookChangeSettingsModern { get; } =
        PacketCodec<ServerboundRecipeBookChangeSettingsPacket>.Of(
            static (ref PacketWriter w, ServerboundRecipeBookChangeSettingsPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.BookType);
                w.WriteBool(p.Open);
                w.WriteBool(p.Filtering);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundRecipeBookChangeSettingsPacket(r.ReadVarInt(), r.ReadBool(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRecipeBookChangeSettings(PacketBindings bindings)
    {
        bindings.Packet(ItemPackets.Serverbound.RecipeBookChangeSettings)
            .From(JavaProtocols.V1_16_2, RecipeCodecs.RecipeBookChangeSettingsModern);
    }
}
