using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayCommonCodecs
{
    /// <summary>Client command: the action ordinal as a VarInt, unchanged from 1.8 through 26.2.</summary>
    public static readonly PacketCodec<ServerboundClientCommandPacket> ClientCommand =
        PacketCodec<ServerboundClientCommandPacket>.Of(
            static (ref PacketWriter w, ServerboundClientCommandPacket p, PacketCodecContext _) =>
                w.WriteVarInt((int)p.Action),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundClientCommandPacket((ClientCommandAction)r.ReadVarInt()),
            WireShape.Of("varint"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareClientCommand(PacketBindings bindings)
    {
        // client_command / player_loaded / client_tick_end client_command is the respawn request and exists on every version; without it a dead player can never leave the death screen. player_loaded (1.21.4+) and client_tick_end (1.21.2+) are the two per-session obligations the 1.21 servers added; both are empty payloads.
        bindings.Packet(PlayPackets.Serverbound.ClientCommand)
            .From(JavaProtocols.V1_8, PlayCommonCodecs.ClientCommand);
    }
}
