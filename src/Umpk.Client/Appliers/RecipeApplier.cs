using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Applies the recipe surface into <see cref="RecipeState"/>: the recipe registry (update-recipes) and the recipe-book unlock traffic of both eras. 1.12-1.21.1 sends one <c>minecraft:recipe</c> packet carrying an init/add/remove state, the book flags and the affected recipes; 1.21.2+ splits it into recipe_book_add / recipe_book_remove / recipe_book_settings. Both write the same state, so a consumer enumerating unlocked recipes never has to branch on the version. Not feature-gated: the recipe state is always present on <see cref="ClientState"/>.</summary>
internal sealed class RecipeApplier : IApplier
{
    public async ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        RecipeState recipes = context.State.Recipes;
        switch (packet)
        {
            case ClientboundUpdateRecipesPacket update:
                // The registry payload is opaque (nested recipe-property trees); retain it verbatim.
                recipes.Update(update.Payload);
                await context.PublishAsync(new RecipesUpdated(recipes.Revision)).ConfigureAwait(false);
                return true;

            case ClientboundRecipePacket unlock:
                recipes.ApplyLegacyUnlock(unlock);
                await PublishBookAsync(recipes, context).ConfigureAwait(false);
                return true;

            case ClientboundRecipeBookAddPacket add:
                recipes.ApplyBookAdd(add.Entries, add.UndecodedEntries, add.Replace);
                await PublishBookAsync(recipes, context).ConfigureAwait(false);
                return true;

            case ClientboundRecipeBookRemovePacket remove:
                recipes.ApplyRemove(remove.RecipeIds);
                await PublishBookAsync(recipes, context).ConfigureAwait(false);
                return true;

            case ClientboundRecipeBookSettingsPacket settings:
                recipes.ApplySettings(settings.Books);
                await PublishBookAsync(recipes, context).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    private static ValueTask PublishBookAsync(RecipeState recipes, ApplierContext context) =>
        context.PublishAsync(
            new RecipeBookChanged(recipes.UnlockedRecipes.Count + recipes.UnlockedRecipeIds.Count, recipes.BookRevision));
}
