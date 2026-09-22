using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>
/// Everything one component era decides about its own table, in one named value per era. Keeping these choices together makes a protocol's complete component layout directly observable.
/// <para>The fields are deliberately NOT collapsed into a single era number. Six of them move at six different releases, and the pair that most invites collapsing must never be collapsed: the id ORDERING (766, 767, 768, 770, 774, 775, 776, 777) and the component PAYLOAD shapes (769, 771, 773, 775, 776, 777) are different axes, and 1.21.6 is the standing proof, since it keeps 1.21.5's ordering byte-for-byte while changing <c>attribute_modifiers</c>. The interaction dialect is a third axis again, moving at 770 alone.</para>
/// <para>Every instance states every field, including the ones its own builder does not consult, because the value is a fact about the era rather than a switch for a code path: 770 really does carry the four-list <c>custom_model_data</c> and the 1.21.2 payload family, and saying so costs nothing while a <c>false</c> there would be a lie the next reader has to disprove.</para>
/// </summary>
/// <param name="Ordering">The era's component id ordering.</param>
/// <param name="Dialect">The component interaction dialect: legacy <c>clickEvent</c>/<c>hoverEvent</c> through 769, modern <c>click_event</c>/<c>hover_event</c> from 770.</param>
/// <param name="NestedStacks">The era's nested item-stack wire form: count-first through 774, then template stacks from 775.</param>
/// <param name="WideIdPayloads">768+: <c>item_model</c>, <c>tooltip_style</c> and <c>glider</c> exist, <c>food</c> is the direct nutrition/saturation/canAlwaysEat record, and <c>instrument</c>'s direct holder carries a float second count plus a description component.</param>
/// <param name="ListCustomModelData">769+: <c>custom_model_data</c> changed from a single VarInt into the four-list record 1.21.5 also uses.</param>
/// <param name="AttributeDisplay">771+: each attribute-modifier entry gained a trailing display field.</param>
/// <param name="ShearableEquippable">771+: <c>equippable</c> gained the <c>canBeSheared</c> + <c>shearingSound</c> tail. Set on the same eras as <paramref name="AttributeDisplay"/> and kept separate from it, because one field is appended to a modifier entry and the other to the equippable record; nothing says the next release moves them together.</param>
/// <param name="TypedEntityData">773+: <c>profile</c> changed shape, while <c>entity_data</c> and <c>block_entity_data</c> gained a registry type id before the tag. <c>bucket_entity_data</c> did not move with them.</param>
/// <param name="UnwrappedHolders">775+: <c>damage_resistant</c> became a holder set, and <c>instrument</c> and <c>jukebox_playable</c> lost their holder-or-inline wrapper.</param>
/// <param name="PotDecorationsStacks">777+: <c>pot_decorations</c> is exactly four optional item-stack templates (back, left, right, front) instead of a counted id list.</param>
internal readonly record struct ItemComponentLayout(
    string[] Ordering,
    ComponentWireEra Dialect,
    NestedStackForm NestedStacks,
    bool WideIdPayloads,
    bool ListCustomModelData,
    bool AttributeDisplay,
    bool ShearableEquippable,
    bool TypedEntityData,
    bool UnwrappedHolders,
    bool PotDecorationsStacks = false)
{
    /// <summary>766 (1.20.5/1.20.6): the first component era.</summary>
    public static ItemComponentLayout V1_20_5 { get; } = Legacy(ComponentIds.V1_20_5);

    /// <summary>767 (1.21/1.21.1): its own ordering, the 766 payloads.</summary>
    public static ItemComponentLayout V1_21 { get; } = Legacy(ComponentIds.V1_21);

    /// <summary>768 (1.21.2/1.21.3): its own 67-id ordering and the 1.21.2 payload family.</summary>
    public static ItemComponentLayout V1_21_2 { get; } =
        Legacy(ComponentIds.V1_21_2) with { WideIdPayloads = true };

    /// <summary>769 (1.21.4): the 768 ordering and payloads, with the four-list custom model data.</summary>
    public static ItemComponentLayout V1_21_4 { get; } =
        V1_21_2 with { ListCustomModelData = true };

    /// <summary>770 (1.21.5): the 96-id ordering, the modern dialect, no later payload change yet.</summary>
    public static ItemComponentLayout V1_21_5 { get; } = Modern(ComponentIds.V1_21_5);

    /// <summary>771/772 (1.21.6-1.21.8): the 770 ordering, the 1.21.6 attribute and equippable payloads.</summary>
    public static ItemComponentLayout V1_21_6 { get; } =
        V1_21_5 with { AttributeDisplay = true, ShearableEquippable = true };

    /// <summary>773 (1.21.9/1.21.10): as 771 plus the three components 1.21.9 re-shaped.</summary>
    public static ItemComponentLayout V1_21_9 { get; } = V1_21_6 with { TypedEntityData = true };

    /// <summary>774 (1.21.11): its own 104-id ordering, the 773 payloads.</summary>
    public static ItemComponentLayout V1_21_11 { get; } = V1_21_9 with { Ordering = ComponentIds.V1_21_11 };

    /// <summary>775 (26.1): its own 110-id ordering, the 26.x payloads, template nested stacks.</summary>
    public static ItemComponentLayout V26_1 { get; } = V1_21_9 with
    {
        Ordering = ComponentIds.V26_1,
        NestedStacks = NestedStackForm.Template,
        UnwrappedHolders = true,
    };

    /// <summary>776 (26.2): the 111-id ordering (<c>sulfur_cube_content</c> at 78), the 26.1 payloads.</summary>
    public static ItemComponentLayout V26_2 { get; } = V26_1 with { Ordering = ComponentIds.V26_2 };

    /// <summary>777 (26.3): its own 122-id ordering (thirteen components added, <c>swing_animation</c> and <c>map_color</c> removed), the 26.1 payloads. New components stay unmodeled until typed: the table reports their identifiers and the compact path raises the packet-scoped fault.</summary>
    public static ItemComponentLayout V26_3 { get; } = V26_2 with { Ordering = ComponentIds.V26_3, PotDecorationsStacks = true };

    /// <inheritdoc />
    public override string ToString()
    {
        string form =
            $"ids={WireShapeDigest.Of(Ordering)},{Dialect},{NestedStacks},wideid={(WideIdPayloads ? 1 : 0)}," +
            $"cmdlist={(ListCustomModelData ? 1 : 0)},attrdisplay={(AttributeDisplay ? 1 : 0)}," +
            $"shear={(ShearableEquippable ? 1 : 0)},typedentity={(TypedEntityData ? 1 : 0)}," +
            $"unwrapped={(UnwrappedHolders ? 1 : 0)}";
        return PotDecorationsStacks ? $"{form},potstacks=1" : form;
    }

    private static ItemComponentLayout Legacy(string[] ordering) =>
        new(
            ordering,
            ComponentWireEra.Legacy,
            NestedStackForm.CountFirst,
            WideIdPayloads: false,
            ListCustomModelData: false,
            AttributeDisplay: false,
            ShearableEquippable: false,
            TypedEntityData: false,
            UnwrappedHolders: false);

    private static ItemComponentLayout Modern(string[] ordering) =>
        new(
            ordering,
            ComponentWireEra.Modern,
            NestedStackForm.CountFirst,
            WideIdPayloads: true,
            ListCustomModelData: true,
            AttributeDisplay: false,
            ShearableEquippable: false,
            TypedEntityData: false,
            UnwrappedHolders: false);
}
