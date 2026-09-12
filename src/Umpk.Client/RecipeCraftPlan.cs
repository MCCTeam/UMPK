using Umpk.Game.Inventory;

namespace Umpk.Client;

/// <summary>Why a <see cref="RecipeCraftPlan"/> refuses to place a recipe, or that it does not refuse.</summary>
public enum RecipeCraftRefusal
{
    /// <summary>No refusal: the plan will send.</summary>
    None = 0,

    /// <summary>The live form is <see cref="RecipePlacementForm.NetworkId"/> but the typed recipe was not a number.</summary>
    NeedsNetworkId = 1,

    /// <summary>The live form is <see cref="RecipePlacementForm.ResourceName"/> but the typed recipe was a number.</summary>
    NeedsResourceName = 2,

    /// <summary>The live form is <see cref="RecipePlacementForm.ResourceName"/> and the typed recipe is not a valid resource location.</summary>
    NotAResourceLocation = 3,

    /// <summary>The version has no sendable place-recipe form at all (<see cref="RecipePlacementForm.None"/>).</summary>
    VersionCannotPlace = 4,
}

/// <summary>
/// What a craft request will do, decided BEFORE anything is sent.
/// <para>The wire form is a property of the VERSION, not of the shape of what the user typed: both forms travel under the one <c>minecraft:place_recipe</c> identifier and each version binds exactly one record, so only the outbound table (via <see cref="RecipePlacementSupport"/>) can say which is live. Choosing by argument shape sends a by-name request on a network-id-only server and a numeric one on a by-name-only server, where the matching send is not bound at all.</para>
/// </summary>
/// <param name="Form">The form that will be used, or <see cref="RecipePlacementForm.None"/> when refused.</param>
/// <param name="NetworkId">The network recipe-display id to send when <paramref name="Form"/> is <see cref="RecipePlacementForm.NetworkId"/>.</param>
/// <param name="RecipeId">The parsed resource-location recipe id to send when <paramref name="Form"/> is <see cref="RecipePlacementForm.ResourceName"/>; null otherwise.</param>
/// <param name="Refusal">Why nothing will be sent, or <see cref="RecipeCraftRefusal.None"/> when it will be.</param>
public sealed record RecipeCraftPlan(RecipePlacementForm Form, int NetworkId, Identifier? RecipeId, RecipeCraftRefusal Refusal)
{
    /// <summary>Whether this plan will actually send a request.</summary>
    public bool CanPlace => Refusal == RecipeCraftRefusal.None;

    /// <summary>Decides what a craft request will do, given the version's live placement form and the recipe as typed.</summary>
    /// <param name="placement">The live form, read from <see cref="RecipePlacementSupport.Resolve"/>.</param>
    /// <param name="recipe">The recipe as typed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="placement"/> or <paramref name="recipe"/> is null.</exception>
    public static RecipeCraftPlan Plan(RecipePlacementSupport placement, string recipe)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(recipe);

        string wanted = recipe.Trim();
        bool typedNumber = int.TryParse(wanted, out int networkId);

        switch (placement.Form)
        {
            case RecipePlacementForm.NetworkId when typedNumber:
                return new RecipeCraftPlan(RecipePlacementForm.NetworkId, networkId, RecipeId: null, RecipeCraftRefusal.None);

            case RecipePlacementForm.NetworkId:
                return Refuse(RecipeCraftRefusal.NeedsNetworkId);

            case RecipePlacementForm.ResourceName when typedNumber:
                return Refuse(RecipeCraftRefusal.NeedsResourceName);

            case RecipePlacementForm.ResourceName:
                return Identifier.TryParse(wanted, out Identifier id)
                    ? new RecipeCraftPlan(RecipePlacementForm.ResourceName, NetworkId: 0, id, RecipeCraftRefusal.None)
                    : Refuse(RecipeCraftRefusal.NotAResourceLocation);

            default:
                return Refuse(RecipeCraftRefusal.VersionCannotPlace);
        }

        static RecipeCraftPlan Refuse(RecipeCraftRefusal refusal) =>
            new(RecipePlacementForm.None, NetworkId: 0, RecipeId: null, refusal);
    }
}
