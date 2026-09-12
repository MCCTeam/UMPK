using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration clear-dialog (1.21.6+): an empty packet using the play-phase wire form.</summary>
    public static readonly PacketCodec<ClientboundConfigClearDialogPacket> ClearDialog =
        PacketCodec<ClientboundConfigClearDialogPacket>.Of(
            static (ref PacketWriter _, ClientboundConfigClearDialogPacket _, PacketCodecContext _) => { },
            static (ref PacketReader _, PacketCodecContext _) => new ClientboundConfigClearDialogPacket());

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareClearDialogConfiguration(PacketBindings bindings)
    {
        // Dialogs are common packets, registered in the configuration phase as well as in play since
        // 1.21.6. clear_dialog and custom_click_action share their stream codec with the play phase;
        // show_dialog instead carries a bare NBT body with no holder id.
        bindings.Packet(LoginFamilyPackets.Config.ClearDialog)
            .From(JavaProtocols.V1_21_6, ConfigurationCodecs.ClearDialog);
    }
}
