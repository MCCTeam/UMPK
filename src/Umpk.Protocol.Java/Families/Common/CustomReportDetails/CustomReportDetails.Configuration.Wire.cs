using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Custom report details

    private static readonly WriterAction<ReportDetail> WriteReportDetail =
        static (ref PacketWriter w, ReportDetail d) =>
        {
            w.WriteString(d.Title);
            w.WriteString(d.Description);
        };

    private static readonly ReaderFunc<ReportDetail> ReadReportDetail =
        static (ref PacketReader r) => new ReportDetail(r.ReadString(), r.ReadString());

    /// <summary>Configuration custom-report-details (VarInt-prefixed list of title/description pairs).</summary>
    public static readonly PacketCodec<ClientboundConfigCustomReportDetailsPacket> CustomReportDetails =
        PacketCodec<ClientboundConfigCustomReportDetailsPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigCustomReportDetailsPacket p, PacketCodecContext _) =>
                w.WriteList(p.Details, WriteReportDetail),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigCustomReportDetailsPacket(r.ReadList(ReadReportDetail)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomReportDetailsConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.CustomReportDetails)
            .From(JavaProtocols.V1_21, ConfigurationCodecs.CustomReportDetails);
    }
}
