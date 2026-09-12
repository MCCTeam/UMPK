using Umpk.Game.Inventory;

namespace Umpk.Client;

/// <summary>
/// The live recipe-placement form and the version it was read from.
/// <para>There is exactly ONE live form per version and the wire decides which, so a caller must branch on this rather than on the shape of whatever the user typed. Resolved from <see cref="ClientActionCapabilities"/>, which is the outbound-table answer for the exact record each send builds: a bare identifier lookup cannot answer it at all, because both forms travel under the one <c>minecraft:place_recipe</c> identifier and the identifier is equally present in both eras.</para>
/// </summary>
/// <param name="Form">The one form this version can actually send.</param>
/// <param name="VersionName">The negotiated Minecraft version name, or empty before a session has negotiated one.</param>
/// <param name="Protocol">The protocol number the answer was resolved for.</param>
public sealed record RecipePlacementSupport(RecipePlacementForm Form, string VersionName, int Protocol)
{
    /// <summary>The version as a message should name it: the Minecraft version name when known, else the protocol number. Never empty, so a refusal always identifies what refused.</summary>
    public string Describe() => string.IsNullOrEmpty(VersionName) ? $"protocol {Protocol}" : $"Minecraft {VersionName}";

    /// <summary>Resolves the live recipe-placement form for a session from its action capabilities.</summary>
    /// <param name="caps">The session's resolved action capabilities.</param>
    /// <param name="session">The session info, or null before a version has negotiated.</param>
    /// <exception cref="ArgumentNullException"><paramref name="caps"/> is null.</exception>
    public static RecipePlacementSupport Resolve(ClientActionCapabilities caps, SessionInfo? session)
    {
        ArgumentNullException.ThrowIfNull(caps);
        RecipePlacementForm form = caps.CanPlaceRecipe
            ? RecipePlacementForm.NetworkId
            : caps.CanPlaceRecipeByName
                ? RecipePlacementForm.ResourceName
                : RecipePlacementForm.None;
        return new RecipePlacementSupport(form, session?.Version.Version.Name ?? string.Empty, caps.Protocol);
    }
}
