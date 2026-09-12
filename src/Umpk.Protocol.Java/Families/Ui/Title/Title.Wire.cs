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

public static partial class TitleCodecs
{
    /// <summary>Legacy 1.8 title (47).</summary>
    public static readonly PacketCodec<ClientboundLegacyTitlePacket> LegacyTitleV1_8 =
        PacketCodec<ClientboundLegacyTitlePacket>.Of(
            static (ref PacketWriter w, ClientboundLegacyTitlePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt((int)p.Action);
                switch (p.Action)
                {
                    case LegacyTitleAction.Title:
                    case LegacyTitleAction.Subtitle:
                        WriteLegacyComponent(ref w, p.Text ?? throw new ProtocolViolationException("Title/subtitle requires a component."));
                        break;
                    case LegacyTitleAction.Times:
                        w.WriteInt(p.FadeIn);
                        w.WriteInt(p.Stay);
                        w.WriteInt(p.FadeOut);
                        break;
                    default:
                        break;
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                var action = (LegacyTitleAction)r.ReadVarInt();
                switch (action)
                {
                    case LegacyTitleAction.Title:
                    case LegacyTitleAction.Subtitle:
                        return new ClientboundLegacyTitlePacket(action, ReadLegacyComponent(ref r), 0, 0, 0);
                    case LegacyTitleAction.Times:
                        return new ClientboundLegacyTitlePacket(action, null, r.ReadInt(), r.ReadInt(), r.ReadInt());
                    default:
                        return new ClientboundLegacyTitlePacket(action, null, 0, 0, 0);
                }
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTitle(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.LegacyTitle)
            .From(JavaProtocols.V1_8, TitleCodecs.LegacyTitleV1_8);
    }
}
