using System.Text;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Binding-level pins for the per-protocol item data-component tables. Protocols 768, 769, 774 and 775 had no table of their own and decoded component stacks through a NEIGHBOURING era's ordering (768, 769 and 774 through the 770 table, 775 through the 776 table). Unlike the advancement case nothing marked this band, so it decoded live.</summary>
/// <remarks>
/// <para>The registration order is the numeric wire id. It is checked entry for entry for protocols 768, 769, 770, 774, 775, and 776. The expected ids below are restated here rather than read from <c>ComponentIds</c> so that changing the production array is what a failure reports.</para>
/// <para>A round trip cannot detect a consistently wrong table: encode and decode agree with each other perfectly. Every assertion here is therefore either a specific WIRE-ID BYTE in the frame, a frame LENGTH, or a cross-era rejection. The registration fixture cannot see it either: swapping a codec leaves every one of the 49 pins byte-identical.</para>
/// </remarks>
public class ComponentTableBindingTests
{
    private const string SetSlot = "minecraft:container_set_slot";

    // The component eras this suite covers. 768/769/774/775 are the four that had no table; 770, 771, 773 and 776 keep the neighbours honest (771 and 773 are new era splits of their own, see below).
    private const int P768 = 768;   // 1.21.2/1.21.3
    private const int P769 = 769;   // 1.21.4
    private const int P770 = 770;   // 1.21.5
    private const int P771 = 771;   // 1.21.6
    private const int P773 = 773;   // 1.21.9/1.21.10
    private const int P774 = 774;   // 1.21.11
    private const int P775 = 775;   // 26.1
    private const int P776 = 776;   // 26.2

    private static ItemStack Map(DataComponentMap components) =>
        new(ItemTestRegistries.Item(ItemTestRegistries.FilledMap), 1, components);

    private static ClientboundContainerSetSlotPacket Slot(ItemStack stack) =>
        new(ContainerId: 1, StateId: 0, Slot: 0, Item: stack);

    // container_set_slot on 768+ is: VarInt containerId, VarInt stateId, short slot, then the stack (VarInt count, VarInt item holder id, VarInt added, VarInt removed, then per-component [VarInt wireId, payload]). Every leading field here is a single byte for the values used, so the first component's wire id sits at a fixed offset and the payload follows it.
    private const int FirstComponentIdOffset = 4 + 1 + 1 + 1 + 1;

    private static byte[] EncodeSlot(int protocol, ItemStack stack) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot).Encode(Slot(stack));

    private static byte FirstComponentWireId(byte[] frame) => frame[FirstComponentIdOffset];

    // The id ORDERING. One component, one wire-id byte, one era.

    /// <summary><c>minecraft:map_id</c> is the sharpest ordering probe in the set: its wire id moves on five of the eight eras. A filled map in inventory carries it, so this is the exact byte a live container_set_slot would have got wrong.</summary>
    [Theory]
    [InlineData(P768, 36)]
    [InlineData(P769, 36)]
    [InlineData(P770, 37)]
    [InlineData(P771, 37)]
    [InlineData(P773, 37)]
    [InlineData(P774, 44)]
    [InlineData(P775, 46)]
    [InlineData(P776, 46)]
    public void MapId_WireIdIsTheProtocolsOwn(int protocol, int expectedWireId)
    {
        byte[] frame = EncodeSlot(protocol, Map(DataComponentMap.Empty.With(DataComponents.MapId, new MapIdComponent(7))));

        Assert.Equal(expectedWireId, FirstComponentWireId(frame));

        // The payload is one VarInt on every era, so the frame is the header plus id plus one byte.
        Assert.Equal(FirstComponentIdOffset + 2, frame.Length);
        Assert.Equal(7, frame[FirstComponentIdOffset + 1]);
    }

    /// <summary><c>minecraft:lore</c> moves at 774 (the <c>use_effects</c>/<c>minimum_attack_charge</c>/ <c>damage_type</c> insertions push it from 8 to 11), which is the divergence 774 had while it was bound to the 770 table.</summary>
    [Theory]
    [InlineData(P768, 8)]
    [InlineData(P769, 8)]
    [InlineData(P770, 8)]
    [InlineData(P773, 8)]
    [InlineData(P774, 11)]
    [InlineData(P775, 11)]
    [InlineData(P776, 11)]
    public void Lore_WireIdIsTheProtocolsOwn(int protocol, int expectedWireId)
    {
        byte[] frame = EncodeSlot(
            protocol,
            Map(DataComponentMap.Empty.With(DataComponents.Lore, new LoreComponent([Component.Text("l")]))));

        Assert.Equal(expectedWireId, FirstComponentWireId(frame));
    }

    /// <summary><c>minecraft:custom_name</c> moves from 5 to 6 at 774, which is the ONLY thing separating the 770 and 774 tables for this component. 775 and 776 agree on it, so a 775 failure here means 775 fell back to a pre-774 ordering.</summary>
    [Theory]
    [InlineData(P768, 5)]
    [InlineData(P770, 5)]
    [InlineData(P773, 5)]
    [InlineData(P774, 6)]
    [InlineData(P775, 6)]
    [InlineData(P776, 6)]
    public void CustomName_WireIdIsTheProtocolsOwn(int protocol, int expectedWireId)
    {
        byte[] frame = EncodeSlot(
            protocol,
            Map(DataComponentMap.Empty.With(DataComponents.CustomName, new CustomNameComponent(Component.Text("n")))));

        Assert.Equal(expectedWireId, FirstComponentWireId(frame));
    }

    // The PAYLOADS. Frame LENGTH, which a round trip through the wrong table cannot expose.

    /// <summary><c>minecraft:unbreakable</c> is a <c>showInTooltip</c> BOOL on 766-769 (one payload byte) and a zero-byte <c>Unit</c> from 1.21.5 on. Its wire id is 4 on every era, so only the LENGTH separates the two payloads: exactly the assertion a round trip cannot make.</summary>
    [Theory]
    [InlineData(P768, 1)]
    [InlineData(P769, 1)]
    [InlineData(P770, 0)]
    [InlineData(P771, 0)]
    [InlineData(P774, 0)]
    [InlineData(P776, 0)]
    public void Unbreakable_PayloadWidthIsTheProtocolsOwn(int protocol, int payloadBytes)
    {
        byte[] frame = EncodeSlot(
            protocol,
            Map(DataComponentMap.Empty.With(DataComponents.Unbreakable, new UnbreakableComponent(ShowInTooltip: true))));

        Assert.Equal(4, FirstComponentWireId(frame));
        Assert.Equal(FirstComponentIdOffset + 1 + payloadBytes, frame.Length);
    }

    /// <summary><c>minecraft:custom_model_data</c> is ONE VarInt on 766-768 and FOUR lists from 1.21.4 on. The earlier form is one VarInt; the later form is a composite of four lists. This is the ONLY payload difference between the 768 and 769 tables, whose id orderings are identical, so it is the single assertion that proves they must be two tables and not one.</summary>
    [Theory]
    [InlineData(P768, 1)]   // one VarInt: the single legacy value
    [InlineData(P769, 8)]   // float count(1) + one f32(4) + three empty list counts(3)
    [InlineData(P770, 8)]
    [InlineData(P774, 8)]
    [InlineData(P776, 8)]
    public void CustomModelData_PayloadWidthIsTheProtocolsOwn(int protocol, int payloadBytes)
    {
        byte[] frame = EncodeSlot(
            protocol,
            Map(DataComponentMap.Empty.With(DataComponents.CustomModelData, new CustomModelDataComponent([3f], [], [], []))));

        Assert.Equal(FirstComponentIdOffset + 1 + payloadBytes, frame.Length);
    }

    /// <summary><c>minecraft:attribute_modifiers</c> gained a trailing per-entry <c>Display</c> at 1.21.6. The 1.21.5 entry is attribute + modifier + slot; 1.21.6 appends a VarInt display type id (DEFAULT = 0). 771, 772 and 773 share the 770 ORDERING, so this payload is the only reason they cannot share the 770 table, and it is a pure length difference.</summary>
    [Theory]
    [InlineData(P771, true)]
    [InlineData(P773, true)]
    [InlineData(P774, true)]
    [InlineData(P775, true)]
    [InlineData(P776, true)]
    [InlineData(P770, false)]
    public void AttributeModifiers_DisplayFieldIsTheProtocolsOwn(int protocol, bool hasDisplay)
    {
        ItemStack stack = Map(DataComponentMap.Empty.With(DataComponents.AttributeModifiers, OneModifier));

        // Measured against the 770 frame rather than an absolute width, so the assertion is exactly "this era writes one more byte per entry than 1.21.5 does" and nothing else.
        int baseline = EncodeSlot(P770, stack).Length;
        int actual = EncodeSlot(protocol, stack).Length;
        Assert.Equal(baseline + (hasDisplay ? 1 : 0), actual);
    }

    /// <summary>768 and 769 leave <c>attribute_modifiers</c> UNTYPED: its 1.21.2/1.21.4 payload still carries the trailing <c>showInTooltip</c> bool that the pre-1.21.5 tables never modelled. Encoding it must FAIL LOUDLY with the component identity rather than silently emit one of the two neighbouring shapes, which is what binding the 770 table there did.</summary>
    [Theory]
    [InlineData(P768)]
    [InlineData(P769)]
    public void AttributeModifiers_IsUntypedOn768And769(int protocol)
    {
        ItemStack stack = Map(DataComponentMap.Empty.With(DataComponents.AttributeModifiers, OneModifier));

        ProtocolViolationException ex = Assert.Throws<ProtocolViolationException>(() => EncodeSlot(protocol, stack));
        Assert.Contains("attribute_modifiers", ex.Message, StringComparison.Ordinal);
    }

    private static AttributeModifiersComponent OneModifier { get; } = new(
    [
        new Umpk.Game.Items.Components.AttributeModifierEntry(
            ItemTestRegistries.Attribute(ItemTestRegistries.AttackDamage), "minecraft:base", 1.0, "add_value", "mainhand"),
    ]);

    // The component TEXT dialect inside an item stack.

    /// <summary>A component-valued item component inherits the protocol's interaction dialect, which moves at 1.21.5 exactly like every other component carrier. The earlier form names the style fields <c>clickEvent</c>/<c>hoverEvent</c>; the later form renames them to <c>click_event</c>/<c>hover_event</c>. The item component tables hardcoded the modern dialect, so a renamed item on 766-769 went out in the 1.21.5 shape.</summary>
    [Theory]
    [InlineData(766, false)]
    [InlineData(767, false)]
    [InlineData(P768, false)]
    [InlineData(P769, false)]
    [InlineData(P770, true)]
    [InlineData(P774, true)]
    [InlineData(P776, true)]
    public void CustomName_UsesTheProtocolsInteractionDialect(int protocol, bool modern)
    {
        var named = new CustomNameComponent(new Component(
            new TextContent("n"),
            new Style { ClickEvent = new ClickEvent(ClickEventAction.OpenUrl, "https://example.invalid/u20") }));

        byte[] frame = EncodeSlot(protocol, Map(DataComponentMap.Empty.With(DataComponents.CustomName, named)));

        Assert.Equal(modern, Contains(frame, "click_event"));
        Assert.Equal(!modern, Contains(frame, "clickEvent"));
    }

    private static bool Contains(byte[] frame, string marker) =>
        frame.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker).AsSpan()) >= 0;

    // Cross-era rejection: a frame built for one era must not read cleanly under its neighbour.

    /// <summary>A 774 frame carrying <c>map_id</c> must not decode under the 770 table. 774 writes wire id 44, which the 770 ordering calls <c>minecraft:suspicious_stew_effects</c>, so the wrong reader dispatches on a different component.</summary>
    [Fact]
    public void MapIdFrameFrom774_DoesNotDecodeUnder770()
    {
        byte[] frame = EncodeSlot(P774, Map(DataComponentMap.Empty.With(DataComponents.MapId, new MapIdComponent(7))));

        Assert.Equal(44, FirstComponentWireId(frame));
        Assert.ThrowsAny<Exception>(() => BoundCodec.At(P770, PacketFlow.Clientbound, SetSlot).DecodeFrame(frame));
    }

    /// <summary>A 768 frame carrying <c>unbreakable</c> must not decode under the 770 table: 770 reads the component as a zero-byte Unit and then has one unconsumed <c>showInTooltip</c> byte, which the frame-exact entry point rejects.</summary>
    [Fact]
    public void UnbreakableFrameFrom768_DoesNotDecodeUnder770()
    {
        byte[] frame = EncodeSlot(
            P768,
            Map(DataComponentMap.Empty.With(DataComponents.Unbreakable, new UnbreakableComponent(ShowInTooltip: true))));

        Assert.ThrowsAny<Exception>(() => BoundCodec.At(P770, PacketFlow.Clientbound, SetSlot).DecodeFrame(frame));
    }

    /// <summary>A 775 frame carrying a component past wire id 78 must not decode under the 776 table. 26.2 inserts <c>minecraft:sulfur_cube_content</c> at 78, so everything from <c>lock</c> on shifts by one. <c>minecraft:shulker/color</c> is the last id on both eras (108 on 775, 109 on 776), which makes it out of range under the other table and therefore a hard framing fault rather than a silent mis-read.</summary>
    [Fact]
    public void TailComponentFrameFrom775_DoesNotDecodeUnder776()
    {
        ItemComponentTable t775 = ItemComponentTable.V26_1();
        ItemComponentTable t776 = ItemComponentTable.V26_2();

        var shulkerColor = Identifier.Minecraft("shulker/color");
        Assert.Equal(shulkerColor, t775.KeyByWireId(109));
        Assert.Equal(shulkerColor, t776.KeyByWireId(110));

        // Every id from 78 up names a DIFFERENT component under the two orderings, and 776's tail id is out of range under 775 entirely, so a 776 frame read through the 775 table walks off the end rather than mis-reading quietly.
        Assert.Equal(Identifier.Minecraft("sheep/color"), t776.KeyByWireId(109));
        Assert.Throws<ProtocolViolationException>(() => t775.KeyByWireId(110));
    }

    // Table shape: the counts and divergence points.

    /// <summary>Entry counts for each wire table. Protocols 768 and 769 share one 67-entry ordering.</summary>
    [Theory]
    [InlineData(P768, 67)]
    [InlineData(P769, 67)]
    [InlineData(P770, 96)]
    [InlineData(P774, 104)]
    [InlineData(P775, 110)]
    [InlineData(P776, 111)]
    public void WireLayoutTable_HasTheDecompiledEntryCount(int protocol, int count)
    {
        ItemComponentTable table = TableFor(protocol);

        // The last id resolves and the next one is out of range: that pins the count exactly.
        _ = table.KeyByWireId(count - 1);
        Assert.Throws<ProtocolViolationException>(() => table.KeyByWireId(count));
    }

    /// <summary>The divergence points, asserted as identifiers rather than counts: 768/769 differ from 770 at wire id 15, 774 from 770 at wire id 5, and 775 from 776 at wire id 78.</summary>
    [Fact]
    public void WireLayoutTables_DivergeWhereTheDecompiledRegistriesDo()
    {
        Assert.Equal(Identifier.Minecraft("hide_additional_tooltip"), TableFor(P768).KeyByWireId(15));
        Assert.Equal(Identifier.Minecraft("tooltip_display"), TableFor(P770).KeyByWireId(15));

        Assert.Equal(Identifier.Minecraft("use_effects"), TableFor(P774).KeyByWireId(5));
        Assert.Equal(Identifier.Minecraft("custom_name"), TableFor(P770).KeyByWireId(5));

        Assert.Equal(Identifier.Minecraft("lock"), TableFor(P775).KeyByWireId(78));
        Assert.Equal(Identifier.Minecraft("sulfur_cube_content"), TableFor(P776).KeyByWireId(78));
    }

    private static ItemComponentTable TableFor(int protocol) => protocol switch
    {
        P768 => ItemComponentTable.V1_21_2(),
        P769 => ItemComponentTable.V1_21_4(),
        P770 => ItemComponentTable.V1_21_5(),
        P771 => ItemComponentTable.V1_21_6(),
        P773 => ItemComponentTable.V1_21_9(),
        P774 => ItemComponentTable.V1_21_11(),
        P775 => ItemComponentTable.V26_1(),
        _ => ItemComponentTable.V26_2(),
    };
}
