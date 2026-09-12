using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.ItemPacketCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class ContainerCodecs
{
    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareContainerSetSlot(PacketBindings bindings)
    {
        // Same era split as container_set_content: 1.14-1.17 (477-755) is the no-state V1_14 form and 1.17.1-1.20.3 (756-765) adds the state id (V1_17_1). The 1.9 slot is the 1.8 slot: the damage short survives until the flattening drops it at 1.13, so 1.9-1.12.2 (107-340) take the legacy-stack V1_9 form. The V1_13 form would interpret the damage short as the start of NBT; empty stacks do not distinguish those forms. Same NAMED-vs-unnamed NBT-root split as container_set_content above: 477-763 named, 764/765 unnamed. The component band needs one entry for every ordering or payload change. Protocols 768 and 769 diverge from 770 at wire id 15, protocol 774 at wire id 5, and protocol 775 from 776 at wire id
        // 78. A stack carrying a component at or beyond those ids dispatches to the wrong payload codec
        // under a neighbouring table.
        PacketTimelineBuilder<ClientboundContainerSetSlotPacket> setSlot =
            bindings.Packet(ItemPackets.Clientbound.ContainerSetSlot)
                .From(JavaProtocols.V1_9, ContainerCodecs.ContainerSetSlotV1_9)
                .From(JavaProtocols.V1_13, ContainerCodecs.ContainerSetSlotV1_13)
                .From(JavaProtocols.V1_13_2, ContainerCodecs.ContainerSetSlotV1_13_2)
                .From(JavaProtocols.V1_14, ContainerCodecs.ContainerSetSlotV1_14)
                .From(JavaProtocols.V1_17_1, ContainerCodecs.ContainerSetSlotV1_17_1)
                .From(JavaProtocols.V1_20_2, ContainerCodecs.ContainerSetSlotV1_20_2)
                .From(JavaProtocols.V1_20_5, ContainerCodecs.ContainerSetSlotV1_20_5)
                .From(JavaProtocols.V1_21, ContainerCodecs.ContainerSetSlotV1_21);

        foreach ((int protocol, string era, ItemComponentTable table) in ItemPacketCodecShared.ComponentEras)
            setSlot.From(protocol, ItemPacketCodecShared.MakeContainerSetSlot(table), $"MakeContainerSetSlot({era})");

    }
}
