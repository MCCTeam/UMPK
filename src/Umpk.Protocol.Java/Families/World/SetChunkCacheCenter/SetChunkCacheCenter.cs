using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set chunk cache center (<c>minecraft:set_chunk_cache_center</c>).</summary>
        public static readonly PacketType<ClientboundSetChunkCacheCenterPacket> SetChunkCacheCenter =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_chunk_cache_center"));
    }
}

/// <summary>Set chunk cache center: the new center chunk coordinate.</summary>
public sealed record ClientboundSetChunkCacheCenterPacket(int ChunkX, int ChunkZ) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetChunkCacheCenter;
}
