using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Apply mob effect (<c>minecraft:update_mob_effect</c> / 1.8 <c>entity_effect</c>).</summary>
        public static readonly PacketType<ClientboundUpdateMobEffectPacket> UpdateMobEffect =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("update_mob_effect"));
    }
}

/// <summary>Apply mob effect. 1.8-1.17.1: effect id byte, amplifier byte, VarInt duration, hide-particles byte. 1.18-1.18.2: effect id VarInt, amplifier byte, VarInt duration, flags byte. 1.19-1.20.1: adds a trailing nullable <see cref="FactorData"/> network-NBT compound. Modern (1.20.2+): effect holder VarInt, VarInt amplifier, VarInt duration, flags byte (ambient/visible/icon/blend), no factor data.</summary>
public sealed record ClientboundUpdateMobEffectPacket(
    int EntityId,
    int EffectId,
    int Amplifier,
    int Duration,
    byte Flags,
    NbtTag? FactorData = null) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.UpdateMobEffect;
}
