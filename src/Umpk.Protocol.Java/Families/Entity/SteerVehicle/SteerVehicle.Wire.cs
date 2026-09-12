using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Steer vehicle, 47-767: strafe float, forward float, and flags byte. 1.8 spells the identifier <c>minecraft:steer_vehicle</c>; 1.9 renamed it to <c>minecraft:player_input</c> while keeping this body, and only 1.21.2 replaced the body with the seven-flag <c>Input</c> byte; 1.21 still uses two floats then a two-bit flags byte.</summary>
    public static readonly PacketCodec<ServerboundSteerVehiclePacket> SteerVehicleV1_8 =
        PacketCodec<ServerboundSteerVehiclePacket>.Of(
            static (ref PacketWriter w, ServerboundSteerVehiclePacket p, PacketCodecContext _) =>
            {
                w.WriteFloat(p.Strafe);
                w.WriteFloat(p.Forward);
                w.WriteByte(p.Flags);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundSteerVehiclePacket(r.ReadFloat(), r.ReadFloat(), r.ReadByte()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSteerVehicle(PacketBindings bindings)
    {
        bindings.Packet(EntityPackets.Serverbound.SteerVehicle)
            .From(JavaProtocols.V1_8, EntityServerboundCodecs.SteerVehicleV1_8);
    }
}
