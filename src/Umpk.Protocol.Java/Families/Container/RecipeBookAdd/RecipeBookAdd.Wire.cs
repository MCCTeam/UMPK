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
    /// <summary>recipe-book-add (1.21.2+): the whole recipe-display tree, read and written.</summary>
    /// <param name="era">The era's slot-display rules and item-stack pair.</param>
    /// <remarks>
    /// The payload is a typed tree; see <see cref="RecipeDisplayCodecs"/> for its shape and for why a decode failure degrades to an opaque entry count rather than throwing.
    /// <para>Write is round-trip faithful: a recorded frame re-encodes byte for byte. Both legs read the same era table, so a frame cannot be decoded under one slot registry and re-encoded under another.</para>
    /// </remarks>
    public static PacketCodec<ClientboundRecipeBookAddPacket> RecipeBookAdd(RecipeBookWire era) =>
        PacketCodec<ClientboundRecipeBookAddPacket>.Of(
            (ref PacketWriter w, ClientboundRecipeBookAddPacket p, PacketCodecContext context) =>
                RecipeDisplayCodecs.WriteAdd(ref w, p, context, era.Slots, era.Stacks.Write),
            (ref PacketReader r, PacketCodecContext context) =>
                RecipeDisplayCodecs.ReadAdd(ref r, context, era.Slots, era.Stacks.Read),
            WireShape.OfEra("recipe_book_add", era));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareRecipeBookAdd(PacketBindings bindings)
    {
        // One binding per COMPONENT era, because an item_stack slot display inside the tree is read with that era's component table. The slot_display table axis moves independently of that and TWICE: At 1.21.5 the smithing_trim pattern changes from a nested slot display to a registry holder, and 26.1 inserted three variants so every type id from 2 upward shifted. Hence three slot tables, not two.
        bindings.Packet(ItemPackets.Clientbound.RecipeBookAdd)
            .From(JavaProtocols.V1_21_2, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V1_21_2(Table768)))
            .From(JavaProtocols.V1_21_4, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V1_21_2(Table769)))
            .From(JavaProtocols.V1_21_5, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V1_21_5(Table770)))
            .From(JavaProtocols.V1_21_6, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V1_21_5(Table771)))
            .From(JavaProtocols.V1_21_9, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V1_21_5(Table773)))
            .From(JavaProtocols.V1_21_11, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V1_21_5(Table774)))
            // At 26.1 an item_stack slot display carries a template rather than a full stack. The template writes holder id before count, while the stack writes count first. The two forms mirror each other, so a frame read with the wrong
            // one re-encodes byte-identically while reporting the id as the count and the count as the id;
            // only a field assertion separates them, never a round trip.
            .From(JavaProtocols.V26_1, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V26_1(Table775)))
            .From(JavaProtocols.V26_2, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V26_1(Table776)))
            .From(JavaProtocols.V26_3, RecipeCodecs.RecipeBookAdd(RecipeBookWire.V26_3(Table777)));
    }
}

/// <summary>How one era frames a recipe-book-add frame. The axes inside it move independently: <c>slot_display</c> changed at 1.21.5 (<c>smithing_trim</c>'s pattern became a <c>Holder&lt;TrimPattern&gt;</c>), again at 26.1, which inserted three variants so every type id from 2 up shifted, and again at 26.3, where the <c>tag</c> payload became an item holder set; the nested item stack moved to <c>ItemStackTemplate</c> at 26.1 and carries a per-protocol component table besides. Era facts on one factory is what a named shape is for.</summary>
/// <param name="Slots">The era's <c>slot_display</c> wire rules.</param>
/// <param name="Stacks">The era's item-stack reader and writer.</param>
internal readonly record struct RecipeBookWire(RecipeDisplayCodecs.SlotDisplayTable Slots, StackWire Stacks)
{
    /// <summary>768/769 (1.21.2-1.21.4): nested smithing-trim pattern, count-first component stacks.</summary>
    /// <param name="table">The era's component table.</param>
    /// <returns>The shape.</returns>
    internal static RecipeBookWire V1_21_2(ItemComponentTable table) =>
        new(RecipeDisplayCodecs.SlotsV1_21_2, StackWire.Components(table));

    /// <summary>770-774 (1.21.5-1.21.11): holder-form smithing-trim pattern, count-first stacks.</summary>
    /// <param name="table">The era's component table.</param>
    /// <returns>The shape.</returns>
    internal static RecipeBookWire V1_21_5(ItemComponentTable table) =>
        new(RecipeDisplayCodecs.SlotsV1_21_5, StackWire.Components(table));

    /// <summary>775+ (26.1): the re-numbered slot-display table and template stacks.</summary>
    /// <param name="table">The era's component table.</param>
    /// <returns>The shape.</returns>
    internal static RecipeBookWire V26_1(ItemComponentTable table) =>
        new(RecipeDisplayCodecs.SlotsV26_1, StackWire.Templates(table));

    /// <summary>777 (26.3): the holder-set tag payload, template stacks under the era's component table.</summary>
    /// <param name="table">The era's component table.</param>
    /// <returns>The shape.</returns>
    internal static RecipeBookWire V26_3(ItemComponentTable table) =>
        new(RecipeDisplayCodecs.SlotsV26_3, StackWire.Templates(table));

    /// <inheritdoc />
    public override string ToString() => $"slots={Slots.Form},stacks={Stacks}";
}
