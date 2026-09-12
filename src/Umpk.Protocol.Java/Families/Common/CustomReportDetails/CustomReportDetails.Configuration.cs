using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Config
    {
        /// <summary>Custom report details (<c>minecraft:custom_report_details</c>), clientbound.</summary>
        public static readonly PacketType<ClientboundConfigCustomReportDetailsPacket> CustomReportDetails =
            new(ProtocolPhase.Configuration, PacketFlow.Clientbound, Identifier.Minecraft("custom_report_details"));
    }
}

/// <summary>Configuration custom-report-details: a map of detail title/description strings.</summary>
public sealed record ClientboundConfigCustomReportDetailsPacket(IReadOnlyList<ReportDetail> Details) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Config.CustomReportDetails;
}
