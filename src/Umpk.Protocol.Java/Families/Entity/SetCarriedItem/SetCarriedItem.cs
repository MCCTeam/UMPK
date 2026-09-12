using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Set carried item (<c>minecraft:set_carried_item</c>).</summary>
        public static readonly PacketType<ServerboundSetCarriedItemPacket> SetCarriedItem =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("set_carried_item"));
    }
}

/// <summary>Set carried item (serverbound): the hotbar slot as a short.</summary>
public sealed record ServerboundSetCarriedItemPacket(short Slot) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.SetCarriedItem;
}
