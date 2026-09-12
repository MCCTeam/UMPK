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

public static partial class UiMiscCodecs
{
    /// <summary>Clear dialog (1.21.6+): an empty packet.</summary>
    public static readonly PacketCodec<ClientboundClearDialogPacket> ClearDialogV1_21_6 =
        PacketCodec<ClientboundClearDialogPacket>.Of(
            static (ref PacketWriter _, ClientboundClearDialogPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ClientboundClearDialogPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareClearDialogPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.ClearDialog)
            .From(JavaProtocols.V1_21_6, UiMiscCodecs.ClearDialogV1_21_6);
    }
}
