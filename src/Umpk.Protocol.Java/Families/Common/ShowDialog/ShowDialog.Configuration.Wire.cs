using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    // Dialogs (1.21.6+). show_dialog / clear_dialog / custom_click_action are common packets, so both GameProtocols and ConfigurationProtocols register them. clear_dialog and custom_click_action use the same stream codec in both phases; show_dialog does NOT (see ShowDialog below).

    /// <summary>Configuration show-dialog (1.21.6+): the dialog body as a bare unnamed-root NBT tag, with no holder id in front.</summary>
    /// <remarks>The configuration form is exactly one NBT tag. The play form instead starts with a holder VarInt, where zero means inline and positive values reference the dialog registry. Reusing the play codec here would read the tag's leading type byte as a holder id. The configuration form is unchanged from 1.21.6 through 26.2.</remarks>
    public static readonly PacketCodec<ClientboundConfigShowDialogPacket> ShowDialog =
        PacketCodec<ClientboundConfigShowDialogPacket>.Of(
            static (ref PacketWriter w, ClientboundConfigShowDialogPacket p, PacketCodecContext _) =>
                w.WriteNbt(p.Dialog, NbtWireFormat.JavaUnnamedRoot),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundConfigShowDialogPacket(r.ReadNbt(NbtWireFormat.JavaUnnamedRoot)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareShowDialogConfiguration(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.ShowDialog)
            .From(JavaProtocols.V1_21_6, ConfigurationCodecs.ShowDialog);
    }
}
