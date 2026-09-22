using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class AdvancementCodecs
{
    // Update advancements. Advancements arrived at 1.12 (protocol 335); before that the equivalent achievement data rode on the statistics packet, so 335 is the first era.
    //
    // The outer body has been constant since 1.12: reset bool, added list (id + node), removed id list, progress map (id -> criterion map -> optional obtained-instant long), and (from 1.21.5) a trailing show-advancements bool. DisplayInfo contains title, description, icon, VarInt frame, a full big-endian int flags (writeInt, NOT a VarInt), the background id only when bit 1 is set, then float x and float y.
    //
    // Four things vary, and only four:
    //   1. the node's criterion-name list  present 335-763, removed at 1.20.2 (764)
    //   2. the node's telemetry bool       added at 1.20 (763)
    //   3. the DisplayInfo icon slot form  legacy -> short-id -> present-id -> unnamed-NBT -> components
    //   4. the DisplayInfo text encoding   JSON string through 764, network NBT from 1.20.3 (765)
    //
    // The legacy node, display, and progress forms match the modern field order exactly.
    //
    // Recorded frames pin each boundary independently by requiring every shape to consume its frame exactly. The transitions are criteria removal at 764, telemetry addition at 763, and the trailing show-advancements boolean at 770.

    /// <summary>Update advancements on 1.12-1.12.2 (335-340): legacy id/damage icon slot, JSON text, criteria.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_12 =
        UpdateAdvancements(new AdvancementWireShape(IconLegacySlot, ComponentWire.V1_8, HasCriteria: true, HasTelemetry: false, HasShowAdvancements: false));

    /// <summary>Update advancements on 1.13-1.13.1 (393-401): short-id icon slot, JSON text, criteria.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_13 =
        UpdateAdvancements(new AdvancementWireShape(IconShortIdSlot, ComponentWire.V1_8, HasCriteria: true, HasTelemetry: false, HasShowAdvancements: false));

    /// <summary>Update advancements on 1.13.2-1.19.4 (404-762): present-flag VarInt-id icon slot with a NAMED NBT root, JSON text, criteria, no telemetry.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_13_2 =
        UpdateAdvancements(new AdvancementWireShape(IconPresentIdSlot, ComponentWire.V1_8, HasCriteria: true, HasTelemetry: false, HasShowAdvancements: false));

    /// <summary>Update advancements on 1.20-1.20.1 (763): as 1.13.2 plus the per-node telemetry bool that 1.20 appended after the requirements. The criterion list is still on the wire here; it goes at 1.20.2.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_20 =
        UpdateAdvancements(new AdvancementWireShape(IconPresentIdSlot, ComponentWire.V1_8, HasCriteria: true, HasTelemetry: true, HasShowAdvancements: false));

    /// <summary>Update advancements on 1.20.2 (764): the criterion list is gone, telemetry stays, the icon uses unnamed-root NBT, and the text is still a JSON string.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_20_2 =
        UpdateAdvancements(new AdvancementWireShape(IconVarIntIdSlot, ComponentWire.V1_8, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: false));

    /// <summary>Update advancements on 1.20.3-1.20.4 (765): as 1.20.2 but the text is network NBT.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_20_3 =
        UpdateAdvancements(new AdvancementWireShape(IconVarIntIdSlot, ComponentWire.V1_20_3, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: false));

    /// <summary>Update advancements on 1.20.5-1.20.6 (766): the icon becomes a component stack (766 table).</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_20_5 =
        UpdateAdvancements(new AdvancementWireShape(IconComponents(ItemPacketCodecShared.Table766), ComponentWire.V1_20_3, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: false));

    /// <summary>Update advancements on 1.21-1.21.1 (767): the 767 component-id table.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_21 =
        UpdateAdvancements(new AdvancementWireShape(IconComponents(ItemPacketCodecShared.Table767), ComponentWire.V1_20_3, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: false));

    /// <summary>Update advancements (protocol 770, 1.21.5 component era).</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_21_5 =
        MakeUpdateAdvancementsV1_21_5(ItemPacketCodecShared.Table770);

    /// <summary>Builds the 1.21.5-layout update-advancements codec over one component era table. The packet frame is constant from 1.21.5 to 1.21.11: reset, the advancement list, the removed set, the progress map, and the show-advancements bool. The only thing that moves across 770-774 is the component era of the DisplayInfo icon stack, which is exactly this parameter, so 771/772 (1.21.6 attribute display), 773 (1.21.9 profile and typed entity data) and 774 (its own id ordering) all get a correct codec from it.</summary>
    /// <param name="table">The protocol's component era table.</param>
    /// <returns>The codec.</returns>
    internal static PacketCodec<ClientboundUpdateAdvancementsPacket> MakeUpdateAdvancementsV1_21_5(ItemComponentTable table) =>
        UpdateAdvancements(new AdvancementWireShape(
            IconComponents(table), ComponentWire.V1_21_5, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: true));

    /// <summary>Builds the 768/769 update-advancements codec over one component era table: the 1.21.5 layout minus the trailing show-advancements bool (1.21.5 appended it), with legacy-dialect text.</summary>
    /// <param name="table">The protocol's component era table.</param>
    /// <returns>The codec.</returns>
    internal static PacketCodec<ClientboundUpdateAdvancementsPacket> MakeUpdateAdvancementsV1_21_2(ItemComponentTable table) =>
        UpdateAdvancements(new AdvancementWireShape(
            IconComponents(table), ComponentWire.V1_20_3, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: false));

    /// <summary>Builds a 26.1+ update-advancements codec: the 1.21.5 packet layout with the icon read as an <c>ItemStackTemplate</c> (holder id first, VarInt count, no empty sentinel) instead of an <c>ItemStack</c>.</summary>
    /// <remarks>26.1 changes only the icon encoding from count-first to holder-first template stacks.</remarks>
    /// <param name="table">The protocol's component era table.</param>
    /// <returns>The codec.</returns>
    internal static PacketCodec<ClientboundUpdateAdvancementsPacket> MakeUpdateAdvancementsV26_1(ItemComponentTable table) =>
        UpdateAdvancements(new AdvancementWireShape(
            IconTemplateComponents(table), ComponentWire.V1_21_5, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: true));

    /// <summary>Update advancements (protocol 775, 26.1 component era, template icon).</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV26_1 =
        MakeUpdateAdvancementsV26_1(ItemPacketCodecShared.Table775);

    /// <summary>Update advancements (protocol 776, 26.2 component era, template icon).</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV26_2 =
        MakeUpdateAdvancementsV26_1(ItemStackCodecs.ComponentsV26_2);

    /// <summary>Update advancements (protocol 777, 26.3 component era, template icon, positioned added elements).</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV26_3 =
        MakeUpdateAdvancementsV26_3(ItemStackCodecs.ComponentsV26_3);

    /// <summary>Builds a 26.3 update-advancements codec: the 26.1 layout whose added elements each carry trailing Float x and Float y, and whose DisplayInfo carries no inner coordinates.</summary>
    /// <param name="table">The protocol's component era table.</param>
    /// <returns>The codec.</returns>
    internal static PacketCodec<ClientboundUpdateAdvancementsPacket> MakeUpdateAdvancementsV26_3(ItemComponentTable table) =>
        UpdateAdvancements(new AdvancementWireShape(
            IconTemplateComponents(table), ComponentWire.V1_21_5, HasCriteria: false, HasTelemetry: true, HasShowAdvancements: true, HasPositions: true, HasDisplayCoordinates: false));

    /// <summary>768 (1.21.2/1.21.3) update advancements: identical to the 1.21.5 layout except the trailing show-advancements bool does not exist yet. Decode surfaces <see cref="ClientboundUpdateAdvancementsPacket.ShowAdvancements"/> as true (these versions always show); encode drops the field because it has no wire slot.</summary>
    /// <remarks>Protocols 768 and 769 use their own component tables so icon component ids resolve in the correct era ordering.</remarks>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_21_2 =
        MakeUpdateAdvancementsV1_21_2(ItemPacketCodecShared.Table768);

    /// <summary>769 (1.21.4) update advancements: the 768 layout under the 769 component era table.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_21_4 =
        MakeUpdateAdvancementsV1_21_2(ItemPacketCodecShared.Table769);

    /// <summary>771/772 (1.21.6-1.21.8) update advancements: the 1.21.5 layout, the 771 component era.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_21_6 =
        MakeUpdateAdvancementsV1_21_5(ItemPacketCodecShared.Table771);

    /// <summary>773 (1.21.9/1.21.10) update advancements: the 1.21.5 layout, the 773 component era.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_21_9 =
        MakeUpdateAdvancementsV1_21_5(ItemPacketCodecShared.Table773);

    /// <summary>774 (1.21.11) update advancements: the 1.21.5 layout, the 774 component era.</summary>
    public static readonly PacketCodec<ClientboundUpdateAdvancementsPacket> UpdateAdvancementsV1_21_11 =
        MakeUpdateAdvancementsV1_21_5(ItemPacketCodecShared.Table774);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUpdateAdvancements(PacketBindings bindings)
    {
        // Advancements exist from 1.12 (protocol 335); 1.8-1.11.2 carried achievements on the statistics packet instead, so there is nothing to bind below 335. The era timeline and the recorded-frame evidence for each boundary live on AdvancementCodecs.
        //
        // DisplayInfo icons are era item stacks, so a band is only registered where its icon form is actually modeled. Pre-component eras (335-765) all have their slot codec, so they bind. From 1.20.5 the icon is a component stack and the band binds where that era's component-id table exists, which is every protocol except 775. The 771 and 773 splits share the 770 ordering, but 1.21.6 added the attribute-modifier display field and 1.21.9 re-shaped profile / entity_data / block_entity_data, so they are their own eras.
        //
        // 1.21.11 writes a VarInt count first, with zero meaning empty. Protocols 26.1 and 26.2 write the item holder first, then a VarInt count, with no empty sentinel.
        bindings.Packet(UiPackets.Clientbound.UpdateAdvancements)
            .From(JavaProtocols.V1_12, AdvancementCodecs.UpdateAdvancementsV1_12)
            .From(JavaProtocols.V1_13, AdvancementCodecs.UpdateAdvancementsV1_13)
            .From(JavaProtocols.V1_13_2, AdvancementCodecs.UpdateAdvancementsV1_13_2)
            .From(JavaProtocols.V1_20, AdvancementCodecs.UpdateAdvancementsV1_20)
            .From(JavaProtocols.V1_20_2, AdvancementCodecs.UpdateAdvancementsV1_20_2)
            .From(JavaProtocols.V1_20_3, AdvancementCodecs.UpdateAdvancementsV1_20_3)
            .From(JavaProtocols.V1_20_5, AdvancementCodecs.UpdateAdvancementsV1_20_5)
            .From(JavaProtocols.V1_21, AdvancementCodecs.UpdateAdvancementsV1_21)
            .From(JavaProtocols.V1_21_2, AdvancementCodecs.UpdateAdvancementsV1_21_2)
            .From(JavaProtocols.V1_21_4, AdvancementCodecs.UpdateAdvancementsV1_21_4)
            .From(JavaProtocols.V1_21_5, AdvancementCodecs.UpdateAdvancementsV1_21_5)
            .From(JavaProtocols.V1_21_6, AdvancementCodecs.UpdateAdvancementsV1_21_6)
            .From(JavaProtocols.V1_21_9, AdvancementCodecs.UpdateAdvancementsV1_21_9)
            .From(JavaProtocols.V1_21_11, AdvancementCodecs.UpdateAdvancementsV1_21_11)
            .From(JavaProtocols.V26_1, AdvancementCodecs.UpdateAdvancementsV26_1)
            .From(JavaProtocols.V26_2, AdvancementCodecs.UpdateAdvancementsV26_2)
            .From(JavaProtocols.V26_3, AdvancementCodecs.UpdateAdvancementsV26_3);
    }
}
