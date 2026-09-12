using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Serverbound vehicle move, 769+ (1.21.4 on): x/y/z double, yaw/pitch float, on-ground bool. The on-ground bool is the 1.21.4 addition; every earlier protocol uses <see cref="MoveVehicleV1_9"/>.</summary>
    public static readonly PacketCodec<ServerboundMoveVehiclePacket> MoveVehicle =
        PacketCodec<ServerboundMoveVehiclePacket>.Of(
            static (ref PacketWriter w, ServerboundMoveVehiclePacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X); w.WriteDouble(p.Y); w.WriteDouble(p.Z);
                w.WriteFloat(p.Yaw); w.WriteFloat(p.Pitch);
                w.WriteBool(p.OnGround);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundMoveVehiclePacket(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadFloat(), r.ReadFloat(), r.ReadBool()));

    /// <summary>Serverbound vehicle move, 107-768 (1.9-1.21.3): x/y/z doubles then yaw/pitch floats, and nothing else.</summary>
    /// <remarks>Appending the on-ground bool that 1.21.4 introduced makes the server reject the frame as oversized.</remarks>
    public static readonly PacketCodec<ServerboundMoveVehiclePacket> MoveVehicleV1_9 =
        PacketCodec<ServerboundMoveVehiclePacket>.Of(
            static (ref PacketWriter w, ServerboundMoveVehiclePacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X); w.WriteDouble(p.Y); w.WriteDouble(p.Z);
                w.WriteFloat(p.Yaw); w.WriteFloat(p.Pitch);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundMoveVehiclePacket(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadFloat(), r.ReadFloat(), OnGround: false));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMoveVehicle(PacketBindings bindings)
    {
        // 107-768 send x/y/z doubles + yaw/pitch floats and nothing else; the trailing on-ground bool is a 1.21.4 addition. Versions through 1.21.3 retain that five-field layout, while 1.21.4 appends the bool. Appending it below 769 makes the server reject the frame as oversized.
        bindings.Packet(EntityPackets.Serverbound.MoveVehicle)
            .From(JavaProtocols.V1_9, EntityServerboundCodecs.MoveVehicleV1_9)
            .From(JavaProtocols.V1_21_4, EntityServerboundCodecs.MoveVehicle);
    }
}
