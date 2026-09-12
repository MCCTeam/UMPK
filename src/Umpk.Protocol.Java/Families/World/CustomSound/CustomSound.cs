using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.9 through 1.19.2 custom (resource-location addressed) sound (<c>minecraft:custom_sound</c>).</summary>
        public static readonly PacketType<ClientboundCustomSoundPacket> CustomSound =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("custom_sound"));
    }
}

/// <summary>1.9 through 1.19.2 custom sound: a sound name addressed by resource location rather than by registry id, a sound source, a fixed-point (x8) position, a volume and a pitch. <see cref="Seed"/> is the 1.19 variant-selection seed and stays zero on the eras that do not carry it.</summary>
/// <remarks>Distinct from <see cref="ClientboundSoundPacket"/>, which addresses the sound by registry id. 1.19.3 merged the two by giving <c>minecraft:sound</c> a holder-or-inline sound event, at which point this packet leaves the protocol.</remarks>
public sealed record ClientboundCustomSoundPacket(
    string SoundName,
    int Source,
    int X,
    int Y,
    int Z,
    float Volume,
    float Pitch,
    long Seed) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.CustomSound;
}
