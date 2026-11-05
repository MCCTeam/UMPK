using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Registries;

namespace Umpk.Client.Actions;

/// <summary>The source-bound default bone-meal result on canonical wheat; growth and consumption remain server-owned.</summary>
internal readonly record struct VanillaBoneMealUse(bool HasWheatRules)
{
    private static readonly string[] Ages = ["0", "1", "2", "3", "4", "5", "6", "7"];

    internal static VanillaBoneMealUse ForProtocol(int protocol) => new(protocol == 766);

    internal bool TryPredict(ItemStack item, BlockState clicked, bool adventure, bool localSwing, out BlockUsePrediction prediction)
    {
        prediction = BlockUsePrediction.Unknown;
        if (!HasWheatRules || item.IsEmpty || !item.Components.HasNoPatch || item.Item.IsDefault || item.Item.Id != Identifier.Minecraft("bone_meal")
            || !JavaGameData.Registries(766).Items.TryGet(item.Item.NetworkId, out RegistryEntry<ItemDefinition> canonicalItem)
            || canonicalItem.Id != item.Item.Id || !ReferenceEquals(canonicalItem.Value, item.Item.Value)
            || item.Components.Get(DataComponents.CanPlaceOn) is not null
            || !clicked.IsValid || clicked.Block.Id != Identifier.Minecraft("wheat")
            || !JavaGameData.Registries(766).Blocks.TryGet(clicked.Block.NetworkId, out RegistryEntry<BlockDefinition> canonicalBlock)
            || canonicalBlock.Id != clicked.Block.Id || !ReferenceEquals(canonicalBlock.Value, clicked.Block.Value))
            return false;

        BlockDefinition wheat = canonicalBlock.Value;
        if (wheat.StateCount != 8 || wheat.Properties.Count != 1 || wheat.Properties[0].Name != "age" || !wheat.Properties[0].Values.SequenceEqual(Ages)
            || !wheat.TryGetPropertyValue(clicked.StateId, "age", out string canonicalAge)
            || !clicked.TryGetProperty("age", out string age) || age != canonicalAge)
            return false;

        // The native default stack has no CAN_PLACE_ON, so Adventure returns PASS before BoneMealItem.useOn. Custom component patches remain outside this profile.
        if (adventure)
            prediction = BlockUsePrediction.Pass;
        else
            // Native CropBlock checks only its age. At age7 its empty support shape rejects every face before the water-plant branch; no neighbor or growth outcome is inferred.
            prediction = age == "7" ? BlockUsePrediction.Pass : BlockUsePrediction.Success(localSwing);
        return true;
    }
}
