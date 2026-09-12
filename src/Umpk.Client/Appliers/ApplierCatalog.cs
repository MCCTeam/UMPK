using Umpk.Client.Internal;

namespace Umpk.Client.Appliers;

/// <summary>Builds the ordered applier chain for a session based on the enabled features. Feature-gated appliers are omitted when their feature is off, so a disabled module registers no packet handling.</summary>
internal static class ApplierCatalog
{
    public static IReadOnlyList<IApplier> Build(ClientFeatures features)
    {
        var appliers = new List<IApplier>
        {
            new ConnectionApplier(),
            new ChatApplier(),
            new SelfApplier(),
            new UiApplier(),
            new CommandTreeApplier(),
            new BlockAckApplier(),
            new RecipeApplier(),
        };

        if (features.Terrain)
            appliers.Add(new WorldApplier());

        if (features.Entities)
            appliers.Add(new EntityApplier());

        if (features.Inventory)
            appliers.Add(new InventoryApplier());

        return appliers;
    }
}
