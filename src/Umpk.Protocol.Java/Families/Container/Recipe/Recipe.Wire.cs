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
    //
    // Wire history:
    //   1.12-1.12.2  (335-340): state, 2 bools, varint-id recipe list, (init) varint-id highlight list.
    //                           The list contains VarInt recipe identifiers.
    //   1.13-1.16.1  (393-736): state, 4 bools (crafting open/filter, furnace open/filter), identifier
    //                           lists.
    //   1.16.2-1.21.1 (751-767): state, RecipeBookSettings = 8 bools (CRAFTING, FURNACE, BLAST_FURNACE,
    //                           SMOKER, each open then filtering), identifier lists.
    // The book count is fixed per era form, so a frame re-encodes byte for byte whatever the caller put in Books: a short list pads with (false, false) and a long one is truncated, exactly like the 1.21.2+ recipe_book_settings codec above.

    /// <summary>recipe (1.12-1.12.2): state, crafting open/filter, numeric recipe ids, init-only highlight ids.</summary>
    public static PacketCodec<ClientboundRecipePacket> RecipeV1_12 { get; } = MakeRecipe(books: 1, identifiers: false);

    /// <summary>recipe (1.13-1.16.1): state, crafting + furnace open/filter, identifier lists.</summary>
    public static PacketCodec<ClientboundRecipePacket> RecipeV1_13 { get; } = MakeRecipe(books: 2, identifiers: true);

    /// <summary>recipe (1.16.2-1.21.1): state, all four book open/filter pairs, identifier lists.</summary>
    public static PacketCodec<ClientboundRecipePacket> RecipeV1_16_2 { get; } = MakeRecipe(books: 4, identifiers: true);

    private static PacketCodec<ClientboundRecipePacket> MakeRecipe(int books, bool identifiers) =>
        PacketCodec<ClientboundRecipePacket>.Of(
            (ref PacketWriter w, ClientboundRecipePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.State);
                WriteBooks(ref w, p.Books, books);
                if (identifiers)
                {
                    WriteIdentifiers(ref w, p.Recipes);
                    if (p.State == RecipeBookState.Init)
                        WriteIdentifiers(ref w, p.ToHighlight);

                }
                else
                {
                    w.WriteList(p.LegacyRecipeIds, static (ref PacketWriter iw, int id) => iw.WriteVarInt(id));
                    if (p.State == RecipeBookState.Init)
                        w.WriteList(p.LegacyToHighlightIds, static (ref PacketWriter iw, int id) => iw.WriteVarInt(id));

                }
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                int state = r.ReadVarInt();
                RecipeBookSetting[] settings = ReadBooks(ref r, books);
                if (!identifiers)
                {
                    int[] ids = r.ReadList(static (ref PacketReader ir) => ir.ReadVarInt());
                    int[] highlightIds = state == RecipeBookState.Init
                        ? r.ReadList(static (ref PacketReader ir) => ir.ReadVarInt())
                        : [];
                    return new ClientboundRecipePacket(state, settings, [], [], ids, highlightIds);
                }

                Identifier[] recipes = ReadIdentifiers(ref r);
                Identifier[] highlight = state == RecipeBookState.Init ? ReadIdentifiers(ref r) : [];
                return new ClientboundRecipePacket(state, settings, recipes, highlight, [], []);
            });

    private static void WriteBooks(ref PacketWriter w, IReadOnlyList<RecipeBookSetting> books, int count)
    {
        for (int i = 0; i < count; i++)
        {
            RecipeBookSetting setting = i < books.Count ? books[i] : new RecipeBookSetting(false, false);
            w.WriteBool(setting.Open);
            w.WriteBool(setting.Filtering);
        }
    }

    private static RecipeBookSetting[] ReadBooks(ref PacketReader r, int count)
    {
        var settings = new RecipeBookSetting[count];
        for (int i = 0; i < count; i++)
            settings[i] = new RecipeBookSetting(r.ReadBool(), r.ReadBool());

        return settings;
    }

    private static void WriteIdentifiers(ref PacketWriter w, IReadOnlyList<Identifier> values) =>
        w.WriteList(values, static (ref PacketWriter iw, Identifier id) => iw.WriteString(id.ToString()));

    private static Identifier[] ReadIdentifiers(ref PacketReader r) =>
        r.ReadList(static (ref PacketReader ir) => Identifier.Parse(ir.ReadString()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRecipe(PacketBindings bindings)
    {
        // The recipe-book unlock packet exists only on 1.12-1.21.1; the 1.21.2 split replaces it with recipe_book_add/remove/settings. It has three era forms: 1.12 sends numeric crafting-manager ids behind two book bools, 1.13 flips the recipes to resource locations and adds the furnace book pair, and 1.16.2 replaces the loose bools with the four-book RecipeBookSettings block (the same release that split recipe_book_update into change_settings + seen_recipe below).
        bindings.Packet(ItemPackets.Clientbound.Recipe)
            .From(JavaProtocols.V1_12, RecipeCodecs.RecipeV1_12)
            .From(JavaProtocols.V1_13, RecipeCodecs.RecipeV1_13)
            .From(JavaProtocols.V1_16_2, RecipeCodecs.RecipeV1_16_2);
    }
}
