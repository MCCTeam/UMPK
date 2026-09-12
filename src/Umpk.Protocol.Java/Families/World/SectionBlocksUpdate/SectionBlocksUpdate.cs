using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Section (multi) block update (<c>minecraft:section_blocks_update</c> / 1.8 <c>block_change_multi</c>).</summary>
        public static readonly PacketType<ClientboundSectionBlocksUpdatePacket> SectionBlocksUpdate =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("section_blocks_update"));
    }
}

/// <summary>Section (multi) block update. Modern (770/776) carries a packed <see cref="SectionPos"/> long and a list of packed VarLongs (<c>state &lt;&lt; 12 | localXYZ</c>). 1.8 carries two chunk ints plus a list of (short position, VarInt state) records; the era codec normalizes both into the same fields and sets <see cref="IsLegacy"/> so consumers decode <see cref="SectionBlockChange.PackedPosition"/> with the right bit layout (legacy short: x:4 hi, z:4, y:8 lo, absolute Y; modern: 12-bit local xzy).</summary>
public sealed record ClientboundSectionBlocksUpdatePacket(
    long SectionPos,
    int LegacyChunkX,
    int LegacyChunkZ,
    IReadOnlyList<SectionBlockChange> Changes,
    bool IsLegacy = false) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SectionBlocksUpdate;
}

/// <summary>One entry of a section (multi) block update: the packed in-section position and state id.</summary>
public readonly record struct SectionBlockChange(short PackedPosition, int BlockStateId);
