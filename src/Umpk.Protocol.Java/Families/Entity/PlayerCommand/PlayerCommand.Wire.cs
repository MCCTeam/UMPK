using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Player command / entity action (47/770/776): entity id VarInt, action VarInt, data VarInt.</summary>
    public static readonly PacketCodec<ServerboundPlayerCommandPacket> PlayerCommand =
        PacketCodec<ServerboundPlayerCommandPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayerCommandPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteVarInt(p.Action);
                w.WriteVarInt(p.Data);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundPlayerCommandPacket(r.ReadVarInt(), r.ReadVarInt(), r.ReadVarInt()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerCommand(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.PlayerCommand)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.PlayerCommand);
    }
}
