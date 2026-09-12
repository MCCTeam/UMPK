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
    // Dialogs / waypoint (introduced 1.21.6; wire-identical through 26.1/26.2). The era anchor is V1_21_6: the dialog, clear-dialog, custom-click-action, and tracked-waypoint wire shapes remain unchanged through 26.2.

    /// <summary>Show dialog (1.21.6+): a dialog holder (VarInt id; 0 = inline network-NBT body).</summary>
    public static readonly PacketCodec<ClientboundShowDialogPacket> ShowDialogV1_21_6 =
        PacketCodec<ClientboundShowDialogPacket>.Of(
            static (ref PacketWriter w, ClientboundShowDialogPacket p, PacketCodecContext _) =>
            {
                if (p.RegistryId is { } id)
                    w.WriteVarInt(id + 1);

                else
                {
                    w.WriteVarInt(0);
                    w.WriteNbt(p.InlineDialog ?? throw new ProtocolViolationException("An inline dialog requires an NBT body."), NbtWireFormat.JavaUnnamedRoot);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                return id == 0
                    ? new ClientboundShowDialogPacket(null, r.ReadNbt(NbtWireFormat.JavaUnnamedRoot))
                    : new ClientboundShowDialogPacket(id - 1, null);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareShowDialogPlay(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.ShowDialog)
            .From(JavaProtocols.V1_21_6, UiMiscCodecs.ShowDialogV1_21_6);
    }
}
