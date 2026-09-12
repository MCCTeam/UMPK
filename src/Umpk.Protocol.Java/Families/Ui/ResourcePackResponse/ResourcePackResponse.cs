using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Serverbound
    {
        /// <summary>Legacy 1.8 resource pack status (<c>minecraft:resource_pack_response</c>, 47).</summary>
        public static readonly PacketType<ServerboundLegacyResourcePackPacket> LegacyResourcePack =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("resource_pack_response"));
    }
}

/// <summary>Pre-1.20.3 resource pack status (47-404): an action (first four values), preceded by the pack hash on 47-110 only. <paramref name="Hash"/> is null from 1.10 (210) on, where the echoed hash was dropped.</summary>
public sealed record ServerboundLegacyResourcePackPacket(string? Hash, ResourcePackAction Action) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.LegacyResourcePack;
}
