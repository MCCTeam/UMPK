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
        /// <summary>Server data / MOTD (<c>minecraft:server_data</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundServerDataPacket> ServerData =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("server_data"));
    }
}

/// <summary>Server data (770/776): the MOTD component and an optional favicon PNG byte array.</summary>
public sealed record ClientboundServerDataPacket(Component Motd, byte[]? IconBytes) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ServerData;

    /// <summary>The trailing enforces-secure-chat boolean carried only by the 1.20.2-1.20.4 wire (protocols 764-767, protocols 764/765; 1.20.5 moved it into JoinGame). Always false on 1.20.5+ eras.</summary>
    public bool EnforcesSecureChat { get; init; }

    /// <summary>The verbatim MOTD JSON string as it appeared on the 1.19-1.20.2 wire (protocols 759-764), captured on decode so the JSON-era codecs can re-encode byte-exact. There the MOTD is a length-prefixed JSON string, and the vanilla server serializes a literal component in object form (<c>{"text":"..."}</c>) which the shared component serializer collapses to a bare string; preserving the original avoids that re-encode drift. Null on the 1.20.3+ NBT MOTD eras and on synthesized packets, where <see cref="Motd"/> is serialized directly.</summary>
    public string? MotdJsonVerbatim { get; init; }

    /// <summary>Whether a MOTD is present at all. The MOTD is an OPTIONAL component only on 1.19-1.19.3 (protocols 759-761); from 1.19.4 it is unconditional, so this is always true there.</summary>
    public bool HasMotd { get; init; } = true;

    /// <summary>The favicon as a base64 PNG string, which is the icon's wire form on 1.19-1.19.3 (protocols 759-761). From 1.19.4 the icon is a raw byte array carried by <see cref="IconBytes"/> instead, and this is null.</summary>
    public string? IconBase64 { get; init; }

    /// <summary>The chat-preview flag, carried only by 1.19-1.19.2 (protocols 759/760). The feature was removed at 1.19.3 and the field with it, so this is always false on every other era.</summary>
    public bool PreviewsChat { get; init; }
}
