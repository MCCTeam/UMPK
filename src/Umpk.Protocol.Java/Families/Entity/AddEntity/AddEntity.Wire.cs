using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntitySpawnCodecs
{
    /// <summary>1.8 object spawn: entity id VarInt, type byte, x/y/z fixed-point ints, pitch/yaw byte, data int, and (only if data &gt; 0) three velocity shorts.</summary>
    public static readonly PacketCodec<ClientboundAddEntityPacket> AddEntityV1_8 =
        PacketCodec<ClientboundAddEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundAddEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteByte((byte)p.TypeId);
                w.WriteInt(PackLegacyPos(p.X));
                w.WriteInt(PackLegacyPos(p.Y));
                w.WriteInt(PackLegacyPos(p.Z));
                w.WriteByte((byte)((int)Math.Round(p.XRot * 256.0f / 360.0f) & 0xFF));
                w.WriteByte((byte)((int)Math.Round(p.YRot * 256.0f / 360.0f) & 0xFF));
                w.WriteInt(p.Data);
                if (p.Data > 0)
                {
                    w.WriteShort(p.VelocityX);
                    w.WriteShort(p.VelocityY);
                    w.WriteShort(p.VelocityZ);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                byte type = r.ReadByte();
                double x = UnpackLegacyPos(r.ReadInt());
                double y = UnpackLegacyPos(r.ReadInt());
                double z = UnpackLegacyPos(r.ReadInt());
                float pitch = r.ReadByte() * 360.0f / 256.0f;
                float yaw = r.ReadByte() * 360.0f / 256.0f;
                int data = r.ReadInt();
                short vx = 0, vy = 0, vz = 0;
                if (data > 0)
                {
                    vx = r.ReadShort();
                    vy = r.ReadShort();
                    vz = r.ReadShort();
                }

                return new ClientboundAddEntityPacket(id, Guid.Empty, type, x, y, z, pitch, yaw, 0, data, vx, vy, vz, null);
            });

    /// <summary>1.9 - 1.18.2 add-entity (spawn entity/object): id VarInt, uuid, type VarInt, x/y/z double, xRot/yRot byte, data INT, xa/ya/za short. This predates the 1.19 additions of the head-yaw angle and the switch of the data field from a 32-bit int to a VarInt, so the modern <see cref="AddEntityV1_19"/> codec mis-decodes this era (it reads a head-yaw byte plus a VarInt data where the wire has none and a 4-byte int, leaving 2 trailing bytes and faulting the session on any entity spawn).</summary>
    public static readonly PacketCodec<ClientboundAddEntityPacket> AddEntityV1_9 =
        PacketCodec<ClientboundAddEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundAddEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                w.WriteVarInt(p.TypeId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteAngle(p.XRot);
                w.WriteAngle(p.YRot);
                w.WriteInt(p.Data);
                w.WriteShort(p.VelocityX);
                w.WriteShort(p.VelocityY);
                w.WriteShort(p.VelocityZ);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                int type = r.ReadVarInt();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float xRot = r.ReadAngle(), yRot = r.ReadAngle();
                int data = r.ReadInt();
                short vx = r.ReadShort(), vy = r.ReadShort(), vz = r.ReadShort();
                return new ClientboundAddEntityPacket(id, uuid, type, x, y, z, xRot, yRot, 0, data, vx, vy, vz, null);
            });

    /// <summary>1.21.5 add-entity: id VarInt, uuid, type VarInt, x/y/z double, xRot/yRot/yHeadRot byte, data VarInt, xa/ya/za short.</summary>
    public static readonly PacketCodec<ClientboundAddEntityPacket> AddEntityV1_19 =
        PacketCodec<ClientboundAddEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundAddEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                w.WriteVarInt(p.TypeId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteAngle(p.XRot);
                w.WriteAngle(p.YRot);
                w.WriteAngle(p.YHeadRot);
                w.WriteVarInt(p.Data);
                w.WriteShort(p.VelocityX);
                w.WriteShort(p.VelocityY);
                w.WriteShort(p.VelocityZ);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                int type = r.ReadVarInt();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float xRot = r.ReadAngle(), yRot = r.ReadAngle(), yHead = r.ReadAngle();
                int data = r.ReadVarInt();
                short vx = r.ReadShort(), vy = r.ReadShort(), vz = r.ReadShort();
                return new ClientboundAddEntityPacket(id, uuid, type, x, y, z, xRot, yRot, yHead, data, vx, vy, vz, null);
            });

    /// <summary>1.21.9+ add-entity: id VarInt, uuid, type VarInt, x/y/z double, then the low-precision velocity block (Vec3.LP_STREAM_CODEC), xRot/yRot/yHeadRot byte, data VarInt. The velocity block is a variable-length quantized encoding with no lossless value model, so it is carried as raw bytes (<see cref="ClientboundAddEntityPacket.ModernVelocityRaw"/>). The LpVec3 rework landed at 1.21.9, not 26.1, so protocols 773/774/775/776 all share this shape. The preceding form ends in three trailing velocity shorts; this form carries LpVec3 in the middle of the packet.</summary>
    public static readonly PacketCodec<ClientboundAddEntityPacket> AddEntityV1_21_9 =
        PacketCodec<ClientboundAddEntityPacket>.Of(
            static (ref PacketWriter w, ClientboundAddEntityPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.EntityId);
                w.WriteUuid(p.Uuid);
                w.WriteVarInt(p.TypeId);
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteBytes(p.ModernVelocityRaw ?? [0]);
                w.WriteAngle(p.XRot);
                w.WriteAngle(p.YRot);
                w.WriteAngle(p.YHeadRot);
                w.WriteVarInt(p.Data);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int id = r.ReadVarInt();
                Guid uuid = r.ReadUuid();
                int type = r.ReadVarInt();
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                byte[] lp = ReadLpVec3(ref r);
                float xRot = r.ReadAngle(), yRot = r.ReadAngle(), yHead = r.ReadAngle();
                int data = r.ReadVarInt();
                return new ClientboundAddEntityPacket(id, uuid, type, x, y, z, xRot, yRot, yHead, data, 0, 0, 0, lp);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareAddEntity(PacketBindings bindings)
    {
        // The 1.9 split-spawn form (double positions, UUID) replaces the 1.8 shape; the head-yaw + VarInt data modern form lands at 1.19, and the LpVec3 low-precision velocity rework at 1.21.9.
        bindings.Packet(EntityPackets.Clientbound.AddEntity)
            .From(JavaProtocols.V1_8, EntitySpawnCodecs.AddEntityV1_8)
            .From(JavaProtocols.V1_9, EntitySpawnCodecs.AddEntityV1_9)
            .From(JavaProtocols.V1_19, EntitySpawnCodecs.AddEntityV1_19)
            .From(JavaProtocols.V1_21_9, EntitySpawnCodecs.AddEntityV1_21_9);
    }
}
