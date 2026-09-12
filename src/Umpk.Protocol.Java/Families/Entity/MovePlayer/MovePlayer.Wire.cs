using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>1.8 status-only move: a single on-ground byte.</summary>
    public static readonly PacketCodec<ServerboundMovePlayerStatusPacket> MovePlayerStatusV1_8 =
        PacketCodec<ServerboundMovePlayerStatusPacket>.Of(
            static (ref PacketWriter w, ServerboundMovePlayerStatusPacket p, PacketCodecContext _) => w.WriteBool(p.OnGround),
            static (ref PacketReader r, PacketCodecContext _) => new ServerboundMovePlayerStatusPacket(r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMovePlayer(PacketBindings bindings)
    {
        // The stationary on-ground heartbeat carries one boolean through 1.16.5 and leaves the protocol at 1.17 where the dataset renames it move_player_status_only (bound separately below).
        bindings.Packet(EntityPackets.Serverbound.MovePlayerStatus)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.MovePlayerStatusV1_8);
    }
}
