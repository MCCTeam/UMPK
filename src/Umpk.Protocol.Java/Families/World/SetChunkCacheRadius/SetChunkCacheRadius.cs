using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Set chunk cache radius (<c>minecraft:set_chunk_cache_radius</c>).</summary>
        public static readonly PacketType<ClientboundSetChunkCacheRadiusPacket> SetChunkCacheRadius =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_chunk_cache_radius"));
    }
}

/// <summary>Set chunk cache radius: the new view distance in chunks.</summary>
public sealed record ClientboundSetChunkCacheRadiusPacket(int Radius) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetChunkCacheRadius;
}
