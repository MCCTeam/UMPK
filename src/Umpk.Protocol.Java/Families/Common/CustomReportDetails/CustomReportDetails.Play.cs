using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Custom report details (<c>minecraft:custom_report_details</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundCustomReportDetailsPacket> CustomReportDetails =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("custom_report_details"));
    }
}

/// <summary>Custom report details (770/776): an ordered list of key/value detail pairs (server-supplied diagnostics attached to abuse reports). The wire form is a count-prefixed map (keys up to 128 chars, values up to 4096, at most 32 entries); the list preserves wire order for frame-exact round-trips since the server's source map has no defined iteration order.</summary>
public sealed record ClientboundCustomReportDetailsPacket(
    IReadOnlyList<KeyValuePair<string, string>> Details) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.CustomReportDetails;
}

/// <summary>A single custom-report detail (title, description).</summary>
public sealed record ReportDetail(string Title, string Description);
