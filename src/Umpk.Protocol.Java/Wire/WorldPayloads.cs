using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

/// <summary>A light-update data block: the four presence bitsets (as long arrays) and the sky/block nibble arrays (2048 bytes each). Shared by the standalone light-update packet and, later, chunk-with-light.</summary>
/// <param name="SkyYMask">Sections carrying a sky-light array, as a bitset packed into longs.</param>
/// <param name="BlockYMask">Sections carrying a block-light array, as a bitset packed into longs.</param>
/// <param name="EmptySkyYMask">Sections whose sky light is known to be all zero.</param>
/// <param name="EmptyBlockYMask">Sections whose block light is known to be all zero.</param>
/// <param name="SkyUpdates">One 2048-byte nibble array per set bit of the sky mask.</param>
/// <param name="BlockUpdates">One 2048-byte nibble array per set bit of the block mask.</param>
/// <param name="TrustEdges">The vanilla trust-edges flag. It is on the wire only for 1.16 through 1.19.4 (protocols 735-762); 1.14-1.15.2 and 1.20 onward neither read nor write it, and this value is ignored there.</param>
public sealed record LightUpdateData(
    long[] SkyYMask,
    long[] BlockYMask,
    long[] EmptySkyYMask,
    long[] EmptyBlockYMask,
    IReadOnlyList<byte[]> SkyUpdates,
    IReadOnlyList<byte[]> BlockUpdates,
    bool TrustEdges = true);

// Sounds

/// <summary>A holder-or-inline sound event reference: either a registry id (<see cref="SoundId"/> &gt;= 0) or an inline sound (<see cref="InlineName"/> plus optional fixed range).</summary>
public sealed record SoundEventHolder(int SoundId, string? InlineName, float? FixedRange);

// Map data

/// <summary>A map pixel patch: a rectangular sub-region at (StartX, StartY) of size Columns x Rows, row-major map-color bytes. Absent (all zero, empty colors) when the update carries no pixel change.</summary>
public readonly record struct MapPatch(byte Columns, byte Rows, byte StartX, byte StartY, byte[] Colors);
