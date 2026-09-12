using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>1.8 named sound effect (<c>minecraft:sound_effect</c>).</summary>
        public static readonly PacketType<ClientboundNamedSoundPacket> NamedSound =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("sound_effect"));
    }
}

/// <summary>1.8 named sound: a sound name, a fixed-point (x8) position, a volume, and an x63 pitch byte.</summary>
public sealed record ClientboundNamedSoundPacket(
    string SoundName,
    int X,
    int Y,
    int Z,
    float Volume,
    byte Pitch) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.NamedSound;
}
