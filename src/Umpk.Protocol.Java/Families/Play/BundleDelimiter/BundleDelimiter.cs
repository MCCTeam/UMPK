using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Bundle delimiter (<c>minecraft:bundle_delimiter</c>, 1.19.4+): an empty payload that opens and then closes an atomic group of packets.</summary>
        public static readonly PacketType<ClientboundBundleDelimiterPacket> BundleDelimiter =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("bundle_delimiter"));
    }
}

/// <summary>Bundle delimiter (clientbound play, empty payload, 1.19.4+). The frame carries nothing: its whole meaning is its arrival, which opens a bundle and then closes it.</summary>
public sealed record ClientboundBundleDelimiterPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.BundleDelimiter;
}
