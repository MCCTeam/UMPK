using Umpk.Protocol.Java.Codecs;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java;

/// <summary>Immutable per-version protocol feature flags, generated onto each descriptor from the dataset. These are the flags that would otherwise become scattered version comparisons. Codecs that need them receive them at construction time (era selection), never by reading a version off the codec context. Unknown string flags are carried as strings; the derived enum/layout properties translate them once for codec construction.</summary>
public sealed record ProtocolFeatures
{
    /// <summary>Whether the configuration phase exists (1.20.2+).</summary>
    public bool ConfigurationPhase { get; init; }

    /// <summary>Chat-signing generation: <c>none</c>, <c>v1</c>, <c>v2</c>, <c>v3</c>.</summary>
    public string ChatSigning { get; init; } = "none";

    /// <summary>NBT root framing: <c>javaNamedRoot</c>, <c>javaUnnamedRoot</c>, <c>javaRootTagOrString</c>.</summary>
    public string NbtWireFormat { get; init; } = "javaNamedRoot";

    /// <summary>Block-position packing: <c>legacy</c> or <c>modern</c>. Carried as the dataset's own string because <see cref="PosLayout"/>, which consumers read, is derived from it.</summary>
    public string BlockPosLayout { get; init; } = "modern";

    /// <summary>Fluid-movement model: <c>legacy</c> or <c>swimmingUpdate</c>.</summary>
    public string FluidMovement { get; init; } = "swimmingUpdate";

    /// <summary>Water-travel model: <c>legacy</c> (47-340) or <c>sprintAware</c> (393+). Distinct from <see cref="FluidMovement"/>, which gates only the depth-strider blend: this one gates the sprinting arm on the horizontal water slow-down and the vertical sink model.</summary>
    public string WaterTravel { get; init; } = "sprintAware";

    /// <summary>Whether vanilla's water arm carries the in-water climbable bump (the <c>horizontalCollision &amp;&amp; onClimbable -&gt; y = 0.2</c> clause). False for 47-404, true from 477 (1.14). Distinct from <see cref="WaterTravel"/>, whose boundary is 340 -&gt; 393, and from <see cref="CrawlPose"/>, which merely shares the 477 boundary.</summary>
    public bool WaterClimbBump { get; init; }

    /// <summary>Whether elytra flight exists.</summary>
    public bool Elytra { get; init; }

    /// <summary>Whether the swim pose exists.</summary>
    public bool SwimPose { get; init; }

    /// <summary>Whether the crawl pose exists.</summary>
    public bool CrawlPose { get; init; }

    /// <summary>The <see cref="Codecs.BlockPosLayout"/> derived from <see cref="BlockPosLayout"/>.</summary>
    public BlockPosLayout PosLayout =>
        string.Equals(BlockPosLayout, "legacy", StringComparison.Ordinal)
            ? Codecs.BlockPosLayout.PrePacked114
            : Codecs.BlockPosLayout.Packed114;

    /// <summary>
    /// The component click/hover interaction era: <c>modern</c> (the 1.21.5+ dispatched forms) or <c>legacy</c> (the flat string-value forms). Distinct from <see cref="NbtWireFormat"/> because the two boundaries are in different places: components move from JSON strings to network NBT at 1.20.3 (765), but the interaction shapes only change at 1.21.5 (770), so protocols <b>764-769</b> already use non-named-root network NBT while still carrying the legacy interaction shapes.
    /// <para>The legacy form serializes <c>clickEvent</c> / <c>hoverEvent</c> string values; the modern form dispatches on <c>action</c> and serializes <c>click_event</c> / <c>hover_event</c>.</para>
    /// </summary>
    public string ComponentInteractionEra { get; init; } = "modern";

    /// <summary>
    /// The component wire era derived from <see cref="NbtWireFormat"/> and <see cref="ComponentInteractionEra"/>.
    /// <para>This is the single source of truth for where the interaction era changes. The codecs cannot read it directly - they are shared statics that bake their era in at construction, per the era-at- construction rule - so the binding tables restate the same boundary in code. That duplication is held honest by <c>ComponentEraBindingAgreementTests</c>, which resolves the codec the registrar actually binds at every protocol, round-trips a component carrying a click and a hover event through it, and asserts the bytes match the era this property declares. A disagreement between the dataset and binding tables therefore fails the test instead of silently dropping interactions.</para>
    /// </summary>
    public ComponentWireEra ComponentEra =>
        string.Equals(NbtWireFormat, "javaNamedRoot", StringComparison.Ordinal)
            || string.Equals(ComponentInteractionEra, "legacy", StringComparison.Ordinal)
            ? ComponentWireEra.Legacy
            : ComponentWireEra.Modern;

    /// <summary>The command-tree argument-type table era, one value per dataset version (<c>none</c>, <c>v1_13</c> .. <c>v26_3</c>). Numeric parser ids start at <c>v1_19</c>; the earlier eras name parsers with a resource-location string and consult no numeric table.</summary>
    public string ArgumentTypeEra { get; init; } = "v1_21_5";

    /// <summary>The <see cref="Packets.ArgumentTypeRegistry"/> derived from <see cref="ArgumentTypeEra"/>. This lets the command-tree applier select the parser table off the bound descriptor instead of comparing a protocol number, mirroring how the item codecs pick their component table at bind time.</summary>
    /// <remarks>Every era in which a numeric table exists is listed explicitly, and each arm must name the same registry the packet's timeline binds for that protocol (<c>CommandsBindings</c>); a test pins the two against each other, because a feature flag and a binding table that disagree is exactly how a wrong parser table survives. Eras before <c>v1_19</c> (and <c>none</c>) have no numeric registry at all: the string-keyed codec resolves parser names straight off the wire, so the fallback value here is never consulted for them.</remarks>
    public Packets.ArgumentTypeRegistry ArgumentTypes => ArgumentTypeEra switch
    {
        "v1_19" or "v1_19_1" => Packets.ArgumentTypeRegistry.V759,
        "v1_19_3" => Packets.ArgumentTypeRegistry.V761,
        "v1_19_4" or "v1_20" or "v1_20_2" => Packets.ArgumentTypeRegistry.V764,
        "v1_20_3" => Packets.ArgumentTypeRegistry.V765,
        "v1_20_5" or "v1_21" or "v1_21_2" or "v1_21_4" => Packets.ArgumentTypeRegistry.V766,
        "v1_21_6" or "v1_21_7" or "v1_21_9" or "v1_21_11" or "v26_1" => Packets.ArgumentTypeRegistry.V1_21_6,
        "v26_2" => Packets.ArgumentTypeRegistry.V26_2,
        "v26_3" => Packets.ArgumentTypeRegistry.V26_3,
        _ => Packets.ArgumentTypeRegistry.V1_21_5,
    };
}
