using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Forget level chunk (<c>minecraft:forget_level_chunk</c>, modern chunk unload).</summary>
        public static readonly PacketType<ClientboundForgetLevelChunkPacket> ForgetLevelChunk =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("forget_level_chunk"));
    }
}

// Chunk metadata

/// <summary>Forget level chunk (modern unload): the chunk coordinate.</summary>
public sealed record ClientboundForgetLevelChunkPacket(ChunkPos Chunk) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.ForgetLevelChunk;
}
