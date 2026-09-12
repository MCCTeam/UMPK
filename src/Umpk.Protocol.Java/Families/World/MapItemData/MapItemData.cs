using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Map item data (<c>minecraft:map_item_data</c> / 1.8 <c>map</c>).</summary>
        public static readonly PacketType<ClientboundMapItemDataPacket> MapItemData =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("map_item_data"));
    }
}

/// <summary>Map item data, into the <see cref="MapState"/> vocabulary (<see cref="MapIcon"/> for decorations). Modern (770/776) sends optional decoration and pixel patches; 1.8 always sends the icon list and only sends a patch when columns &gt; 0. <paramref name="TrackingPosition"/> is the flag 1.9 added and 1.14 replaced with <paramref name="Locked"/>; it is null outside 107-404, and <paramref name="Locked"/> is false inside it.</summary>
public sealed record ClientboundMapItemDataPacket(
    int MapId,
    byte Scale,
    bool Locked,
    IReadOnlyList<MapIcon>? Icons,
    MapPatch Patch,
    bool? TrackingPosition) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.MapItemData;
}
