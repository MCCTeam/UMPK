using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java;

/// <summary>Item and container family timelines: windows, slots, stacks, creative/click sends, trades, recipes, and block/entity item interactions. The item stack form drives most of the era splits here: the short-id 1.8 slot, the bool-present + VarInt-id 1.13.2 flip, the 1.14 varintId stack, the 1.20.5 structured components, and the 1.21.5 hashed click stack, with the 776 component id table on top. Container ids are bytes through 1.21.1 and VarInts from 1.21.2.</summary>
internal static class ItemBindings
{
    /// <summary>Adds this family's packet timelines to the binding table.</summary>
    public static void Register(PacketBindings bindings)
    {
        ContainerCodecs.DeclareContainerButtonClick(bindings);
        ContainerCodecs.DeclareContainerClick(bindings);
        ContainerCodecs.DeclareContainerClose(bindings);
        ContainerCodecs.DeclareContainerSetContent(bindings);
        ContainerCodecs.DeclareContainerSetData(bindings);
        ContainerCodecs.DeclareContainerSetSlot(bindings);
        ContainerCodecs.DeclareContainerSlotStateChanged(bindings);
        ContainerCodecs.DeclareCreativeInventoryAction(bindings);
        ContainerCodecs.DeclareOpenScreen(bindings);
        ContainerCodecs.DeclareRenameItem(bindings);
        ContainerCodecs.DeclareSetCreativeModeSlot(bindings);
        ContainerCodecs.DeclareSetCursorItem(bindings);
        ContainerCodecs.DeclareSetPlayerInventory(bindings);
        ContainerCodecs.DeclareSetSlot(bindings);
        ContainerCodecs.DeclareTransaction(bindings);
        MerchantCodecs.DeclareMerchantOffers(bindings);
        MerchantCodecs.DeclareSelectTrade(bindings);
        RecipeCodecs.DeclarePlaceGhostRecipe(bindings);
        RecipeCodecs.DeclarePlaceRecipe(bindings);
        RecipeCodecs.DeclareRecipe(bindings);
        RecipeCodecs.DeclareRecipeBookAdd(bindings);
        RecipeCodecs.DeclareRecipeBookChangeSettings(bindings);
        RecipeCodecs.DeclareRecipeBookRemove(bindings);
        RecipeCodecs.DeclareRecipeBookSeenRecipe(bindings);
        RecipeCodecs.DeclareRecipeBookSettings(bindings);
        RecipeCodecs.DeclareUpdateRecipes(bindings);
        UseItemCodecs.DeclareBlockPlace(bindings);
        UseItemCodecs.DeclarePickItemFromBlock(bindings);
        UseItemCodecs.DeclarePickItemFromEntity(bindings);
        UseItemCodecs.DeclareUseItem(bindings);
        UseItemCodecs.DeclareUseItemOn(bindings);
    }
}
