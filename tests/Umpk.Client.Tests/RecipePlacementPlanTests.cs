using Umpk.Client;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Inventory;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="RecipePlacementSupport"/> and <see cref="RecipeCraftPlan"/>: the recipe-book placement form is a property of the version, not of the shape of what the caller typed. Name-based and numeric forms are valid on different protocol bands.</summary>
public sealed class RecipePlacementPlanTests
{
    // RecipePlacementSupport.Resolve: exactly one live form per version.

    [Theory]
    [InlineData("1.12.2", RecipePlacementForm.None)]
    [InlineData("1.13", RecipePlacementForm.ResourceName)]
    [InlineData("1.16.5", RecipePlacementForm.ResourceName)]
    [InlineData("1.20.1", RecipePlacementForm.ResourceName)]
    [InlineData("1.21.2", RecipePlacementForm.NetworkId)]
    [InlineData("26.2", RecipePlacementForm.NetworkId)]
    public void Resolve_HasExactlyOneLiveFormPerVersion(string versionName, RecipePlacementForm expected)
    {
        JavaVersion version = Version(versionName);
        ClientActionCapabilities caps = CapabilitiesFor(version);

        RecipePlacementSupport support = RecipePlacementSupport.Resolve(caps, session: null);

        Assert.Equal(expected, support.Form);

        // Never both forms live for one version: a caller that picks by argument shape is picking at random.
        Assert.False(caps.CanPlaceRecipe && caps.CanPlaceRecipeByName);
    }

    // The trap the whole gate exists for: the identifier is present where the outbound table disagrees.

    [Theory]
    [InlineData("1.12.2")] // present as a MARKER: 1.12.2 sends a numeric crafting-manager id neither record carries.
    [InlineData("26.2")] // present and genuinely live, but as the network-id form only.
    public void PlaceRecipeIdentifier_CannotSayWhichFormIsLive(string versionName)
    {
        JavaVersion version = Version(versionName);
        Assert.True(HasServerboundPlayIdentifier(version, Identifier.Minecraft("place_recipe")));
    }

    // RecipeCraftPlan.Plan: the form is picked BY VERSION, never by argument shape.

    [Theory]
    // The by-name era: a resource location goes, a number is refused rather than sent as a network id.
    [InlineData(RecipePlacementForm.ResourceName, "minecraft:torch", RecipePlacementForm.ResourceName, RecipeCraftRefusal.None)]
    [InlineData(RecipePlacementForm.ResourceName, "42", RecipePlacementForm.None, RecipeCraftRefusal.NeedsResourceName)]
    // The network-id era: a number goes, a name is refused rather than sent as a resource location.
    [InlineData(RecipePlacementForm.NetworkId, "42", RecipePlacementForm.NetworkId, RecipeCraftRefusal.None)]
    [InlineData(RecipePlacementForm.NetworkId, "minecraft:torch", RecipePlacementForm.None, RecipeCraftRefusal.NeedsNetworkId)]
    public void Plan_PicksTheFormByVersion_NotByArgumentShape(
        RecipePlacementForm live, string typed, RecipePlacementForm expectedForm, RecipeCraftRefusal expectedRefusal)
    {
        var placement = new RecipePlacementSupport(live, "1.20.1", 763);

        RecipeCraftPlan plan = RecipeCraftPlan.Plan(placement, typed);

        Assert.Equal(expectedForm, plan.Form);
        Assert.Equal(expectedRefusal, plan.Refusal);
        Assert.Equal(expectedRefusal == RecipeCraftRefusal.None, plan.CanPlace);
    }

    [Fact]
    public void Plan_Refuses_NonIdentifier_OnTheResourceNameWireLayout()
    {
        var placement = new RecipePlacementSupport(RecipePlacementForm.ResourceName, "1.20.1", 763);

        RecipeCraftPlan plan = RecipeCraftPlan.Plan(placement, "not a valid id!!");

        Assert.False(plan.CanPlace);
        Assert.Equal(RecipeCraftRefusal.NotAResourceLocation, plan.Refusal);
    }

    [Fact]
    public void Plan_Refuses_OnAVersionWithNoSendableForm()
    {
        var placement = new RecipePlacementSupport(RecipePlacementForm.None, "1.12.2", 340);

        RecipeCraftPlan plan = RecipeCraftPlan.Plan(placement, "minecraft:torch");

        Assert.False(plan.CanPlace);
        Assert.Equal(RecipeCraftRefusal.VersionCannotPlace, plan.Refusal);
    }

    // RecipePlacementSupport.Describe

    [Fact]
    public void Describe_NamesTheProtocol_WhenNoVersionNameIsNegotiatedYet()
    {
        var support = new RecipePlacementSupport(RecipePlacementForm.None, string.Empty, 340);

        // Never an empty subject: a refusal always identifies what refused.
        Assert.Equal("protocol 340", support.Describe());
    }

    [Fact]
    public void Describe_NamesTheVersion_WhenKnown()
    {
        var support = new RecipePlacementSupport(RecipePlacementForm.ResourceName, "1.20.1", 763);

        Assert.Equal("Minecraft 1.20.1", support.Describe());
    }

    // Fixtures

    private static ClientActionCapabilities CapabilitiesFor(JavaVersion version) =>
        new(new WireIndex(version), version.Version.Protocol, static () => ProtocolPhase.Play);

    private static JavaVersion Version(string name)
    {
        Assert.True(JavaVersions.TryGetByName(name, out JavaVersion? version), $"Unknown version '{name}'.");
        return version!;
    }

    /// <summary>The NAIVE check <see cref="ClientActionCapabilities"/> exists to replace: whether the version's serverbound play registry holds the packet's IDENTIFIER at all, regardless of what is bound under it. True for a marker and true for a band that binds a different record, so production code must never gate on it; kept here only as the "before" half of the comparison the tests above make.</summary>
    private static bool HasServerboundPlayIdentifier(JavaVersion version, Identifier id)
    {
        if (!version.Protocol.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry registry))
            return false;

        foreach ((int _, PacketType type) in registry.Packets)
            if (type.Id == id)
                return true;

        return false;
    }
}
