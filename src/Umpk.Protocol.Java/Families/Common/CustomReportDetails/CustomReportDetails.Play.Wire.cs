using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChatDisplayCodecs
{
    /// <summary>Custom report details (770/776): a count-prefixed map of detail key/value strings.</summary>
    public static readonly PacketCodec<ClientboundCustomReportDetailsPacket> CustomReportDetailsV1_21 =
        PacketCodec<ClientboundCustomReportDetailsPacket>.Of(
            static (ref PacketWriter w, ClientboundCustomReportDetailsPacket p, PacketCodecContext _) =>
            {
                w.WriteList(p.Details, static (ref PacketWriter sw, KeyValuePair<string, string> kv) =>
                {
                    sw.WriteString(kv.Key, 128);
                    sw.WriteString(kv.Value, 4096);
                });
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                KeyValuePair<string, string>[] details = r.ReadList(static (ref PacketReader sr) =>
                {
                    string k = sr.ReadString(128);
                    string v = sr.ReadString(4096);
                    return new KeyValuePair<string, string>(k, v);
                });
                return new ClientboundCustomReportDetailsPacket(details);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCustomReportDetailsPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.CustomReportDetails)
            .From(JavaProtocols.V1_21, ChatDisplayCodecs.CustomReportDetailsV1_21);
    }
}
