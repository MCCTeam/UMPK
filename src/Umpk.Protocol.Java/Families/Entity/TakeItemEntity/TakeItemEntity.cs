using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Item pickup (<c>minecraft:take_item_entity</c> / 1.8 <c>collect_item</c>).</summary>
        public static readonly PacketType<ClientboundTakeItemEntityPacket> TakeItemEntity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("take_item_entity"));
    }
}

/// <summary>Item pickup: collected item entity id, collector entity id, and (modern) the amount.</summary>
public sealed record ClientboundTakeItemEntityPacket(int ItemEntityId, int CollectorEntityId, int? Amount) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.TakeItemEntity;
}
