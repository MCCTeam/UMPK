using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.EntityCodecShared;

namespace Umpk.Protocol.Java.Codecs;

internal static partial class EntityMoveCodecs
{
    /// <summary>1.8 player position: x/y/z doubles, yaw/pitch floats, and relative-flag byte.</summary>
    public static readonly PacketCodec<ClientboundPlayerPositionPacket> PlayerPositionV1_8 =
        PacketCodec<ClientboundPlayerPositionPacket>.Of(
            static (ref PacketWriter w, ClientboundPlayerPositionPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X); w.WriteDouble(p.Y); w.WriteDouble(p.Z);
                w.WriteFloat(p.Yaw); w.WriteFloat(p.Pitch);
                w.WriteByte(LegacyRelativeFlags(p.RelativeFlags));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundPlayerPositionPacket(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadFloat(), r.ReadFloat(), r.ReadByte(), null, null));

    /// <summary>
    /// Modern player position (1.21.2+): VarInt teleport id, PositionMoveRotation, relative bitset int.
    /// <para>The bitset is carried at its full wire width. It runs to bit 8 (<c>ROTATE_DELTA</c>), so narrowing the decoded value to a byte would silently drop <c>ROTATE_DELTA</c> and a frame carrying it would not round-trip byte-exactly.</para>
    /// </summary>
    public static readonly PacketCodec<ClientboundPlayerPositionPacket> PlayerPositionV1_21_2 =
        PacketCodec<ClientboundPlayerPositionPacket>.Of(
            static (ref PacketWriter w, ClientboundPlayerPositionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.TeleportId ?? 0);
                WritePositionMoveRotation(ref w, p.ModernValues!);
                w.WriteInt(p.RelativeFlags);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int teleportId = r.ReadVarInt();
                PositionMoveRotation values = ReadPositionMoveRotation(ref r);
                int relatives = r.ReadInt();
                return new ClientboundPlayerPositionPacket(values.Position.X, values.Position.Y, values.Position.Z, values.YRot, values.XRot, relatives, teleportId, values);
            });

    /// <summary>player_position for 1.19-1.19.3: the 1.9 form (double x/y/z + float yaw/pitch + byte relative flags + VarInt teleport id) plus a trailing <c>dismountVehicle</c> bool that was removed at 1.19.4 (762). The bool is always false in join/idle traffic, so it is read and re-emitted as false (the record does not model dismount state).</summary>
    public static readonly PacketCodec<ClientboundPlayerPositionPacket> PlayerPositionV1_17 =
        PacketCodec<ClientboundPlayerPositionPacket>.Of(
            static (ref PacketWriter w, ClientboundPlayerPositionPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteFloat(p.Yaw);
                w.WriteFloat(p.Pitch);
                w.WriteByte(LegacyRelativeFlags(p.RelativeFlags));
                w.WriteVarInt(p.TeleportId ?? 0);
                w.WriteBool(false);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadFloat(), pitch = r.ReadFloat();
                byte flags = r.ReadByte();
                int tp = r.ReadVarInt();
                r.ReadBool();
                return new ClientboundPlayerPositionPacket(x, y, z, yaw, pitch, flags, tp, null);
            });

    /// <summary>1.9-1.21.1 clientbound player position: double x/y/z, float yaw/pitch, byte relative-move flags, VarInt teleport id. Nothing trails the teleport id on this range (the dismount-vehicle bool was removed at 1.19.4; the teleport-id-first + PositionMoveRotation rework is 1.21.2+). Routed for 764/765/766/767.</summary>
    public static readonly PacketCodec<ClientboundPlayerPositionPacket> PlayerPositionV1_9 =
        PacketCodec<ClientboundPlayerPositionPacket>.Of(
            static (ref PacketWriter w, ClientboundPlayerPositionPacket p, PacketCodecContext _) =>
            {
                w.WriteDouble(p.X);
                w.WriteDouble(p.Y);
                w.WriteDouble(p.Z);
                w.WriteFloat(p.Yaw);
                w.WriteFloat(p.Pitch);
                w.WriteByte(LegacyRelativeFlags(p.RelativeFlags));
                w.WriteVarInt(p.TeleportId ?? 0);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float yaw = r.ReadFloat(), pitch = r.ReadFloat();
                byte flags = r.ReadByte();
                int tp = r.ReadVarInt();
                return new ClientboundPlayerPositionPacket(x, y, z, yaw, pitch, flags, tp, null);
            });

    /// <summary>Narrows the relative bitset for the pre-1.21.2 wires, which carry a single byte with five defined relative-movement flags (bits 0-4). A value with a bit above 7 set has no representation there, so it is rejected rather than silently truncated. Truncation would lose relative-movement flags that the modern wire can represent.</summary>
    private static byte LegacyRelativeFlags(int flags) =>
        (uint)flags <= byte.MaxValue
            ? (byte)flags
            : throw new ProtocolViolationException(
                "The pre-1.21.2 player_position relative-flag field is one byte; this bitset does not fit.");

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerPosition(PacketBindings bindings)
    {
        // The trailing dismountVehicle bool exists only on 1.17-1.19.3 (added 1.17, removed 1.19.4); the teleport-id-first + PositionMoveRotation form arrives at 1.21.2.
        bindings.Packet(EntityPackets.Clientbound.PlayerPosition)
            .From(JavaProtocols.V1_8, EntityMoveCodecs.PlayerPositionV1_8)
            .From(JavaProtocols.V1_9, EntityMoveCodecs.PlayerPositionV1_9)
            .From(JavaProtocols.V1_17, EntityMoveCodecs.PlayerPositionV1_17)
            .From(JavaProtocols.V1_19_4, EntityMoveCodecs.PlayerPositionV1_9)
            .From(JavaProtocols.V1_21_2, EntityMoveCodecs.PlayerPositionV1_21_2);
    }
}
