using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Entity-following sound (<c>minecraft:sound_entity</c>).</summary>
        public static readonly PacketType<ClientboundSoundEntityPacket> SoundEntity =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("sound_entity"));
    }
}

/// <summary>Entity-following sound (modern <c>minecraft:sound_entity</c>).</summary>
public sealed record ClientboundSoundEntityPacket(
    SoundEventHolder Sound,
    int Source,
    int EntityId,
    float Volume,
    float Pitch,
    long Seed) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SoundEntity;
}
