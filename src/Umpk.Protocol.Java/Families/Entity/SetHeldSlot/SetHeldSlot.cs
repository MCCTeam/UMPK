using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Held item slot (<c>minecraft:set_held_slot</c> / 1.8 <c>held_item_slot</c>).</summary>
        public static readonly PacketType<ClientboundSetHeldSlotPacket> SetHeldSlot =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_held_slot"));
    }
}

/// <summary>Held item slot (clientbound): the hotbar slot (byte on 1.8, VarInt modern).</summary>
public sealed record ClientboundSetHeldSlotPacket(int Slot) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.SetHeldSlot;
}
