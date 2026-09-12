using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Standalone light update (<c>minecraft:light_update</c>).</summary>
        public static readonly PacketType<ClientboundLightUpdatePacket> LightUpdate =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("light_update"));
    }
}

/// <summary>Standalone light update: the chunk coordinate and the light data block.</summary>
public sealed record ClientboundLightUpdatePacket(int ChunkX, int ChunkZ, LightUpdateData Light) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.LightUpdate;
}
