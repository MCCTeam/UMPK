using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityServerboundCodecs
{
    /// <summary>Player input (1.21.2+): a single bitflags byte.</summary>
    public static readonly PacketCodec<ServerboundPlayerInputPacket> PlayerInput =
        PacketCodec<ServerboundPlayerInputPacket>.Of(
            static (ref PacketWriter w, ServerboundPlayerInputPacket p, PacketCodecContext _) =>
            {
                int flags = 0;
                if (p.Forward) flags |= 0x01;
                if (p.Backward) flags |= 0x02;
                if (p.Left) flags |= 0x04;
                if (p.Right) flags |= 0x08;
                if (p.Jump) flags |= 0x10;
                if (p.Shift) flags |= 0x20;
                if (p.Sprint) flags |= 0x40;
                w.WriteByte((byte)flags);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                byte f = r.ReadByte();
                return new ServerboundPlayerInputPacket(
                    (f & 0x01) != 0, (f & 0x02) != 0, (f & 0x04) != 0, (f & 0x08) != 0, (f & 0x10) != 0, (f & 0x20) != 0, (f & 0x40) != 0);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerInput(PacketBindings bindings)
    {
        // 107-767 send the 1.8 steer-vehicle body (strafe float, forward float, flags byte) under the renamed identifier; the seven-flag byte-only form the modern codec writes is a 1.21.2 rework. 1.21 retains the two floats and a byte with only jumping and shift flags; 1.21.2 is the first to compose the Input record's single flags byte. The flags-only record cannot carry the strafe/forward floats at all, so the era needs the steer-vehicle record: that is why the whole 107-767 span renders under minecraft:steer_vehicle in the registration pin.
        bindings.Packet(EntityPackets.Serverbound.PlayerInput)
            .FromAs(JavaEras.Combat, EntityPackets.Serverbound.SteerVehicle, EntityServerboundCodecs.SteerVehicleV1_8)
            .From(JavaEras.WideIds, EntityServerboundCodecs.PlayerInput);
    }
}
