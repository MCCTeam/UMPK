using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>The 26.1 nested item-stack form change. Every stack NESTED inside another payload changed from the count-first form to the template form, and the two swap their first two fields: the count-first stack is <c>VarInt count</c> (0 meaning empty) then the item holder id, and the template is the item holder id FIRST, then a VarInt count, with no empty sentinel at all.</summary>
/// <remarks>
/// <para>
/// In 26.1 and 26.2, the template is an item holder id, a VarInt count, and a component patch; a zero count or an AIR holder is invalid. The four nested locations that matter to UMPK, each contrasted with its 1.21.5/1.21.11 form:
/// <list type="bullet">
/// <item>Container contents: the per-slot empty sentinel becomes an explicit present bool.</item>
/// <item>Bundle contents and charged projectiles: the list framing is unchanged; only each element
/// changes from count-first to template form.</item>
/// <item>Advancement display icons: nothing else in display information moved, making the icon the one
/// place where the FORM can be isolated from every other era axis.</item>
/// </list>
/// The outer stack of the item packets did not move and still uses the count-first optional stack form, so this suite deliberately leaves the outer stack alone.
/// </para>
/// <para>WHY THE ASSERTIONS LOOK LIKE THIS. For a nested stack with no components the two forms are the same LENGTH and differ only by two swapped bytes, so a frame-length assertion cannot distinguish them and a round trip is blind twice over (encode and decode through the same wrong form agree perfectly, and the cross-era decode below shows it does not even fault, it silently yields a different item and a different count). Every pin here is therefore a specific BYTE VALUE at a fixed offset, a length difference where the container's present bool creates one, or a rejection.</para>
/// </remarks>
public class ItemStackTemplateBindingTests
{
    private const string SetSlot = "minecraft:container_set_slot";
    private const string UpdateAdvancements = "minecraft:update_advancements";

    private const int P774 = 774;   // 1.21.11: the last count-first era
    private const int P775 = 775;   // 26.1: the first template era
    private const int P776 = 776;   // 26.2

    // container_set_slot from 1.21.2 on: VarInt containerId, VarInt stateId, short slot, then the OUTER stack (VarInt count, VarInt item holder id, VarInt added, VarInt removed, then the patch entries). Every leading value used here is a single byte, so the first component's wire id sits at a fixed offset and its payload starts right after it.
    private const int ComponentIdOffset = 4 + 1 + 1 + 1 + 1;

    // For bundle_contents / charged_projectiles the payload is a VarInt list count, then the elements.
    private const int NestedStackOffset = ComponentIdOffset + 2;

    private static ItemStack Carrier(DataComponentMap components) =>
        new(ItemTestRegistries.Item(ItemTestRegistries.FilledMap), 1, components);

    private static ItemStack Nested(int networkId, int count) =>
        new(ItemTestRegistries.Item(networkId), count, DataComponentMap.Empty);

    private static byte[] EncodeSlot(int protocol, ItemStack stack) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot)
            .Encode(new ClientboundContainerSetSlotPacket(ContainerId: 1, StateId: 0, Slot: 0, Item: stack));

    // Holder-first framing on a nested stack with a REAL item and a count greater than one.

    /// <summary><c>minecraft:bundle_contents</c> holding one <c>diamond_sword</c> (holder id 2) with a count of 3. Under the count-first form the two payload bytes after the list count read 3 then 2; under the template they read 2 then 3. The frame is the SAME LENGTH either way, which is exactly why this has to be a byte assertion.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="componentWireId">The era's <c>bundle_contents</c> wire id.</param>
    /// <param name="firstByte">The first nested byte: the count on 774, the holder id from 775.</param>
    /// <param name="secondByte">The second nested byte: the holder id on 774, the count from 775.</param>
    [Theory]
    [InlineData(P774, 48, 3, 2)]
    [InlineData(P775, 50, 2, 3)]
    [InlineData(P776, 50, 2, 3)]
    public void BundleContents_NestedStackIsHolderFirstFrom775(
        int protocol, int componentWireId, int firstByte, int secondByte)
    {
        byte[] frame = EncodeSlot(
            protocol,
            Carrier(DataComponentMap.Empty.With(
                DataComponents.BundleContents,
                new BundleContentsComponent([Nested(ItemTestRegistries.DiamondSword, 3)]))));

        Assert.Equal(componentWireId, frame[ComponentIdOffset]);
        Assert.Equal(1, frame[ComponentIdOffset + 1]);            // the VarInt list count
        Assert.Equal(firstByte, frame[NestedStackOffset]);
        Assert.Equal(secondByte, frame[NestedStackOffset + 1]);
        Assert.Equal(0, frame[NestedStackOffset + 2]);            // nested patch: added
        Assert.Equal(0, frame[NestedStackOffset + 3]);            // nested patch: removed
        Assert.Equal(NestedStackOffset + 4, frame.Length);
    }

    /// <summary><c>minecraft:charged_projectiles</c> is the same list-of-elements shape as bundle_contents and moves with it, so a crossbow's loaded arrow is affected identically.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="componentWireId">The era's <c>charged_projectiles</c> wire id.</param>
    /// <param name="firstByte">The first nested byte.</param>
    /// <param name="secondByte">The second nested byte.</param>
    [Theory]
    [InlineData(P774, 47, 3, 2)]
    [InlineData(P775, 49, 2, 3)]
    [InlineData(P776, 49, 2, 3)]
    public void ChargedProjectiles_NestedStackIsHolderFirstFrom775(
        int protocol, int componentWireId, int firstByte, int secondByte)
    {
        byte[] frame = EncodeSlot(
            protocol,
            Carrier(DataComponentMap.Empty.With(
                DataComponents.ChargedProjectiles,
                new ChargedProjectilesComponent([Nested(ItemTestRegistries.DiamondSword, 3)]))));

        Assert.Equal(componentWireId, frame[ComponentIdOffset]);
        Assert.Equal(firstByte, frame[NestedStackOffset]);
        Assert.Equal(secondByte, frame[NestedStackOffset + 1]);
    }

    /// <summary>The three eras produce the same frame LENGTH for the same bundle, so a suite that only measured widths would have declared the 776 binding correct. Stated as its own pin so the blindness is on the record rather than implied.</summary>
    [Fact]
    public void BundleContents_LengthIsIdenticalAcrossTheFormChange()
    {
        ItemStack stack = Carrier(DataComponentMap.Empty.With(
            DataComponents.BundleContents,
            new BundleContentsComponent([Nested(ItemTestRegistries.DiamondSword, 3)])));

        Assert.Equal(EncodeSlot(P774, stack).Length, EncodeSlot(P775, stack).Length);
        Assert.Equal(EncodeSlot(P774, stack).Length, EncodeSlot(P776, stack).Length);
        Assert.NotEqual(EncodeSlot(P774, stack), EncodeSlot(P775, stack));
    }

    // 2. minecraft:container, where the template's missing sentinel DOES cost a byte.

    /// <summary>A container slot is <c>optional(ItemStackTemplate)</c> from 26.1, so a NON-EMPTY slot gains a leading present bool that the count-first form never had. That is a real one-byte-per-filled-slot length difference, and the first payload byte flips from the count to the bool.</summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="componentWireId">The era's <c>container</c> wire id.</param>
    /// <param name="extraBytes">The bytes this era adds over the 774 baseline.</param>
    [Theory]
    [InlineData(P774, 73, 0)]
    [InlineData(P775, 75, 1)]
    [InlineData(P776, 75, 1)]
    public void Container_FilledSlotGainsAPresentBoolFrom775(int protocol, int componentWireId, int extraBytes)
    {
        ItemStack stack = Carrier(DataComponentMap.Empty.With(
            DataComponents.Container,
            new ContainerComponent([new ContainerSlotEntry(0, Nested(ItemTestRegistries.DiamondSword, 3))])));

        byte[] frame = EncodeSlot(protocol, stack);

        Assert.Equal(componentWireId, frame[ComponentIdOffset]);
        Assert.Equal(1, frame[ComponentIdOffset + 1]);   // the VarInt list length

        // 774: [count=3][holder=2][0][0]. 775/776: [present=1][holder=2][count=3][0][0].
        Assert.Equal(protocol == P774 ? 3 : 1, frame[NestedStackOffset]);
        Assert.Equal(NestedStackOffset + 4 + extraBytes, frame.Length);
    }

    /// <summary>The trap that hid this for a whole release: an EMPTY container slot is a single 0x00 byte under BOTH forms (the count-first zero sentinel, or the template's false present bool), so a container of empty slots is byte-identical across the change and can never separate the two eras. Only a slot carrying a real item can, which is what the pin above does.</summary>
    [Fact]
    public void Container_AllEmptySlotsAreByteIdenticalAcrossTheFormChange()
    {
        var empties = new ContainerComponent(
        [
            new ContainerSlotEntry(0, ItemStack.Empty),
            new ContainerSlotEntry(1, ItemStack.Empty),
        ]);

        byte[] frame774 = EncodeSlot(P774, Carrier(DataComponentMap.Empty.With(DataComponents.Container, empties)));
        byte[] frame776 = EncodeSlot(P776, Carrier(DataComponentMap.Empty.With(DataComponents.Container, empties)));

        // Only the component wire id byte differs (73 versus 75); the payload is identical.
        Assert.Equal(frame774.Length, frame776.Length);
        Assert.Equal(frame774.AsSpan(ComponentIdOffset + 1).ToArray(), frame776.AsSpan(ComponentIdOffset + 1).ToArray());
    }

    // Cross-era rejection on the nested stack, both directions.

    /// <summary>A 26.1 container frame must not decode under 1.21.11, and a 1.21.11 one must not decode under 26.1. Both the component id ORDERING and the nested form move at that boundary, so this pins the boundary rather than isolating the form; the form on its own is isolated by the advancement-icon pins below, where nothing else in the packet moved.</summary>
    /// <param name="from">The protocol that builds the frame.</param>
    /// <param name="to">The protocol that must refuse it.</param>
    [Theory]
    [InlineData(P775, P774)]
    [InlineData(P774, P775)]
    [InlineData(P776, P774)]
    public void ContainerFrame_DoesNotCrossTheFormBoundary(int from, int to)
    {
        byte[] frame = EncodeSlot(
            from,
            Carrier(DataComponentMap.Empty.With(
                DataComponents.Container,
                new ContainerComponent([new ContainerSlotEntry(0, Nested(ItemTestRegistries.DiamondSword, 3))]))));

        Assert.ThrowsAny<Exception>(() => BoundCodec.At(to, PacketFlow.Clientbound, SetSlot).DecodeFrame(frame));
    }

    // The advancement icon: the one carrier where ONLY the stack form moved.

    private static ClientboundUpdateAdvancementsPacket IconPacket(ItemStack icon) => new(
        Reset: false,
        Added:
        [
            new AdvancementEntry(
                Identifier.Minecraft("story/root"),
                new AdvancementNode(
                    Parent: null,
                    Display: new AdvancementDisplayInfo(
                        Component.Text("t"),
                        Component.Text("d"),
                        icon,
                        AdvancementFrameType.Task,
                        Background: null,
                        ShowToast: true,
                        Hidden: false,
                        X: 0f,
                        Y: 0f),
                    Criteria: [],
                    Requirements: [],
                    SendsTelemetryEvent: false)),
        ],
        Removed: [],
        Progress: [],
        ShowAdvancements: true);

    /// <summary>The 26.x icon really is the template. 1.21.11 and 26.2 produce frames of the same length for the same advancement, and the cross-era decode does not fault: it SILENTLY yields a different item and a different count, because the swapped fields both happen to be valid. That is the exact failure mode the 776 binding had in production, and it is invisible to a round trip.</summary>
    [Fact]
    public void AdvancementIcon_CrossWireLayoutDecodeIsSilentlyWrong()
    {
        // diamond_sword (holder id 2) with a count of 3, so the two swapped bytes are distinguishable AND each is a valid value in the other position.
        ClientboundUpdateAdvancementsPacket packet = IconPacket(Nested(ItemTestRegistries.DiamondSword, 3));

        BoundPacketCodec v774 = BoundCodec.At(P774, PacketFlow.Clientbound, UpdateAdvancements);
        BoundPacketCodec v776 = BoundCodec.At(P776, PacketFlow.Clientbound, UpdateAdvancements);
        byte[] frame774 = v774.Encode(packet);
        byte[] frame776 = v776.Encode(packet);

        // The whole packet body is constant from 1.21.5 to 26.2 (both eras use the same reset / added / removed / progress / showAdvancements sequence), so any difference here is the icon and nothing else.
        Assert.Equal(frame774.Length, frame776.Length);
        Assert.NotEqual(frame774, frame776);

        ItemStack own = Icon(v776.DecodeFrame(frame776));
        Assert.Equal(ItemTestRegistries.DiamondSword, own.Item.NetworkId);
        Assert.Equal(3, own.Count);

        ItemStack crossed = Icon(v776.DecodeFrame(frame774));
        Assert.Equal(ItemTestRegistries.FilledMap, crossed.Item.NetworkId);
        Assert.Equal(2, crossed.Count);
    }

    /// <summary>The count-first form is REJECTED from 775 on, in both directions, and the empty stack is the sharpest case: <c>ItemStackTemplate</c>'s canonical constructor throws for a zero count or an AIR holder, so 26.1 simply has no wire spelling for an absent icon, while 1.21.11 spells it as the single byte 0x00.</summary>
    /// <param name="protocol">The 26.x protocol.</param>
    [Theory]
    [InlineData(P775)]
    [InlineData(P776)]
    public void AdvancementIcon_CountFirstEmptyStackIsRejectedFrom775(int protocol)
    {
        BoundPacketCodec template = BoundCodec.At(protocol, PacketFlow.Clientbound, UpdateAdvancements);

        // Encode: there is nothing to write, so this faults instead of inventing a sentinel.
        Assert.Throws<ProtocolViolationException>(() => template.Encode(IconPacket(ItemStack.Empty)));

        // Decode: 774 writes the empty icon as one 0x00 byte, which the template reader takes as item holder id 0 and then mis-frames everything after it.
        byte[] emptyIcon774 = BoundCodec.At(P774, PacketFlow.Clientbound, UpdateAdvancements)
            .Encode(IconPacket(ItemStack.Empty));
        Assert.ThrowsAny<Exception>(() => template.DecodeFrame(emptyIcon774));
    }

    /// <summary>775 and 776 agree on the icon form (26.1 introduced it and 26.2 kept it), and their component id orderings are identical below wire id 78, so the same advancement encodes byte-for-byte the same on both. Guards against a future split of one without the other.</summary>
    [Fact]
    public void AdvancementIcon_775And776AgreeOnTheTemplateForm()
    {
        ClientboundUpdateAdvancementsPacket packet = IconPacket(Nested(ItemTestRegistries.DiamondSword, 3));

        Assert.Equal(
            BoundCodec.At(P775, PacketFlow.Clientbound, UpdateAdvancements).Encode(packet),
            BoundCodec.At(P776, PacketFlow.Clientbound, UpdateAdvancements).Encode(packet));
    }

    private static ItemStack Icon(object packet) =>
        ((ClientboundUpdateAdvancementsPacket)packet).Added[0].Value.Display!.Icon;
}
