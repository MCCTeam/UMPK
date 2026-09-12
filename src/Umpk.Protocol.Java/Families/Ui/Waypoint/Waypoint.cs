using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Tracked waypoint (<c>minecraft:waypoint</c>, 776).</summary>
        public static readonly PacketType<ClientboundWaypointPacket> Waypoint =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("waypoint"));
    }
}

/// <summary>Tracked waypoint (776): an operation and the waypoint payload. The identifier is either a uuid or a string (<c>Either&lt;UUID, String&gt;</c>: <see cref="IdentifierIsUuid"/> selects which). The icon is a style id plus an optional packed-RGB color (three bytes on the wire). The position kind selects which of the coordinate fields is meaningful (vec3i uses X/Y/Z; chunk uses X/Z; azimuth uses the angle; empty uses none).</summary>
public sealed record ClientboundWaypointPacket(
    WaypointOperation Operation,
    bool IdentifierIsUuid,
    Guid IdentifierUuid,
    string? IdentifierString,
    Identifier IconStyle,
    int? IconColor,
    WaypointKind Kind,
    int X,
    int Y,
    int Z,
    float Azimuth) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.Waypoint;
}

/// <summary>The tracked-waypoint operation (track=0, untrack=1, update=2).</summary>
public enum WaypointOperation
{
    /// <summary>Begin tracking the waypoint.</summary>
    Track = 0,

    /// <summary>Stop tracking the waypoint.</summary>
    Untrack = 1,

    /// <summary>Update the tracked waypoint.</summary>
    Update = 2,
}

/// <summary>The tracked-waypoint position kind (empty=0, block-pos=1, chunk=2, azimuth=3).</summary>
public enum WaypointKind
{
    /// <summary>No position data.</summary>
    Empty = 0,

    /// <summary>A block position (three VarInts).</summary>
    Vec3i = 1,

    /// <summary>A chunk position (two VarInts).</summary>
    Chunk = 2,

    /// <summary>A compass bearing (a float angle in radians).</summary>
    Azimuth = 3,
}
