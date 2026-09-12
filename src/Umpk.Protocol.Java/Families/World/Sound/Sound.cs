using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Sound effect with a holder-or-inline sound event (<c>minecraft:sound</c>).</summary>
        public static readonly PacketType<ClientboundSoundPacket> Sound =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("sound"));
    }
}

/// <summary>Sound effect at a fixed-point position (modern <c>minecraft:sound</c>).</summary>
public sealed record ClientboundSoundPacket(
    SoundEventHolder Sound,
    int Source,
    int X,
    int Y,
    int Z,
    float Volume,
    float Pitch,
    long Seed) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.Sound;
}
